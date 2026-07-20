using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Documents;
using ZhuaQianDesktopApp.Providers;

namespace ZhuaQianDesktopApp
{
    // New user-facing capabilities extracted from the oversized core partial:
    // "save markdown document", "build website", "optimize office file". Each routes
    // through the single command pipeline (WriteFile executor, permFileWrite) so the
    // writes are audited and user-approved. The model call is injected via LlmBridge,
    // which is wired once at the composition root (WireLlmBridge).
    public partial class MainForm
    {
        bool LooksLikeSaveDocumentRequest(string lower)
        {
            return ContainsAny(lower,
                "保存文档", "保存文件", "儲存文件", "落盘", "保存成", "保存为", "存为 markdown", "存成 md",
                "save document", "save as markdown", "save to file", "write to file", "export as md",
                "保存笔记", "写文档", "寫文檔", "保存总结", "存个文档");
        }

        bool LooksLikeWebsiteRequest(string lower)
        {
            return ContainsAny(lower,
                "做网站", "做一个网站", "生成网站", "生成网页", "建站", "做个网页", "做個網站", "生成網站",
                "build a website", "build website", "generate a website", "create a site", "make a website",
                "做网页", "做个网站", "网页生成", "生成站点");
        }

        bool LooksLikeOptimizeOfficeRequest(string lower)
        {
            return ContainsAny(lower,
                "优化ppt", "优化 ppt", "优化pptx", "压缩ppt", "优化word", "优化 docx", "优化excel",
                "优化 xlsx", "优化演示", "优化文档", "压缩文档", "优化表格", "优化excel",
                "optimize ppt", "optimize powerpoint", "optimize word", "optimize excel",
                "optimize docx", "optimize xlsx", "compress ppt", "compress document", "optimize office");
        }

        // Idempotent: point LlmBridge at the configured provider so SiteGenerator and
        // ModelFixStrategy can reach the model. Safe to call from any entry point.
        void WireLlmBridge()
        {
            if (LlmBridge.Chat == null) LlmBridge.Chat = m => providerManager.SendAsync(m);
        }

        async void ExecuteSaveDocument(string raw)
        {
            WireLlmBridge();
            if (!EnsurePermission(
                    Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"),
                    permFileWrite, true, "Save Markdown Document"))
                return;

            string path = ExtractSavePath(raw) ?? Path.Combine(configDir, "docs", SanitizeFileName(raw) + ".md");
            string content;
            try
            {
                content = await AskModelAsync(
                    "You write clear, well-structured Markdown documents. Produce the full document body with headings, lists and fenced code where useful.",
                    "Write a Markdown document about: " + raw);
            }
            catch (Exception ex)
            {
                AppendChat("Error", Tr("Model call failed: ", "模型调用失败：", "模型呼叫失敗：") + ex.Message, Color.FromArgb(190, 40, 40));
                return;
            }
            var result = SaveTextFileViaPipeline(path, content, "Save markdown: " + path);
            ReportFileResult(result, "Markdown document", path);
            input.Clear();
        }

