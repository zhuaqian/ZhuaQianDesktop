using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZhuaQianDesktopApp.Tools;

namespace ZhuaQianDesktopApp.Agent
{
    // ============================================================================
    // Task-completion loop: perceive -> decide -> act -> verify, repeated until
    // the policy says "done" or a step budget is exhausted.
    //
    // This is the missing half that turns ZhuaQian from a "report/tool" agent
    // into one that can *complete work* in a browser or on the desktop. The loop
    // itself is environment- and policy-agnostic; the LLM plugs in through
    // ITaskPolicy, and the browser/desktop plug in through IEnvironment.
    //
    // NOTE: in production the env.Actuate step should route through the pipeline
    // (PermissionGate -> Executor -> AuditLog) so every action is gated + logged.
    // The adapters below call the clients directly for the closed-loop demo; the
    // comment in RunAsync shows where to insert the gated call.
    // ============================================================================

    // What the policy observes at each step.
    public sealed class Observation
    {
        public DateTime At = DateTime.Now;
        public string Url = "";
        public string Title = "";
        public string Text = "";
        public List<DomElement> Dom = new List<DomElement>();
        public string ScreenshotPath = "";
    }

    // A single action the policy wants executed. Shaped like an AgentCommand so
    // it can be forwarded to the pipeline unchanged when gating is enabled.
    public sealed class AgentAction
    {
        public string CommandType = "";
        public string Target = "";
        public Dictionary<string, object> Parameters = new Dictionary<string, object>();
        public AgentAction() { }
        public AgentAction(string commandType, string target = "", Dictionary<string, object> parameters = null)
        {
            CommandType = commandType; Target = target ?? "";
            Parameters = parameters ?? new Dictionary<string, object>();
        }
    }

    public sealed class ActionOutcome
    {
        public bool Ok;
        public string Detail = "";
        public bool Fatal; // unrecoverable -> stop the loop
        public static ActionOutcome Done(bool ok, string detail, bool fatal = false)
        { return new ActionOutcome { Ok = ok, Detail = detail ?? "", Fatal = fatal }; }
    }

    // The environment the agent acts inside (a browser tab, or the desktop).
    public interface IEnvironment
    {
        Task<Observation> ObserveAsync(CancellationToken token);
        Task<ActionOutcome> ActuateAsync(AgentAction action, CancellationToken token);
        Task TeardownAsync();
    }

    // The brain: given the latest observation + history, decide the next action
    // or declare the task complete. The LLM implementation goes here.
    public interface ITaskPolicy
    {
        Task<PolicyDecision> DecideAsync(Observation obs, List<StepRecord> history, CancellationToken token);
    }

    public sealed class PolicyDecision
    {
        public bool Done;
        public string Reasoning = "";
        public AgentAction NextAction;
    }

    public sealed class StepRecord
    {
        public int Index;
        public Observation Observation;
        public AgentAction Action;
        public ActionOutcome Outcome;
        public string Reasoning = "";
    }

