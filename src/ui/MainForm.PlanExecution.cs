using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp
{
    public partial class MainForm
    {
        async void ExecutePlanDraft()
        {
            string raw = input == null ? "" : input.Text.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                AppendChat("Error", Tr("Paste or generate a plan in the input box first.",
                    "请先在输入框里粘贴或生成一份计划。",
                    "請先在輸入框裡貼上或產生一份計畫。"), Color.FromArgb(190, 40, 40));
                return;
            }

            var plan = new AgentPlanParser().Parse(raw);
            var options = new AgentPlanCommandMapperOptions();
            options.TaskId = currentTaskId;
            options.TaskTitle = currentTaskTitle;
            options.DefaultOutputDirectory = Path.Combine(configDir, "generated");
            options.DefaultText = FirstPlanExecutionText(raw);
            var mapping = new AgentPlanCommandMapper().Map(plan, options);

            if (!mapping.HasCommands)
            {
                AppendChat("Error", Tr("No executable steps were recognized in this plan.",
                    "这份计划里没有识别到可执行步骤。",
                    "這份計畫裡沒有識別到可執行步驟。"), Color.FromArgb(190, 40, 40));
                return;
            }

            string review = BuildPlanExecutionReview(mapping);
            bool approved = ShowApprovalCard("AgentPlan",
                Tr("Confirm plan execution", "确认执行计划", "確認執行計畫"),
                Tr("Execute", "执行", "執行"),
                Tr("Agent pipeline", "Agent 执行流水线", "Agent 執行流水線"),
                Tr("Current task plan", "当前任务计划", "目前任務計畫"),
                Tr("Steps may write files, run plugins, use network search, or affect the Windows desktop according to their permissions.",
                    "步骤可能按各自权限写入文件、运行插件、使用网络搜索，或影响 Windows 桌面。",
                    "步驟可能依各自權限寫入檔案、執行外掛、使用網路搜尋，或影響 Windows 桌面。"),
                review,
                plan.ToReviewMarkdown(),
                "");
            if (!approved)
            {
                RecordAction("AgentPlan", "cancelled", "Plan execution cancelled", "");
                return;
            }

            var pipeline = BuildPlanExecutionPipeline();
            var sb = new StringBuilder();
            int ok = 0;
            for (int i = 0; i < mapping.Commands.Count; i++)
            {
                var command = mapping.Commands[i];
                var result = await pipeline.RunAsync(command, CancellationToken.None);
                if (result.Status == CommandStatus.Success)
                {
                    ok++;
                    sb.AppendLine("OK: " + command.DisplaySummary);
                    if (!string.IsNullOrWhiteSpace(result.ResultPath)) sb.AppendLine("  " + result.ResultPath);
                    if (!string.IsNullOrWhiteSpace(result.OutputText)) sb.AppendLine("  " + TrimPlanExecutionText(result.OutputText, 1200));
                    RecordAction(command.CommandType, "success", command.DisplaySummary, result.ResultPath ?? "");
                    continue;
                }

                string error = result.Status == CommandStatus.Cancelled ? "cancelled" :
                    (result.Status == CommandStatus.Denied ? result.ErrorMessage : result.ErrorMessage);
                sb.AppendLine("STOP: " + command.DisplaySummary);
                sb.AppendLine("  " + (error ?? result.Status.ToString()));
                RecordAction(command.CommandType, result.Status.ToString().ToLowerInvariant(), command.DisplaySummary + " | " + (error ?? ""), "");
                break;
            }

            SetCurrentTaskStatus(ok == mapping.Commands.Count ? "ready_for_review" : "needs_input", "Plan execution: " + ok + "/" + mapping.Commands.Count, true);
            AppendChat("ZhuaQian", Tr("Plan execution result:", "计划执行结果：", "計畫執行結果：") + "\r\n" + sb.ToString().Trim(), Color.FromArgb(0, 130, 80));
            LogAction("AgentPlan", "Executed " + ok + " / " + mapping.Commands.Count + " mapped plan steps");
        }

        AgentPipeline BuildPlanExecutionPipeline()
        {
            var gate = PermissionGate.FromJson(permGate.ToJson());
            gate.Set("permFileWrite", permFileWrite ? PermissionLevel.Allow : PermissionLevel.Deny);
            gate.Set("permFileMoveDelete", permFileMoveDelete ? PermissionLevel.Ask : PermissionLevel.Deny);
            gate.Set("permPluginRun", permPluginRun ? PermissionLevel.Ask : PermissionLevel.Deny);
            gate.Set("permProcessManage", permProcessManage ? PermissionLevel.Ask : PermissionLevel.Deny);
            gate.Set("permAutomationInput", permAutomationInput ? PermissionLevel.Ask : PermissionLevel.Deny);
            gate.Set("permNetworkUpload", permNetworkUpload ? PermissionLevel.Allow : PermissionLevel.Deny);

            var pipeline = agentPipelineFactory.Create(gate, pluginDir, allowAdvancedPlugins);
            pipeline.RequestApproval = command => ConfirmPlanExecutionStep(command);
            return pipeline;
        }

        bool ConfirmPlanExecutionStep(IAgentCommand command)
        {
            return ShowApprovalCard(command.CommandType,
                Tr("Confirm plan step", "确认计划步骤", "確認計畫步驟"),
                Tr("Execute", "执行", "執行"),
                command.PermissionName,
                command.Target,
                Tr("This step is part of the approved plan and still requires its own guarded permission.",
                    "这是已批准计划中的一步，但仍需要单独通过对应权限保护。",
                    "這是已核准計畫中的一步，但仍需要單獨通過對應權限保護。"),
                command.DisplaySummary,
                BuildCommandDetail(command),
                command.Target);
        }

        string BuildPlanExecutionReview(AgentPlanCommandMapping mapping)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Executable steps: " + mapping.Commands.Count);
            for (int i = 0; i < mapping.Commands.Count; i++)
            {
                var command = mapping.Commands[i];
                sb.AppendLine((i + 1).ToString() + ". " + command.CommandType + " - " + command.DisplaySummary);
                if (!string.IsNullOrWhiteSpace(command.Target)) sb.AppendLine("   Target: " + command.Target);
            }
            if (mapping.Skipped.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Skipped:");
                foreach (string skipped in mapping.Skipped) sb.AppendLine("- " + skipped);
            }
            return sb.ToString().Trim();
        }

        string BuildCommandDetail(IAgentCommand command)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Command: " + command.CommandType);
            sb.AppendLine("Permission: " + command.PermissionName);
            sb.AppendLine("Target: " + command.Target);
            foreach (var kv in command.Parameters)
                sb.AppendLine(kv.Key + ": " + Convert.ToString(kv.Value));
            return sb.ToString().Trim();
        }

        string FirstPlanExecutionText(string raw)
        {
            string last = GetLastModelReply();
            if (!string.IsNullOrWhiteSpace(last)) return last;
            return raw ?? "";
        }

        string TrimPlanExecutionText(string text, int maxChars)
        {
            if (text == null) return "";
            if (maxChars <= 0 || text.Length <= maxChars) return text;
            return text.Substring(0, maxChars) + "\r\n[content truncated]";
        }
    }
}