        async void ExecuteBuildSite(string raw)
        {
            WireLlmBridge();
            if (!EnsurePermission(
                    Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"),
                    permFileWrite, true, "Build Website"))
                return;

            string dir = ExtractSavePath(raw);
            if (string.IsNullOrWhiteSpace(dir))
                dir = Path.Combine(configDir, "docs", "site", SanitizeFileName(raw));
            else if (dir.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || dir.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                dir = Path.GetDirectoryName(dir);

            List<SiteGenerator.SiteFile> files;
            try
            {
                files = await SiteGenerator.GenerateAsync(raw, chat => providerManager.SendAsync(chat)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppendChat("Error", Tr("Model call failed: ", "模型调用失败：", "模型呼叫失敗：") + ex.Message, Color.FromArgb(190, 40, 40));
                return;
            }
            int ok = 0;
            foreach (var f in files)
            {
                string fp = Path.Combine(dir, f.Path);
                var r = SaveTextFileViaPipeline(fp, f.Content, "Site file: " + fp);
                if (r != null && r.Status == CommandStatus.Success) ok++;
            }
            AppendChat("ZhuaQian",
                Tr("Generated site with ", "已生成网站，包含 ", "已產生網站，包含 ") + files.Count + " " +
                Tr("files in ", "个文件，位于 ", "個檔案，位於 ") + dir,
                Color.FromArgb(0, 130, 80));
            input.Clear();
        }

        void ExecuteOptimizeOffice(string raw)
        {
            WireLlmBridge();
            if (!EnsurePermission(
                    Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"),
                    permFileWrite, true, "Optimize Office File"))
                return;

            string path = NormalizeOpenTarget(ExtractNaturalTarget(raw,
                new string[] { "优化", "優化", "压缩", "壓縮", "optimize", "compress" }));
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                AppendChat("Error", Tr("Missing office file path. Example: ", "缺少 Office 文件路径。示例：", "缺少 Office 檔案路徑。範例：") + "优化 PPT C:\\work\\deck.pptx", Color.FromArgb(190, 40, 40));
                return;
            }

            OfficeOptimizer.OptimizeResult res;
            string lower = path.ToLowerInvariant();
            try
            {
                if (lower.EndsWith(".pptx")) res = OfficeOptimizer.OptimizePptx(path);
                else if (lower.EndsWith(".docx")) res = OfficeOptimizer.OptimizeDocx(path);
                else if (lower.EndsWith(".xlsx")) res = OfficeOptimizer.OptimizeXlsx(path);
                else
                {
                    AppendChat("Error", Tr("Unsupported type; use .pptx/.docx/.xlsx", "不支持的类型，请使用 .pptx/.docx/.xlsx", "不支援的類型，請使用 .pptx/.docx/.xlsx"), Color.FromArgb(190, 40, 40));
                    return;
                }
            }
            catch (Exception ex)
            {
                AppendChat("Error", Tr("Optimize failed: ", "优化失败：", "優化失敗：") + ex.Message, Color.FromArgb(190, 40, 40));
                return;
            }

            string outPath = Path.Combine(Path.GetDirectoryName(path),
                Path.GetFileNameWithoutExtension(path) + "_optimized" + Path.GetExtension(path));
            var r = SaveBinaryFileViaPipeline(outPath, res.Bytes, "Optimize office: " + outPath);
            if (r != null && r.Status == CommandStatus.Success)
                AppendChat("ZhuaQian", Tr("Optimized ", "已优化 ", "已優化 ") + path + " -> " + outPath + " (" + res.Note + ")", Color.FromArgb(0, 130, 80));
            else
                AppendChat("Error", Tr("Optimize write failed: ", "优化写入失败：", "優化寫入失敗：") + (r == null ? "no result" : r.ErrorMessage), Color.FromArgb(190, 40, 40));
            input.Clear();
        }

        // ---- shared helpers for the new capabilities ----

        async Task<string> AskModelAsync(string system, string user)
        {
            var msgs = LlmBridge.Conversation(system, user);
            return await providerManager.SendAsync(msgs).ConfigureAwait(false);
        }

        CommandResult SaveTextFileViaPipeline(string path, string content, string label)
        {
            return SaveViaPipeline(path, new Dictionary<string, object> { { "content", content } }, label);
        }

        CommandResult SaveBinaryFileViaPipeline(string path, byte[] data, string label)
        {
            return SaveViaPipeline(path, new Dictionary<string, object> { { "base64", Convert.ToBase64String(data) } }, label);
        }

        CommandResult SaveViaPipeline(string path, Dictionary<string, object> parameters, string label)
        {
            var gate = PermissionGate.FromJson(permGate.ToJson());
            gate.Set("permFileWrite", PermissionLevel.Ask);
            var pipeline = agentPipelineFactory.Create(gate, pluginDir, allowAdvancedPlugins);
            pipeline.RequestApproval = approvalCommand => ShowApprovalCard(
                "WriteFile",
                Tr("Confirm saving file", "确认保存文件", "確認儲存檔案"),
                Tr("Save", "保存", "儲存"),
                Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"),
                path,
                Tr("This writes a file to your disk.", "这会把文件写入你的磁盘。", "這會把檔案寫入你的磁碟。"),
                label);
            var cmd = new AgentCommand("WriteFile", "permFileWrite", currentTaskId, path, label, parameters);
            return pipeline.Run(cmd);
        }

        void ReportFileResult(CommandResult result, string kind, string path)
        {
            if (result == null) return;
            if (result.Status == CommandStatus.Success)
            {
                LogAction("WriteFile", "Saved " + kind + " -> " + path);
                RecordAction("WriteFile", "success", "Saved " + kind + " -> " + path, "");
                AppendChat("ZhuaQian", Tr("Saved ", "已保存 ", "已儲存 ") + kind + ": " + path, Color.FromArgb(0, 130, 80));
            }
            else if (result.Status == CommandStatus.Cancelled)
            {
                RecordAction("WriteFile", "cancelled", "Save " + kind, "");
            }
            else if (result.Status == CommandStatus.Denied)
            {
                SetCurrentTaskStatus("needs_input", kind + " save denied", true);
                RecordAction("WriteFile", "denied", result.ErrorMessage, "");
                MessageBox.Show(this, result.ErrorMessage, kind + " save denied");
            }
            else
            {
                SetCurrentTaskStatus("failed", kind + " save failed", true);
                RecordAction("WriteFile", "failed", result.ErrorMessage, "");
                MessageBox.Show(this, result.ErrorMessage, kind + " save failed");
            }
        }

        string ExtractSavePath(string raw)
        {
            string t = NormalizeOpenTarget(ExtractNaturalTarget(raw,
                new string[] { "保存到", "保存至", "存到", "save to", "save in", "输出到" }));
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }

        static string SanitizeFileName(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic)) return "document";
            var sb = new StringBuilder();
            foreach (char c in topic)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_')
                    sb.Append(c == ' ' ? '_' : c);
            }
            string s = sb.ToString().Trim('_');
            if (s.Length > 60) s = s.Substring(0, 60);
            return string.IsNullOrEmpty(s) ? "document" : s;
        }
    }
}
