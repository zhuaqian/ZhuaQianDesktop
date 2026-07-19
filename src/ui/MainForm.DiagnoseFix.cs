using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp
{
    public partial class MainForm
    {
        bool LooksLikeDiagnoseFixRequest(string lower)
        {
            return ContainsAny(lower,
                "诊断修复", "诊断并修复", "检查并修复", "分析并修复",
                "修复编译", "修复构建", "修复代码", "修复项目",
                "检查编译", "检查构建", "检查项目", "项目报错",
                "编译失败", "构建失败", "测试失败", "代码报错",
                "诊断修復", "診斷修復", "檢查並修復", "修復編譯", "修復建置", "編譯失敗", "建置失敗",
                "fix build", "fix compile", "fix project", "fix code",
                "diagnose and fix", "diagnose fix", "diagnose project",
                "check build", "check compile", "check project",
                "why build fail", "build failed", "build error", "compile error",
                "test failed", "why won't it compile", "why won t it compile");
        }

        void ChooseDiagnoseFixProject()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = Tr("Choose the project folder to diagnose and fix.",
                                     "选择要诊断修复的项目文件夹。",
                                     "選擇要診斷修復的專案資料夾。");
                dlg.SelectedPath = FindRepoRoot(Directory.GetCurrentDirectory());
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                ExecuteDiagnoseFixForRoot(dlg.SelectedPath, Tr("Diagnose and fix this project", "诊断并修复这个项目", "診斷並修復這個專案"));
            }
        }

        void ExecuteDiagnoseFix(string text)
        {
            string root = ExtractNaturalTarget(text, new string[] {
                "诊断修复", "诊断并修复", "检查并修复", "分析并修复",
                "修复编译", "修复构建", "修复代码", "修复项目",
                "检查编译", "检查构建", "检查项目",
                "診斷修復", "檢查並修復", "修復編譯", "修復建置",
                "fix build", "fix compile", "fix project", "fix code",
                "diagnose and fix", "diagnose fix", "diagnose project",
                "check build", "check compile", "check project" });
            root = CleanPath(root);

            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = Tr("Choose the project folder to diagnose and fix.",
                                         "选择要诊断修复的项目文件夹。",
                                         "選擇要診斷修復的專案資料夾。");
                    dlg.SelectedPath = FindRepoRoot(Directory.GetCurrentDirectory());
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    root = dlg.SelectedPath;
                }
            }

            ExecuteDiagnoseFixForRoot(root, text);
        }

        void ExecuteDiagnoseFixForRoot(string root, string goal)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                AppendChat("Error", Tr("Project folder not found.", "找不到项目文件夹。", "找不到專案資料夾。"), Color.FromArgb(190, 40, 40));
                return;
            }

            if (!EnsurePermission(
                    Tr("Run build/test and modify code files", "运行构建/测试并修改代码文件", "執行建置/測試並修改程式碼檔案"),
                    permFileWrite, true, "Diagnose & Fix"))
                return;

            AppendChat("You", "[Mode: " + ModeDisplayName(workMode) + "]\r\n" + goal, Color.FromArgb(30, 90, 180));
            SetCurrentTaskStatus("running", "Diagnosing and fixing: " + root, false);

            var parameters = new Dictionary<string, object>();
            parameters["root"] = root;
            parameters["maxIterations"] = "3";

            var fixGate = PermissionGate.FromJson(permGate.ToJson());
            fixGate.Set("permFileWrite", PermissionLevel.Ask);
            var pipeline = agentPipelineFactory.Create(fixGate, pluginDir, allowAdvancedPlugins, root);
            pipeline.RequestApproval = approvalCommand => ShowApprovalCard(
                "DiagnoseFix",
                Tr("Confirm diagnose and fix", "确认诊断修复", "確認診斷修復"),
                Tr("Run", "运行", "執行"),
                Tr("Build/test plus code edits", "构建/测试与代码修改", "建置/測試與程式碼修改"),
                root,
                Tr("This runs project build/test commands and may modify source files to fix safe, recognized errors.",
                   "这会运行项目构建/测试命令，并可能修改源代码文件来修复已识别的安全错误。",
                   "這會執行專案建置/測試命令，並可能修改原始碼檔案來修復已識別的安全錯誤。"),
                Tr("A coding-loop report with build/test output, patches, diff, and review notes.",
                   "生成包含构建/测试输出、补丁、diff 和复核说明的代码闭环报告。",
                   "產生包含建置/測試輸出、補丁、diff 和複核說明的程式碼閉環報告。"),
                "Root: " + root,
                root);

            var fixCommand = new AgentCommand("DiagnoseFix", "permFileWrite", currentTaskId, root,
                "Diagnose & Fix: " + root, parameters);

            CommandResult result = pipeline.Run(fixCommand);
            if (IsDisposed || Disposing) return;

            if (result.Status == CommandStatus.Success)
            {
                LogAction("DiagnoseFix", "Diagnosed and fixed: " + root);
                RecordAction("DiagnoseFix", "success", "Diagnose & Fix: " + root, "");
                SetCurrentTaskStatus("ready_for_review", "Diagnose and fix complete", true);
                ShowDiagnoseFixDialog(root, result.OutputText ?? "");
            }
            else if (result.Status == CommandStatus.Cancelled)
            {
                RecordAction("DiagnoseFix", "cancelled", "Diagnose & Fix cancelled", "");
            }
            else if (result.Status == CommandStatus.Denied)
            {
                SetCurrentTaskStatus("needs_input", "Diagnose and fix denied", true);
                RecordAction("DiagnoseFix", "denied", result.ErrorMessage, "");
                MessageBox.Show(this, result.ErrorMessage, Tr("Diagnose and Fix denied", "诊断修复被拒绝", "診斷修復被拒絕"));
            }
            else
            {
                SetCurrentTaskStatus("failed", "Diagnose and fix failed", true);
                RecordAction("DiagnoseFix", "failed", result.ErrorMessage, "");
                AppendChat("Error", result.ErrorMessage ?? Tr("Diagnose and fix failed.", "诊断修复失败。", "診斷修復失敗。"), Color.FromArgb(190, 40, 40));
            }
        }

        void ShowDiagnoseFixDialog(string root, string markdown)
        {
            try
            {
                using (var dlg = new ZhuaQianDesktopApp.UI.DiagnoseFixDialog(markdown, root, Tr, uiLanguage))
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

        void ExportDiagnoseFixPatch(string root, string patchPath)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                parameters["action"] = "export-patch";
                parameters["name"] = Path.GetFileNameWithoutExtension(patchPath);
                var cmd = new AgentCommand("GitWorkflow", "permCommandRun", currentTaskId, root, "Export patch", parameters);
                var pipeline = agentPipelineFactory.Create(permGate, pluginDir, allowAdvancedPlugins, root);
                var result = pipeline.Run(cmd);
                if (result.Status == CommandStatus.Success && !string.IsNullOrEmpty(result.ResultPath) && File.Exists(result.ResultPath))
                    File.Copy(result.ResultPath, patchPath, true);
                else if (result.Status != CommandStatus.Success)
                    MessageBox.Show(this, result.ErrorMessage ?? Tr("Patch export failed.", "补丁导出失败。", "補丁匯出失敗。"), Tr("Export Patch", "导出补丁", "匯出補丁"));
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
                var cmd = new AgentCommand("GitWorkflow", "permCommandRun", currentTaskId, root, "Commit: " + message, parameters);
                var pipeline = agentPipelineFactory.Create(permGate, pluginDir, allowAdvancedPlugins, root);
                var result = pipeline.Run(cmd);
                if (result.Status != CommandStatus.Success)
                    MessageBox.Show(this, result.ErrorMessage ?? Tr("Commit failed.", "提交失败。", "提交失敗。"), Tr("Git Commit", "Git 提交", "Git 提交"));
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
                parameters["maxIterations"] = "3";
                var cmd = new AgentCommand("DiagnoseFix", "permFileWrite", currentTaskId, root, "Apply patch and rerun", parameters);
                var pipeline = agentPipelineFactory.Create(permGate, pluginDir, allowAdvancedPlugins, root);
                var result = pipeline.Run(cmd);
                return result.Status == CommandStatus.Success
                    ? (result.OutputText ?? "")
                    : (result.ErrorMessage ?? Tr("Rerun failed.", "重新运行失败。", "重新執行失敗。"));
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }
    }
}
