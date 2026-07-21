using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp
{
    partial class MainForm
    {
        void OrganizeFolder()
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = Tr("Choose a messy folder to organize", "选择要整理的杂乱文件夹", "選擇要整理的雜亂資料夾");
                if (fbd.ShowDialog(this) != DialogResult.OK) return;
                ExecuteOrganizeFolder(fbd.SelectedPath);
            }
        }

        void ExecuteOrganizeFolder(string folderPath)
        {
            folderPath = CleanPath(folderPath);
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                AppendChat("Error", Tr("Folder not found: ", "找不到文件夹：", "找不到資料夾：") + folderPath, ThemeManager.Error);
                return;
            }
            if (!EnsurePermission(Tr("Move/delete files", "移动/删除文件", "移動/刪除檔案"), permFileMoveDelete, true, "Organize Folder")) return;

            var files = new List<string>();
            try { files.AddRange(Directory.GetFiles(folderPath)); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Tr("Organize failed", "整理失败", "整理失敗")); return; }
            if (files.Count == 0)
            {
                AppendChat("ZhuaQian", Tr("No files found in this folder.", "这个文件夹里没有文件。", "這個資料夾裡沒有檔案。"), Color.FromArgb(200, 120, 0));
                return;
            }

            var preview = new StringBuilder();
            preview.AppendLine(Tr("Move ", "将 ", "將 ") + files.Count + Tr(" files into _ZhuaQian_Organized?", " 个文件移动到 _ZhuaQian_Organized？", " 個檔案移動到 _ZhuaQian_Organized？"));
            preview.AppendLine();
            for (int i = 0; i < Math.Min(12, files.Count); i++)
                preview.AppendLine(Path.GetFileName(files[i]) + " -> " + Tools.FolderOrganizer.CategoryFor(files[i]));
            if (files.Count > 12) preview.AppendLine("...");
            preview.AppendLine();
            preview.AppendLine(Tr("This will move local files by type category. Continue?", "这会按类型移动本地文件。是否继续？", "這會按類型移動本機檔案。是否繼續？"));

            var organizeGate = PermissionGate.FromJson(permGate.ToJson());
            organizeGate.Set("permFileMoveDelete", PermissionLevel.Ask);
            var pipeline = agentPipelineFactory.Create(organizeGate, pluginDir, allowAdvancedPlugins, null, MainForm.ShowPluginCapabilityPrompt);
            pipeline.RequestApproval = command => ShowApprovalCard(
                "OrganizeFolder",
                "Organize folder",
                "Execute",
                "Move/delete files",
                folderPath,
                "This action moves local files into _ZhuaQian_Organized subfolders by category. A rollback manifest will be created.",
                "Moved files plus rollback manifest.",
                preview.ToString(),
                folderPath);

            var args = new Dictionary<string, object>();
            args["rootDir"] = folderPath;
            args["taskTitle"] = currentTaskTitle;
            var result = pipeline.Run(new AgentCommand("OrganizeFolder", "permFileMoveDelete", currentTaskId, folderPath, "Organize folder " + folderPath, args));
            if (result.Status == CommandStatus.Cancelled)
            {
                SetCurrentTaskStatus("needs_input", "Folder organize cancelled", true);
                RecordAction("OrganizeFolder", "cancelled", "Folder organize cancelled", folderPath);
                return;
            }
            if (result.Status != CommandStatus.Success)
            {
                SetCurrentTaskStatus("failed", "Organize failed", true);
                RecordAction("OrganizeFolder", "failed", result.ErrorMessage ?? "Organize failed", folderPath);
                AppendChat("Error", result.ErrorMessage ?? Tr("Organize failed.", "整理失败。", "整理失敗。"), ThemeManager.Error);
                return;
            }

            string manifestPath = result.RollbackManifestPath;
            undoRedo.Record(Tools.UndoableActionType.OrganizeRollback, "Organize folder", manifestPath);
            RecordExportHistory("rollback", manifestPath, files.Count);
            LogAction("OrganizeFolder", "Organized " + folderPath + " through AgentPipeline");
            SetCurrentTaskStatus("ready_for_review", "Organized " + folderPath, true);
            RecordAction("OrganizeFolder", "success", "Files organized under " + folderPath, manifestPath);
            AppendChat("ZhuaQian", "Files organized into _ZhuaQian_Organized.\r\nRollback manifest:\r\n" + manifestPath, ThemeManager.Success);
        }

        string FileTypeBucket(string ext)
        {
            ext = (ext ?? "").ToLowerInvariant();
            if (imageExts.Contains(ext)) return "Images";
            if (ext == ".pdf") return "PDF";
            if (ext == ".docx" || ext == ".doc") return "Word";
            if (ext == ".xlsx" || ext == ".xlsm" || ext == ".xls" || ext == ".csv") return "Excel";
            if (ext == ".pptx" || ext == ".ppt") return "PowerPoint";
            if (textExts.Contains(ext)) return "Text-Code";
            return "Other";
        }

        string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; i < 10000; i++)
            {
                string candidate = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(candidate)) return candidate;
            }
            return Path.Combine(dir, name + " (" + Guid.NewGuid().ToString("N").Substring(0, 8) + ")" + ext);
        }
    }
}
