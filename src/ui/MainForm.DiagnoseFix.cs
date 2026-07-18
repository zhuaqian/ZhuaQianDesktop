using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Agent;

namespace ZhuaQianDesktopApp
{
    // Epic D: natural-language entry point for the coding-agent closed loop.
    // When the user says "fix build" / "diagnose and fix" / "check compile",
    // this routes the request through the DiagnoseFix executor (which runs
    // CodingLoop internally) and surfaces the report in DiagnoseFixDialog.
    // All side effects (build/test commands, file patches, git operations)
    // go through the AgentPipeline permission gate.
    public partial class MainForm
    {
        bool LooksLikeDiagnoseFixRequest(string lower)
        {
            return ContainsAny(lower,
                "诊断修复", "診斷修復", "诊断并修复", "診斷並修復",
                "修复编译", "修復編譯", "修复构建", "修復構建",
                "检查编译", "檢查編譯", "检查构建", "檢查構建",
                "检查项目", "檢查專案", "修复项目", "修復專案",
                "编译失败", "編譯失敗", "构建失败", "構建失敗",
                "fix build", "fix compile", "fix project",
                "diagnose and fix", "diagnose fix", "diagnose project",
                "check build", "check compile", "why build fail",
                "build failed", "build error", "compile error",
                "why won't it compile", "why won t it compile");
        }

        async void ExecuteDiagnoseFix(string text)
        {
            string root = ExtractNaturalTarget(text, new string[] {
                "诊断修复", "診斷修復", "诊断并修复", "診斷並修復",
                "修复编译", "修復編譯", "修复构建", "修復構建",
                "检查编译", "檢查編譯", "检查构建", "檢查構建",
                "检查项目", "檢查專案", "修复项目", "修復專案",
                "fix build", "fix compile", "fix project",
                "diagnose and fix", "diagnose fix", "diagnose project",
                "check build", "check compile" });
            root = CleanPath(root);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                root = FindRepoRoot(Directory.GetCurrentDirectory());

            if (!EnsurePermission(
                    Tr("Run commands & modify files", "执行命令并修改文件", "執行指令並修改檔案"),
                    permFileWrite, true, "Diagnose & Fix"))
                return;

            AppendChat("You", "[Mode: " + ModeDisplayName(workMode) + "]\r\n" + text, Color.FromArgb(30, 90, 180));
            SetCurrentTaskStatus("running", "Diagnosing & fixing: " + root, false);

            var parameters = new Dictionary<string, object>();
            parameters["root"] = root;

            var fixGate = PermissionGate.FromJson(permGate.ToJson());
            fixGate.Set("permFileWrite", PermissionLevel.Ask);
            var pipeline = agentPipelineFactory.Create(fixGate, pluginDir, allowAdvancedPlugins);
            pipeline.RequestApproval = approvalCommand => ShowApprovalCard(
                "DiagnoseFix",
                Tr("Confirm diagnose & fix", "确认诊断修复", "確認診斷修復"),
                Tr("Run", "运行", "執行"),
                Tr("Run commands & modify files", "执行命令并修改文件", "執行指令並修改檔案"),
                root,
                Tr("This runs build/test commands and may modify source files to fix errors.",
                   "这将运行构建/测试命令，可能修改源文件以修复错误。",
                   "這將執行建構/測試指令，可能修改原始檔案以修復錯誤。"),
                "Diagnose & Fix: " + root,
                "Root: " + root,
                root);

            var fixCommand = new AgentCommand("DiagnoseFix", "permFileWrite", currentTaskId, root,
                "Diagnose & Fix: " + root, parameters);

            CommandResult result = await Task.Run(() => pipeline.Run(fixCommand));

            if (IsDisposed || Disposing) return;

            if (result.Status == CommandStatus.Success)
            {
                LogAction("DiagnoseFix", "Diagnosed & fixed: " + root);
                RecordAction("DiagnoseFix", "success", "Diagnose & Fix: " + root, "");
                SetCurrentTaskStatus("ready_for_review", "Diagnose & fix complete", true);

                try
                {
                    using (var dlg = new ZhuaQianDesktopApp.UI.DiagnoseFixDialog(result.OutputText ?? "", root))
                    {
                        dlg.ExportPatchCallback = patchPath => ExportDiagnoseFixPatch(root, patchPath);
                        dlg.CommitCallback = msg => CommitDiagnoseFixChanges(root, msg);
                        dlg.ApplyPatchCallback = patchText => ApplyDiagnoseFixPatchAndRerun(root, patchText);
                        dlg.ShowDialog(this);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("DiagnoseFixDialog: " + ex.Message);
                }
            }
            else if (result.Status == CommandStatus.Cancelled)
            {
                RecordAction("DiagnoseFix", "cancelled", "Diagnose & Fix cancelled", "");
            }
            else if (result.Status == CommandStatus.Denied)
            {
                SetCurrentTaskStatus("needs_input", "Diagnose & fix denied", true);
                RecordAction("DiagnoseFix", "denied", result.ErrorMessage, "");
                MessageBox.Show(result.ErrorMessage, "Diagnose & Fix denied");
            }
            else
            {
                SetCurrentTaskStatus("failed", "Diagnose & fix failed", true);
                RecordAction("DiagnoseFix", "failed", result.ErrorMessage, "");
                AppendChat("Error", result.ErrorMessage ?? "Diagnose & fix failed.", Color.FromArgb(190, 40, 40));
            }
        }

        void ExportDiagnoseFixPatch(string root, string patchPath)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters["action"] = "export-patch";
                parameters["name"] = Path.GetFileNameWithoutExtension(patchPath);
                var cmd = new AgentCommand("GitWorkflow", "permCommandRun", currentTaskId, root,
                    "Export patch", parameters);
                var pipeline = agentPipelineFactory.Create(permGate, pluginDir, allowAdvancedPlugins);
                var result = pipeline.Run(cmd);
                if (result.Status == CommandStatus.Success && !string.IsNullOrEmpty(result.ResultPath) && File.Exists(result.ResultPath))
                    File.Copy(result.ResultPath, patchPath, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ExportDiagnoseFixPatch: " + ex.Message);
            }
        }

        void CommitDiagnoseFixChanges(string root, string message)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters["action"] = "commit";
                parameters["message"] = message;
                var cmd = new AgentCommand("GitWorkflow", "permCommandRun", currentTaskId, root,
                    "Commit: " + message, parameters);
                var pipeline = agentPipelineFactory.Create(permGate, pluginDir, allowAdvancedPlugins);
                var result = pipeline.Run(cmd);
                if (result.Status != CommandStatus.Success)
                    MessageBox.Show(result.ErrorMessage ?? "Commit failed.", "Git commit");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("CommitDiagnoseFixChanges: " + ex.Message);
            }
        }

        string ApplyDiagnoseFixPatchAndRerun(string root, string patchText)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters["root"] = root;
                parameters["patch"] = patchText;
                parameters["patchFile"] = "manual.patch";
                var cmd = new AgentCommand("DiagnoseFix", "permFileWrite", currentTaskId, root,
                    "Apply patch & rerun", parameters);
                var pipeline = agentPipelineFactory.Create(permGate, pluginDir, allowAdvancedPlugins);
                var result = pipeline.Run(cmd);
                return result.Status == CommandStatus.Success
                    ? (result.OutputText ?? "")
                    : (result.ErrorMessage ?? "rerun failed");
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }
    }
}
