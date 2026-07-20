using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
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

        public async Task<TaskRunReport> RunAsync(string goal, IEnvironment env, ITaskPolicy policy, CancellationToken token = default(CancellationToken), Func<AgentAction, CancellationToken, Task<ActionOutcome>> actuateOverride = null)
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

                    // ---- Production path: route actuation through the audited
                    // AgentPipeline (PermissionGate -> Executor -> AuditLog) when a
                    // gated actuator is supplied; otherwise call the environment
                    // directly (closed-loop demo path).
                    ActionOutcome outcome;
                    if (actuateOverride != null)
                        outcome = await actuateOverride(decision.NextAction, token).ConfigureAwait(false);
                    else
                        outcome = await env.ActuateAsync(decision.NextAction, token).ConfigureAwait(false);
                    rec.Outcome = outcome;

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

    // ---- LLM-backed policy: the "brain" the task loop was missing ------------
    // Given the latest observation + step history, it asks a model to return a
    // structured decision (JSON) naming the next action or declaring done.
    //
    // It depends ONLY on a chat function (prompt -> reply text), so it stays
    // fully decoupled from the concrete provider/UI. The host injects whatever
    // chat backend it has (providerManager.SendAsync, a streaming bridge, ...).
    public sealed class LlmTaskPolicy : ITaskPolicy
    {
        readonly Func<string, CancellationToken, Task<string>> chat;
        readonly string goal;
        readonly string systemPrompt;
        readonly bool strict;
        readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 1 << 20 };

        public LlmTaskPolicy(Func<string, CancellationToken, Task<string>> chat, string goal = null, string systemPrompt = null, bool strict = false)
        {
            this.chat = chat ?? throw new ArgumentNullException(nameof(chat));
            this.goal = goal ?? "";
            this.systemPrompt = systemPrompt ?? DefaultSystemPrompt();
            this.strict = strict;
        }

        public async Task<PolicyDecision> DecideAsync(Observation obs, List<StepRecord> history, CancellationToken token)
        {
            string reply = await chat(BuildPrompt(obs, history), token).ConfigureAwait(false);
            return ParseDecision(reply, history);
        }

        string BuildPrompt(Observation obs, List<StepRecord> history)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(systemPrompt);
            sb.AppendLine();
            sb.AppendLine("GOAL: " + (string.IsNullOrWhiteSpace(goal) ? "(none stated)" : goal));
            sb.AppendLine();
            sb.AppendLine("CURRENT STATE:");
            sb.AppendLine("  url: " + (obs != null ? obs.Url : ""));
            sb.AppendLine("  title: " + (obs != null ? obs.Title : ""));
            sb.AppendLine("  screenshot: " + (obs != null ? obs.ScreenshotPath : ""));
            string text = obs != null ? obs.Text : "";
            sb.AppendLine("  text: " + (text != null && text.Length > 1500 ? text.Substring(0, 1500) + "..." : text));
            sb.AppendLine();
            sb.AppendLine("RECENT STEPS (most recent last):");
            if (history != null)
            {
                int start = Math.Max(0, history.Count - 8);
                for (int i = start; i < history.Count; i++)
                {
                    var s = history[i];
                    sb.AppendLine("  #" + (i + 1) + " " + (s.Action != null ? s.Action.CommandType : "?") +
                        " -> " + (s.Outcome != null ? (s.Outcome.Ok ? "OK" : "FAIL") + " " + s.Outcome.Detail : "") +
                        " | " + (s.Reasoning ?? ""));
                }
            }
            sb.AppendLine();
            sb.AppendLine("Respond with a single JSON object and nothing else:");
            sb.AppendLine("{ \"done\": false, \"reasoning\": \"...\", \"action\": { \"commandType\": \"click\", \"target\": \"#id\", \"parameters\": { \"selector\": \"#id\" } } }");
            sb.AppendLine("Allowed commandType values: navigate, click, clicktext, fill, type, press, submit, screenshot, wait, dom, text, title, url, start, stop.");
            sb.AppendLine("Set \"done\": true (with a \"reasoning\") only when the GOAL is fully achieved. Otherwise pick the single next action.");
            return sb.ToString();
        }

        PolicyDecision ParseDecision(string reply, List<StepRecord> history)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return Fallback("model returned empty reply");

            // Strip markdown fences / prose; keep the first {...} block.
            string json = ExtractJson(reply);
            if (json == null)
                return Fallback("no JSON object found in model reply");

            object parsed;
            try { parsed = serializer.DeserializeObject(json); }
            catch (Exception ex) { return Fallback("JSON parse error: " + ex.Message); }

            var root = parsed as Dictionary<string, object>;
            if (root == null)
                return Fallback("root is not a JSON object");

            object doneObj;
            bool done = root.TryGetValue("done", out doneObj) && doneObj is bool && (bool)doneObj;
            if (done)
                return new PolicyDecision { Done = true, Reasoning = Str(root, "reasoning", "policy declared done") };

            object actionObj;
            var action = root.TryGetValue("action", out actionObj) ? actionObj as Dictionary<string, object> : null;
            if (action == null)
                return Fallback("missing 'action' object in decision");

            string commandType = Str(action, "commandType", "");
            if (string.IsNullOrWhiteSpace(commandType))
                return Fallback("missing 'commandType' in action");

            var parameters = action.TryGetValue("parameters", out object p) ? p as Dictionary<string, object> : null;
            var paramCopy = new Dictionary<string, object>(parameters ?? new Dictionary<string, object>());

            return new PolicyDecision
            {
                Done = false,
                Reasoning = Str(root, "reasoning", ""),
                NextAction = new AgentAction(commandType, Str(action, "target", ""), paramCopy)
            };
        }

        PolicyDecision Fallback(string reason)
        {
            if (strict) throw new FormatException("LlmTaskPolicy: " + reason);
            // Non-strict: stay alive with a 1s wait so the loop can re-observe and
            // recover on the next step (bounded by MaxSteps).
            return new PolicyDecision
            {
                Done = false,
                Reasoning = "parse fallback: " + reason,
                NextAction = new AgentAction("wait", "", new Dictionary<string, object> { { "ms", "1000" } })
            };
        }

        static string DefaultSystemPrompt()
        {
            return "You are the decision-making brain of an autonomous desktop/browser agent. " +
                   "Each turn you receive the current page/desktop state and the recent action history, " +
                   "and you must decide the single next action that makes progress toward the GOAL, " +
                   "or declare the goal complete. Prefer robust selectors (id/css) over positional clicks. " +
                   "Never invent actions outside the allowed commandType list.";
        }

        static string ExtractJson(string reply)
        {
            int first = reply.IndexOf('{');
            int last = reply.LastIndexOf('}');
            if (first < 0 || last <= first) return null;
            return reply.Substring(first, last - first + 1);
        }

        static string Str(Dictionary<string, object> d, string key, string def)
        {
            if (d == null || !d.TryGetValue(key, out object v) || v == null) return def;
            return v.ToString();
        }
    }

    // ---- Production glue: route task actions through the audited pipeline -----
    // Wraps an AgentPipeline into the actuate function RunAsync accepts, so every
    // action is gated by PermissionGate and logged to AuditLog. Browser task verbs
    // are mapped onto the registered "BrowserControl" executor (sharing the
    // environment's BrowserAgentClient session when provided); desktop/other verbs
    // are forwarded verbatim. If a verb has no registered executor the pipeline
    // returns a non-fatal failure and the loop continues (bounded by MaxSteps).
    public static class TaskAgentGating
    {
        public static Func<AgentAction, CancellationToken, Task<ActionOutcome>> GatedActuator(AgentPipeline pipeline, string taskId, BrowserAgentClient sharedSession = null)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (sharedSession != null)
                pipeline.Register(new BrowserControlExecutor(sharedSession, null));

            return (action, token) =>
            {
                if (action == null) return Task.FromResult(ActionOutcome.Done(false, "null action", true));
                string verb = (action.CommandType ?? "").ToLowerInvariant();

                if (IsBrowserVerb(verb))
                {
                    var mapped = new Dictionary<string, object>(action.Parameters);
                    mapped["action"] = verb;
                    string target = action.Target ?? "";
                    if (verb == "navigate" && string.IsNullOrEmpty(target)) target = Str(action, "url");
                    if (verb == "press" && string.IsNullOrEmpty(target)) target = Str(action, "key");
                    var cmd = new AgentCommand("BrowserControl", "permNetworkUpload", taskId, target, "task:" + verb, mapped);
                    CommandResult res = pipeline.Run(cmd);
                    bool ok = res.Status == CommandStatus.Success;
                    return Task.FromResult(ActionOutcome.Done(ok, res.OutputText ?? res.ErrorMessage ?? "", !ok));
                }

                // Desktop / generic: forward verbatim (caller must have registered
                // the matching executor, e.g. "ComputerControl").
                var cmd2 = new AgentCommand(action.CommandType, "permAutomationInput", taskId, action.Target, "task:" + action.CommandType, action.Parameters);
                CommandResult res2 = pipeline.Run(cmd2);
                bool ok2 = res2.Status == CommandStatus.Success;
                return Task.FromResult(ActionOutcome.Done(ok2, res2.OutputText ?? res2.ErrorMessage ?? "", !ok2));
            };
        }

        static bool IsBrowserVerb(string verb)
        {
            return verb == "navigate" || verb == "click" || verb == "clicktext" || verb == "fill"
                || verb == "type" || verb == "press" || verb == "submit" || verb == "screenshot"
                || verb == "wait" || verb == "dom" || verb == "text" || verb == "title"
                || verb == "url" || verb == "start" || verb == "stop";
        }

        static string Str(AgentAction a, string k) { object v; return a.Parameters.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }
    }
}