    public sealed class TaskRunReport
    {
        public bool Success;
        public string Goal = "";
        public int StepsTaken;
        public string FinalReason = "";
        public List<StepRecord> History = new List<StepRecord>();
        public string Summary()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Task: " + Goal);
            sb.AppendLine("Success: " + Success + "  Steps: " + StepsTaken);
            sb.AppendLine("Reason: " + FinalReason);
            for (int i = 0; i < History.Count; i++)
            {
                var s = History[i];
                sb.AppendLine("  #" + (i + 1) + " " + (s.Action != null ? s.Action.CommandType : "?") +
                    " -> " + (s.Outcome != null ? (s.Outcome.Ok ? "OK" : "FAIL") + " " + s.Outcome.Detail : ""));
            }
            return sb.ToString().Trim();
        }
    }

    public sealed class TaskAgentRunner
    {
        public int MaxSteps = 15;
        public int ObserveSettleMs = 500; // brief wait so the page settles before observing

        public async Task<TaskRunReport> RunAsync(string goal, IEnvironment env, ITaskPolicy policy, CancellationToken token = default(CancellationToken))
        {
            var report = new TaskRunReport { Goal = goal ?? "" };
            var history = new List<StepRecord>();
            try
            {
                for (int step = 0; step < MaxSteps; step++)
                {
                    if (ObserveSettleMs > 0) await Task.Delay(ObserveSettleMs, token).ConfigureAwait(false);
                    Observation obs = await env.ObserveAsync(token).ConfigureAwait(false);

                    PolicyDecision decision = await policy.DecideAsync(obs, history, token).ConfigureAwait(false);
                    if (decision.Done)
                    {
                        report.Success = true;
                        report.FinalReason = decision.Reasoning ?? "policy declared done";
                        report.StepsTaken = history.Count;
                        return report;
                    }

                    var rec = new StepRecord { Index = step, Observation = obs, Action = decision.NextAction, Reasoning = decision.Reasoning ?? "" };

                    // ---- GATED PRODUCTION PATH (sketch) ----
                    // var cmd = new AgentCommand(decision.NextAction.CommandType, "permAutomationInput",
                    //     taskId, decision.NextAction.Target, decision.NextAction.CommandType, decision.NextAction.Parameters);
                    // var res = pipeline.Run(cmd); // PermissionGate -> Executor -> AuditLog
                    // rec.Outcome = ActionOutcome.Done(res.Status == CommandStatus.Success, res.OutputText ?? res.ErrorMessage);
                    // ---- demo path: call the environment directly ----
                    rec.Outcome = await env.ActuateAsync(decision.NextAction, token).ConfigureAwait(false);

                    history.Add(rec);
                    if (rec.Outcome.Fatal)
                    {
                        report.Success = false;
                        report.FinalReason = "fatal action failure: " + rec.Outcome.Detail;
                        report.StepsTaken = history.Count;
                        return report;
                    }
                }
                report.Success = false;
                report.FinalReason = "step budget (" + MaxSteps + ") exhausted without completion";
                report.StepsTaken = history.Count;
                return report;
            }
            finally
            {
                report.History = history;
                try { env.TeardownAsync().GetAwaiter().GetResult(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("TaskAgentRunner teardown: " + ex.Message); }
            }
        }
    }

    // ---- Browser environment adapter: wraps BrowserAgentClient ----------------

    public sealed class BrowserEnvironment : IEnvironment
    {
        readonly BrowserAgentClient client;
        public BrowserEnvironment(BrowserAgentClient client) { this.client = client; }

        public async Task<Observation> ObserveAsync(CancellationToken token)
        {
            var obs = new Observation();
            obs.Url = client.CurrentUrl;
            obs.Title = await client.GetTitleAsync(token).ConfigureAwait(false);
            obs.Text = await client.SnapshotTextAsync(token).ConfigureAwait(false);
            obs.Dom = await client.DomSnapshotAsync(token).ConfigureAwait(false);
            return obs;
        }

        public async Task<ActionOutcome> ActuateAsync(AgentAction action, CancellationToken token)
        {
            if (action == null) return ActionOutcome.Done(false, "null action", true);
            string a = (action.CommandType ?? "").ToLowerInvariant();
            try
            {
                BrowserActionResult r;
                switch (a)
                {
                    case "navigate": r = await client.NavigateAsync(Str(action, "url") ?? action.Target, Int(action, "timeoutMs", 30000), token).ConfigureAwait(false); break;
                    case "click": r = await client.ClickAsync(Str(action, "selector"), Int(action, "timeoutMs", 10000), token).ConfigureAwait(false); break;
                    case "clicktext": r = await client.ClickTextAsync(Str(action, "text"), Int(action, "timeoutMs", 10000), token).ConfigureAwait(false); break;
                    case "fill": r = await client.FillAsync(Str(action, "selector"), Str(action, "value"), Int(action, "timeoutMs", 10000), token).ConfigureAwait(false); break;
                    case "type": r = await client.TypeAsync(Str(action, "selector"), Str(action, "text"), Int(action, "timeoutMs", 10000), token).ConfigureAwait(false); break;
                    case "press": r = await client.PressKeyAsync(Str(action, "key") ?? "Enter", token).ConfigureAwait(false); break;
                    case "submit": r = await client.SubmitAsync(Str(action, "form") ?? "form", token).ConfigureAwait(false); break;
                    case "screenshot": r = BrowserActionResult.Success("screenshot"); await client.ScreenshotAsync(Str(action, "path"), token).ConfigureAwait(false); break;
                    case "wait": await Task.Delay(Int(action, "ms", 1000), token).ConfigureAwait(false); r = BrowserActionResult.Success("waited"); break;
                    default: return ActionOutcome.Done(false, "unsupported browser action: " + a, false);
                }
                return ActionOutcome.Done(r.Ok, r.Detail, !r.Ok);
            }
            catch (Exception ex) { return ActionOutcome.Done(false, a + " threw: " + ex.Message, true); }
        }

        public Task TeardownAsync() { return client.StopAsync(); }

        static string Str(AgentAction a, string k) { object v; return a.Parameters.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }
        static int Int(AgentAction a, string k, int d) { int n; return int.TryParse(Str(a, k), out n) ? n : d; }
    }

    // ---- Desktop environment adapter: screenshot (observe) + ComputerControl (act)
    public sealed class DesktopEnvironment : IEnvironment
    {
        readonly DesktopScreenCapture capturer;
        readonly ComputerControlExecutor actuator;
        readonly string shotDir;
        public DesktopEnvironment(DesktopScreenCapture capturer, ComputerControlExecutor actuator, string shotDir = null)
        {
            this.capturer = capturer ?? new DesktopScreenCapture();
            this.actuator = actuator ?? new ComputerControlExecutor();
            this.shotDir = shotDir ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zq-desktop-loop");
        }

        public Task<Observation> ObserveAsync(CancellationToken token)
        {
            var obs = new Observation();
            try
            {
                System.IO.Directory.CreateDirectory(shotDir);
                string path = System.IO.Path.Combine(shotDir, "desktop_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                capturer.Capture(path);
                obs.ScreenshotPath = path;
                obs.Text = "screenshot: " + path;
            }
            catch (Exception ex) { obs.Text = "observe failed: " + ex.Message; }
            return Task.FromResult(obs);
        }

        public Task<ActionOutcome> ActuateAsync(AgentAction action, CancellationToken token)
        {
            if (action == null) return Task.FromResult(ActionOutcome.Done(false, "null action", true));
            var cmd = new AgentCommand(action.CommandType, "permAutomationInput", "", action.Target, action.CommandType, action.Parameters);
            CommandResult res = actuator.Execute(cmd);
            bool ok = res.Status == CommandStatus.Success;
            return Task.FromResult(ActionOutcome.Done(ok, res.OutputText ?? res.ErrorMessage ?? "", !ok));
        }

        public Task TeardownAsync() { return Task.CompletedTask; }
    }

    // ---- Scripted policy (deterministic demo, no LLM needed) ------------------
    // Runs a fixed ordered plan; declares done after the last step. Useful for
    // tests and as the fallback when no model is configured.
    public sealed class ScriptedTaskPolicy : ITaskPolicy
    {
        readonly List<AgentAction> plan = new List<AgentAction>();
        public void Add(AgentAction a) { plan.Add(a); }
        public Task<PolicyDecision> DecideAsync(Observation obs, List<StepRecord> history, CancellationToken token)
        {
            if (history.Count >= plan.Count)
                return Task.FromResult(new PolicyDecision { Done = true, Reasoning = "plan finished" });
            var next = plan[history.Count];
            return Task.FromResult(new PolicyDecision { Done = false, Reasoning = "step " + (history.Count + 1) + "/" + plan.Count + ": " + next.CommandType, NextAction = next });
        }
    }

    // ---- Delegate policy (plugs in an LLM or any external brain) -------------
    public sealed class DelegateTaskPolicy : ITaskPolicy
    {
        readonly Func<Observation, List<StepRecord>, PolicyDecision> fn;
        public DelegateTaskPolicy(Func<Observation, List<StepRecord>, PolicyDecision> fn) { this.fn = fn; }
        public Task<PolicyDecision> DecideAsync(Observation obs, List<StepRecord> history, CancellationToken token)
        {
            return Task.FromResult(fn(obs, history));
        }
    }
}
