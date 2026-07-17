using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Providers;

namespace ZhuaQianDesktopApp
{
    // Save / export / output-recording feature of MainForm, extracted from the
    // oversized ZhuaQianDesktop.cs to keep the main file within its line budget.
    // This is a pure move (no logic change); all members belong to the same
    // partial class MainForm and access shared fields directly.
    public partial class MainForm
    {
        void SaveLastReplyToTxt(bool automatic)
        {
            string text = GetLastModelReply();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this,
                    Tr("No AI reply to save yet.", "还没有可保存的 AI 回复。", "還沒有可儲存的 AI 回覆。"),
                    "Save TXT");
                return;
            }
            SaveTextToTxt(text, automatic);
        }

        bool SaveTextToTxt(string text, bool automatic)
        {
            if (!EnsurePermission(Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"), permFileWrite, false, "Save TXT")) return false;
            if (automatic)
            {
                string path = BuildAutoExportPath("txt");
                var result = RunExportFilePipeline("txt", path, text);
                if (result.Status != CommandStatus.Success)
                {
                    RecordAction("ExportFile", "failed", result.ErrorMessage ?? "Export failed", path);
                    return false;
                }
                SetCurrentTaskStatus("ready_for_review", "Generated TXT", true);
                AppendChat("ZhuaQian",
                    Tr("TXT file generated:\r\n", "TXT 文件已生成：\r\n", "TXT 檔案已產生：\r\n") + path,
                    Color.FromArgb(0, 130, 80));
                return true;
            }
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = Tr("Save reply as TXT", "保存回复为 TXT", "儲存回覆為 TXT");
                sfd.Filter = "Text file|*.txt|All files|*.*";
                string safeTitle = BuildExportFileBaseName();
                sfd.FileName = safeTitle + ".txt";
                if (sfd.ShowDialog(this) != DialogResult.OK) return false;
                File.WriteAllText(sfd.FileName, text, new UTF8Encoding(false));
                LogAction("SaveTxt", "Saved reply to " + sfd.FileName);
                SetCurrentTaskStatus("ready_for_review", "Saved TXT", true);
                RecordAction("SaveTxt", "success", "Saved reply text", sfd.FileName);
                AppendChat("ZhuaQian",
                    Tr("TXT file saved:\r\n", "TXT 文件已保存：\r\n", "TXT 檔案已儲存：\r\n") + sfd.FileName,
                    Color.FromArgb(0, 130, 80));
                return true;
            }
        }

        bool WantsTxtFile(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string value = text.ToLowerInvariant();
            return value.Contains(".txt")
                || value.Contains("txt文件")
                || value.Contains("txt 文件")
                || value.Contains("txt檔")
                || value.Contains("txt 檔")
                || value.Contains("保存为txt")
                || value.Contains("保存成txt")
                || value.Contains("生成txt")
                || value.Contains("生成 txt")
                || (value.Contains("txt") && (value.Contains("\u751f\u6210") || value.Contains("\u521b\u5efa") || value.Contains("\u5275\u5efa") || value.Contains("\u4fdd\u5b58") || value.Contains("\u5bfc\u51fa") || value.Contains("\u684c\u9762")))
                || value.Contains("导出txt")
                || value.Contains("匯出txt")
                || value.Contains("save as txt")
                || value.Contains("export txt")
                || value.Contains("create a txt")
                || value.Contains("generate a txt");
        }

        void SaveLastReplyAsFile(bool automatic, string requestedFormat)
        {
            string text = GetLastModelReply();
            if (string.IsNullOrWhiteSpace(text))
            {
                text = chat != null ? chat.SelectedText : "";
                if (string.IsNullOrWhiteSpace(text)) text = input != null ? input.Text.Trim() : "";
                if (string.IsNullOrWhiteSpace(text))
                {
                    MessageBox.Show(this,
                        Tr("No AI reply, selected chat text, or input text to save yet.",
                           "还没有可保存的 AI 回复、选中文本或输入框内容。",
                           "還沒有可儲存的 AI 回覆、選取文字或輸入框內容。"),
                        Tr("Save File", "保存文件", "儲存檔案"));
                    return;
                }
            }

            string format = string.IsNullOrWhiteSpace(requestedFormat) ? PromptExportFormat() : NormalizeExportFormat(requestedFormat);
            if (string.IsNullOrWhiteSpace(format)) return;
            SaveTextAsFormat(text, format, automatic);
        }

        bool SaveTextAsFormat(string text, string format, bool automatic)
        {
            return SaveTextAsFormat(text, format, automatic, "");
        }

        bool SaveTextAsFormat(string text, string format, bool automatic, string requestedPath)
        {
            if (!EnsurePermission(Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"), permFileWrite, false, "Save File")) return false;
            format = NormalizeExportFormat(format);
            if (string.IsNullOrWhiteSpace(format)) format = "txt";
            string fileText = PrepareGeneratedFileContent(text, format);
            string upper = format.ToUpperInvariant();
            if (automatic)
            {
                string path = string.IsNullOrWhiteSpace(requestedPath) ? BuildAutoExportPath(format) : requestedPath;
                var result = RunExportFilePipeline(format, path, fileText);
                if (result.Status != CommandStatus.Success)
                {
                    RecordAction("ExportFile", "failed", result.ErrorMessage ?? "Export failed", path);
                    return false;
                }
                SetCurrentTaskStatus("ready_for_review", "Generated " + upper, true);
                RememberGeneratedFilePath(path);
                AppendChat("ZhuaQian", upper + Tr(" file generated:\r\n", " 文件已生成：\r\n", " 檔案已產生：\r\n") + path + "\r\n" + Tr("Folder: ", "文件夹：", "資料夾：") + Path.GetDirectoryName(path), Color.FromArgb(0, 130, 80));
                return true;
            }
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = Tr("Save reply as ", "保存回复为 ", "儲存回覆為 ") + upper;
                if (format == "docx") sfd.Filter = "Word document|*.docx|All files|*.*";
                else if (format == "pptx") sfd.Filter = "PowerPoint presentation|*.pptx|All files|*.*";
                else if (format == "xlsx") sfd.Filter = "Excel workbook|*.xlsx|All files|*.*";
                else if (format == "pdf") sfd.Filter = "PDF document|*.pdf|All files|*.*";
                else if (format == "png") sfd.Filter = "PNG image|*.png|All files|*.*";
                else if (format == "md") sfd.Filter = "Markdown file|*.md|All files|*.*";
                else sfd.Filter = "Text file|*.txt|All files|*.*";

                string safeTitle = BuildExportFileBaseName();
                sfd.FileName = safeTitle + "." + format;
                if (sfd.ShowDialog(this) != DialogResult.OK) return false;

                if (format == "docx") officeExporter.SaveDocx(sfd.FileName, fileText);
                else if (format == "pptx") officeExporter.SavePptx(sfd.FileName, fileText);
                else if (format == "xlsx") officeExporter.SaveXlsx(sfd.FileName, fileText);
                else if (format == "pdf") officeExporter.SavePdf(sfd.FileName, fileText);
                else if (format == "png") officeExporter.SavePng(sfd.FileName, fileText);
                else if (format == "md") officeExporter.SaveMd(sfd.FileName, fileText);
                else officeExporter.SaveTxt(sfd.FileName, fileText);

                LogAction("SaveFile", "Saved " + format + " reply to " + sfd.FileName);
                RecordExportHistory(format, sfd.FileName, fileText.Length);
                SetCurrentTaskStatus("ready_for_review", "Saved " + upper, true);
                RecordAction("SaveFile", "success", "Saved " + format + " reply", sfd.FileName);
                RememberGeneratedFilePath(sfd.FileName);
                AppendChat("ZhuaQian", upper + Tr(" file saved:\r\n", " 文件已保存：\r\n", " 檔案已儲存：\r\n") + sfd.FileName + "\r\n" + Tr("Folder: ", "文件夹：", "資料夾：") + Path.GetDirectoryName(sfd.FileName), Color.FromArgb(0, 130, 80));
                return true;
            }
        }

        void RememberGeneratedFilePath(string path)
        {
            lastGeneratedFilePath = path ?? "";
            if (openFolderButton == null) return;
            string dir = string.IsNullOrWhiteSpace(lastGeneratedFilePath) ? "" : Path.GetDirectoryName(lastGeneratedFilePath);
            openFolderButton.Enabled = !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir);
        }

        void OpenLastGeneratedFolder()
        {
            if (string.IsNullOrWhiteSpace(lastGeneratedFilePath))
            {
                MessageBox.Show(this, Tr("No generated file yet.", "还没有生成文件。", "還沒有產生檔案。"), Tr("Open Folder", "打开文件夹", "開啟資料夾"));
                return;
            }
            try
            {
                if (File.Exists(lastGeneratedFilePath))
                    Process.Start("explorer.exe", "/select," + QuoteArg(lastGeneratedFilePath));
                else
                {
                    string dir = Path.GetDirectoryName(lastGeneratedFilePath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                        Process.Start("explorer.exe", QuoteArg(dir));
                    else
                        MessageBox.Show(this, Tr("Folder not found:\r\n", "找不到文件夹：\r\n", "找不到資料夾：\r\n") + lastGeneratedFilePath, Tr("Open Folder", "打开文件夹", "開啟資料夾"));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Tr("Open Folder failed", "打开文件夹失败", "開啟資料夾失敗"));
            }
        }

        CommandResult RunExportFilePipeline(string format, string path, string text)
        {
            var exportGate = PermissionGate.FromJson(permGate.ToJson());
            exportGate.Set("permFileWrite", permFileWrite ? PermissionLevel.Allow : PermissionLevel.Deny);
            var pipeline = agentPipelineFactory.Create(exportGate, pluginDir, allowAdvancedPlugins);
            var args = new Dictionary<string, object>();
            args["format"] = NormalizeExportFormat(format);
            args["text"] = text ?? "";
            args["taskTitle"] = currentTaskTitle;
            var command = new AgentCommand("ExportFile", "permFileWrite", currentTaskId, path, "Generate " + (format ?? "txt") + " file", args);
            return pipeline.Run(command);
        }

        string BuildAutoExportPath(string format)
        {
            format = NormalizeExportFormat(format);
            if (string.IsNullOrWhiteSpace(format)) format = "txt";
            string dir = Path.Combine(configDir, "generated");
            Directory.CreateDirectory(dir);
            string safeTitle = BuildExportFileBaseName();
            string name = safeTitle + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "." + format;
            return UniquePath(Path.Combine(dir, name));
        }

        string BuildDesktopExportPath(string format)
        {
            format = NormalizeExportFormat(format);
            if (string.IsNullOrWhiteSpace(format)) format = "txt";
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(dir))
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
            Directory.CreateDirectory(dir);
            string safeTitle = BuildExportFileBaseName();
            string name = safeTitle + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "." + format;
            return UniquePath(Path.Combine(dir, name));
        }

        string BuildExportFileBaseName()
        {
            string value = !string.IsNullOrWhiteSpace(lastExportNameHint) ? lastExportNameHint : currentTaskTitle;
            value = SanitizeFileTitle(value);
            if (value.Length == 0 || string.Equals(value, "New task", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "ZhuaQian-reply", StringComparison.OrdinalIgnoreCase))
                value = "ZhuaQian-output";
            if (value.Length > 48) value = value.Substring(0, 48).Trim();
            return value.Length == 0 ? "ZhuaQian-output" : value;
        }

        string BuildExportNameHint(string prompt, string format)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return "";
            string value = prompt.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')[0].Trim();
            value = Regex.Replace(value, @"https?://\S+", "", RegexOptions.IgnoreCase).Trim();

            string[] markers = {
                "主题是", "主题：", "主题:", "標題是", "标题是", "标题：", "标题:",
                "功能是", "功能：", "功能:", "内容是", "内容：", "内容:",
                "做一个", "做一個", "写一个", "寫一個", "创建一个", "創建一個",
                "生成一个", "生成一個", "生成", "创建", "建立",
                "make", "build", "create", "generate"
            };
            int best = -1;
            string bestMarker = "";
            foreach (string marker in markers)
            {
                int idx = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && idx >= best)
                {
                    best = idx;
                    bestMarker = marker;
                }
            }
            if (best >= 0) value = value.Substring(best + bestMarker.Length).Trim();

            string fmt = NormalizeExportFormat(format);
            string[] noise = {
                "." + fmt, fmt, "到桌面", "保存到桌面", "生成到桌面", "桌面上", "桌面",
                "文件", "檔案", "脚本", "腳本", "代码", "程式碼", "源码", "原始碼",
                "网页", "網頁", "小网页", "小網頁", "一个", "一個", "一份", "一张", "一張",
                "帮我", "幫我", "请", "請", "保存", "导出", "匯出", "生成", "创建", "建立",
                "excel", "xlsx", "pptx", "ppt", "powerpoint", "word", "docx", "doc",
                "pdf", "png", "html", "txt", "markdown", "md",
                "file", "script", "code", "source", "desktop", "save", "export", "please"
            };
            foreach (string item in noise)
                if (!string.IsNullOrWhiteSpace(item)) value = value.Replace(item, "");

            value = value.Trim(' ', '\t', '，', ',', '。', '.', '：', ':', '；', ';', '-', '_', '"', '\'', '“', '”', '‘', '’');
            value = SanitizeFileTitle(value);
            return value;
        }

        string SanitizeFileTitle(string value)
        {
            value = Regex.Replace(value ?? "", "[\\\\/:*?\"<>|]+", "_").Trim();
            value = Regex.Replace(value, "\\s+", " ").Trim();
            value = value.Trim('.', ' ', '_', '-');
            if (value.Length > 48) value = value.Substring(0, 48).Trim();
            return value;
        }

        string PromptExportFormat()
        {
            using (var form = new Form())
            using (var combo = new ComboBox())
            using (var ok = new Button())
            using (var cancel = new Button())
            using (var label = new Label())
            {
                form.Text = Tr("Save File", "保存文件", "儲存檔案");
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(300, 118);
                form.BackColor = zqPanelBg;
                label.Text = Tr("Choose output format:", "选择输出格式：", "選擇輸出格式：");
                label.SetBounds(16, 14, 260, 22);
                label.ForeColor = zqMuted;
                combo.DropDownStyle = ComboBoxStyle.DropDownList;
                combo.Items.Add("txt");
                combo.Items.Add("md");
                combo.Items.Add("docx");
                combo.Items.Add("pptx");
                combo.Items.Add("xlsx");
                combo.Items.Add("pdf");
                combo.Items.Add("png");
                combo.Items.Add("html");
                combo.Items.Add("py");
                combo.Items.Add("js");
                combo.Items.Add("cs");
                combo.Items.Add("ps1");
                combo.SelectedIndex = 0;
                combo.SetBounds(16, 42, 260, 24);
                ok.Text = Tr("OK", "确定", "確定");
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(118, 80, 74, 26);
                cancel.Text = Tr("Cancel", "取消", "取消");
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(202, 80, 74, 26);
                combo.BackColor = zqSurface;
                combo.ForeColor = zqInk;
                StyleButton(ok, ZqButtonRole.Primary);
                StyleButton(cancel, ZqButtonRole.Ghost);
                form.Controls.Add(label);
                form.Controls.Add(combo);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                if (form.ShowDialog(this) != DialogResult.OK) return "";
                return Convert.ToString(combo.SelectedItem);
            }
        }

        string DetectExportFormat(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string value = text.ToLowerInvariant();
            bool wantsFileOutput = value.Contains("generate") || value.Contains("create") || value.Contains("make ")
                || value.Contains("save") || value.Contains("export") || value.Contains("write") || value.Contains("output")
                || value.Contains("file")
                || value.Contains("\u751f\u6210") || value.Contains("\u521b\u5efa") || value.Contains("\u5275\u5efa")
                || value.Contains("\u5236\u4f5c") || value.Contains("\u4fdd\u5b58") || value.Contains("\u5bfc\u51fa")
                || value.Contains("\u532f\u51fa") || value.Contains("\u843d\u76d8")
                || value.Contains("\u6587\u4ef6") || value.Contains("\u6a94\u6848");
            bool explicitExtension = value.Contains(".pptx") || value.Contains(".ppt")
                || value.Contains(".xlsx") || value.Contains(".xls")
                || value.Contains(".docx") || value.Contains(".doc")
                || value.Contains(".pdf")
                || value.Contains(".png")
                || value.Contains(".html") || value.Contains(".css") || value.Contains(".js") || value.Contains(".ts")
                || value.Contains(".py") || value.Contains(".cs") || value.Contains(".ps1") || value.Contains(".bat") || value.Contains(".cmd")
                || value.Contains(".json") || value.Contains(".sql") || value.Contains(".yaml") || value.Contains(".yml")
                || value.Contains(".md")
                || value.Contains(".txt");
            if (!wantsFileOutput && !explicitExtension) return "";
            if (value.Contains(".html") || value.Contains("html") || value.Contains("\u7f51\u9875") || value.Contains("\u7db2\u9801")) return "html";
            if (value.Contains(".css") || value.Contains("css")) return "css";
            if (value.Contains(".ts") || value.Contains("typescript")) return "ts";
            if (value.Contains(".js") || value.Contains("javascript") || value.Contains("node.js")) return "js";
            if (value.Contains(".py") || value.Contains("python")) return "py";
            if (value.Contains(".cs") || value.Contains("c#") || value.Contains("csharp")) return "cs";
            if (value.Contains(".ps1") || value.Contains("powershell")) return "ps1";
            if (value.Contains(".bat") || value.Contains(".cmd") || value.Contains("\u6279\u5904\u7406") || value.Contains("\u6279\u8655\u7406")) return "bat";
            if (value.Contains(".json") || value.Contains("json")) return "json";
            if (value.Contains(".sql") || value.Contains("sql")) return "sql";
            if (value.Contains(".yaml") || value.Contains(".yml") || value.Contains("yaml")) return "yaml";
            if (value.Contains(".pdf") || value.Contains("pdf")) return "pdf";
            if (value.Contains(".png") || value.Contains("png")
                || value.Contains("\u56fe\u7247") || value.Contains("\u5716\u7247")
                || value.Contains("\u6d77\u62a5") || value.Contains("\u6d77\u5831")) return "png";
            if (value.Contains(".pptx") || value.Contains(".ppt") || value.Contains("ppt") || value.Contains("powerpoint")
                || value.Contains("\u5e7b\u706f\u7247") || value.Contains("\u5e7b\u71c8\u7247") || value.Contains("\u6f14\u793a\u6587\u7a3f")) return "pptx";
            if (value.Contains(".xlsx") || value.Contains(".xls") || value.Contains("excel")
                || value.Contains("\u8868\u683c") || value.Contains("\u5de5\u4f5c\u7c3f")) return "xlsx";
            if (value.Contains(".docx") || value.Contains(".doc") || value.Contains("word")
                || value.Contains("\u6587\u6863") || value.Contains("\u6587\u6a94")) return "docx";
            if (value.Contains(".md") || value.Contains("markdown")) return "md";
            if (WantsTxtFile(text)) return "txt";
            if (wantsFileOutput) return "txt";
            return "";
        }

        string DetectExportTargetPath(string text, string format)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(format)) return "";
            string value = text.ToLowerInvariant();
            bool wantsDesktop = value.Contains("desktop")
                || value.Contains("\u684c\u9762")
                || value.Contains("\u684c\u9762\u4e0a")
                || value.Contains("\u5230\u684c\u9762")
                || value.Contains("\u4fdd\u5b58\u5230\u684c\u9762")
                || value.Contains("\u751f\u6210\u5230\u684c\u9762");
            if (wantsDesktop) return BuildDesktopExportPath(format);
            return "";
        }

        string BuildFileGenerationInstruction(string format)
        {
            format = NormalizeExportFormat(format);
            if (string.IsNullOrWhiteSpace(format)) return "";
            string upper = format.ToUpperInvariant();
            return "The user asked for a real " + upper + " file. The desktop app will create and save the file locally after your reply. "
                + "Do not say that you cannot create files. Produce only the content that should go into the file. "
                + "For DOCX/MD/TXT use clean headings and paragraphs. For PPTX use slide titles and bullet points. "
                + "For XLSX use simple rows with columns separated by |, one row per line. "
                + "For PDF/PNG use a concise document layout with headings and short paragraphs. "
                + "For source code or scripts, output raw file content only, with no markdown code fences and no explanation.";
        }

        string NormalizeExportFormat(string format)
        {
            if (string.IsNullOrWhiteSpace(format)) return "";
            string value = format.Trim().TrimStart('.').ToLowerInvariant();
            if (value == "ppt") return "pptx";
            if (value == "doc") return "docx";
            if (value == "xls" || value == "xlsm") return "xlsx";
            if (value == "text") return "txt";
            if (value == "markdown") return "md";
            if (value == "jpeg" || value == "jpg") return "png";
            if (value == "javascript") return "js";
            if (value == "typescript") return "ts";
            if (value == "python") return "py";
            if (value == "powershell") return "ps1";
            if (value == "csharp" || value == "c#") return "cs";
            if (value == "yml") return "yaml";
            if (value == "txt" || value == "md" || value == "docx" || value == "pptx" || value == "xlsx" || value == "pdf" || value == "png"
                || value == "html" || value == "css" || value == "js" || value == "ts" || value == "py" || value == "cs" || value == "ps1"
                || value == "bat" || value == "cmd" || value == "json" || value == "xml" || value == "yaml" || value == "sql") return value;
            return "";
        }

        bool IsCodeExportFormat(string format)
        {
            format = NormalizeExportFormat(format);
            return format == "html" || format == "css" || format == "js" || format == "ts" || format == "py" || format == "cs"
                || format == "ps1" || format == "bat" || format == "cmd" || format == "json" || format == "xml" || format == "yaml" || format == "sql";
        }

        string PrepareGeneratedFileContent(string text, string format)
        {
            if (!IsCodeExportFormat(format)) return text ?? "";
            string value = (text ?? "").Trim();
            var match = Regex.Match(value, "^```[a-zA-Z0-9_#+.-]*\\s*\\r?\\n([\\s\\S]*?)\\r?\\n```\\s*$");
            if (match.Success) return match.Groups[1].Value.TrimEnd();
            return text ?? "";
        }

        void RecordExportHistory(string format, string path, int chars)
        {
            outputsHub.RecordExportHistory(format, path, chars, currentTaskId, currentTaskTitle);
        }

        void RecordOutput(string sourceAction, string type, string path, string taskId, string taskTitle, string sourceActionId, int sizeBytes)
        {
            outputsHub.RecordOutput(sourceAction, type, path, taskId, taskTitle, sourceActionId, sizeBytes);
        }


        string XmlEscape(string value)
        {
            return System.Security.SecurityElement.Escape(value ?? "") ?? "";
        }
    }
}
