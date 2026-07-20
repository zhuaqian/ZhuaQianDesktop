using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Tools;

namespace ZhuaQianDesktopApp
{
    // Roadmap H module composition root: wire the autonomous task loop
    // (LlmTaskPolicy as the decision brain + TaskAgentGating.GatedActuator as the
    // audited actuation) into a runnable UI entry. Every action the loop decides
    // runs through Command -> PermissionGate -> Executor -> AuditLog, and each
    // risky step shows the same high-risk approval card used everywhere else.
    public partial class MainForm
    {
        public async Task<TaskRunReport> RunTaskAgentAsync(string goal, bool useBrowser, CancellationToken token = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(goal)) goal = "";
            if (LlmBridge.Chat == null) LlmBridge.Chat = m => providerManager.SendAsync(m);

            // Adapter: LlmTaskPolicy speaks (prompt -> reply); the app speaks
            // native message lists. Wrap the single prompt as one user turn.
            Func<string, CancellationToken, Task<string>> chat =
                async (prompt, ct) =>
                {
                    var msgs = new List<Dictionary<string, object>>
                    {
                        new Dictionary<string, object> { { "role", "user" }, { "content", prompt } }
                    };
                    return await LlmBridge.Chat(msgs).ConfigureAwait(false);
                };

            var policy = new LlmTaskPolicy(chat, goal);

            // Fresh gate => every action the loop may take is escalated to Ask,
            // so the user approves each risky step (trust-infra first).
            var gate = new PermissionGate();
            var pipeline = agentPipelineFactory.Create(gate, pluginDir, allowAdvancedPlugins);

            // Route approvals back to the UI thread so the card can be shown even
            // though the loop's actuation runs off the UI thread.
            pipeline.RequestApproval = cmd =>
            {
                Func<bool> show = () => ShowApprovalCard(
                    cmd.CommandType,
                    Tr("Confirm autonomous task action", "确认自主任务动作", "確認自主任務動作"),
                    Tr("Execute", "执行", "執行"),
                    cmd.PermissionName,
                    cmd.Target,
                    Tr("The autonomous task loop requests this action. It runs through the audited pipeline (PermissionGate -> Executor -> AuditLog).",
                       "自主任务循环请求执行此动作，已通过审计管道（权限门禁→执行器→审计日志）。",
                       "自主任務循環請求執行此動作，已透過審計管道（權限門禁→執行器→審計日誌）。"),
                    cmd.DisplaySummary,
                    "Command: " + cmd.CommandType + "\r\nTarget: " + (cmd.Target ?? ""),
                    "");
                if (this.InvokeRequired) return (bool)this.Invoke(show);
                return show();
            };

            IEnvironment env;
            Func<AgentAction, CancellationToken, Task<ActionOutcome>> actuate;
            if (useBrowser)
            {
                var session = BrowserSessionHub.Client;
                try { await session.StartAsync(token: token).ConfigureAwait(false); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("TaskAgent browser start: " + ex.Message); }
                env = new BrowserEnvironment(session);
                actuate = TaskAgentGating.GatedActuator(pipeline, "task-agent", session);
            }
            else
            {
                env = new DesktopEnvironment(new DesktopScreenCapture(), new ComputerControlExecutor());
                actuate = TaskAgentGating.GatedActuator(pipeline, "task-agent");
            }

            var runner = new TaskAgentRunner();
            TaskRunReport report;
            try
            {
                report = await runner.RunAsync(goal, env, policy, token, actuate).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                report = new TaskRunReport { Goal = goal, Success = false, FinalReason = "error: " + ex.Message, StepsTaken = 0 };
            }

            AppendChat("ZhuaQian",
                Tr("Autonomous task finished. Success: ", "自主任务结束。成功：", "自主任務結束。成功：") + report.Success.ToString() +
                "\r\n" + (report.FinalReason ?? "") + "\r\n" + Tr("Steps: ", "步数：", "步數：") + report.StepsTaken.ToString(),
                report.Success ? ThemeManager.Success : ThemeManager.Error);
            return report;
        }
    }
}
