using System;
using System.Collections.Generic;
using System.IO;
using ZhuaQianDesktopApp.Documents;

namespace ZhuaQianDesktopApp.Agent
{
    /// <summary>
    /// 办公文件生成执行器（Epic F1/F2/F3 收口）。把 OfficeTemplateLibrary 渲染的文本骨架
    /// 通过 OfficeExporter 导出为真实本地文件（pptx/docx/xlsx/pdf/png/md/txt）。
    /// 优先使用调用方（OfficeGenerateDialog）传入、经用户审查/编辑后的最终文本；若未传
    /// 文本，则按 kind + 结构化字段现场渲染，保证无 UI 时也可编程调用。
    /// </summary>
    public sealed class OfficeTemplateExecutor : ICommandExecutor
    {
        readonly OfficeExporter officeExporter;

        public OfficeTemplateExecutor(OfficeExporter officeExporter)
        {
            this.officeExporter = officeExporter ?? new OfficeExporter();
        }

        public string CommandType { get { return "OfficeTemplate"; } }

        public CommandResult Execute(IAgentCommand command)
        {
            string path = command.Target;
            if (string.IsNullOrWhiteSpace(path))
                return CommandResult.Failed("missing target path");

            OfficeTemplateKind kind = ParseKind(GetString(command.Parameters, "kind"));
            string format = GetString(command.Parameters, "format");
            if (string.IsNullOrWhiteSpace(format)) format = OfficeTemplateLibrary.SuggestedExtension(kind);

            string text = GetString(command.Parameters, "text");
            if (string.IsNullOrWhiteSpace(text))
            {
                TemplateContext ctx = BuildFallbackContext(command.Parameters);
                text = OfficeTemplateLibrary.Render(kind, ctx).Text;
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            try { ExportByFormat(path, text, format); }
            catch (Exception ex) { return CommandResult.Failed("office export failed: " + ex.Message); }

            int size = 0;
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length <= int.MaxValue) size = (int)info.Length;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("OfficeTemplateExecutor size: " + ex.Message);
            }

            return CommandResult.Ok(path, false, null, format, size);
        }

        void ExportByFormat(string path, string text, string format)
        {
            if (format == "docx") officeExporter.SaveDocx(path, text);
            else if (format == "pptx") officeExporter.SavePptx(path, text);
            else if (format == "xlsx") officeExporter.SaveXlsx(path, text);
            else if (format == "pdf") officeExporter.SavePdf(path, text);
            else if (format == "png") officeExporter.SavePng(path, text);
            else if (format == "md") officeExporter.SaveMd(path, text);
            else officeExporter.SaveTxt(path, text);
        }

        static TemplateContext BuildFallbackContext(IReadOnlyDictionary<string, object> parameters)
        {
            var ctx = new TemplateContext();
            ctx.Title = GetString(parameters, "title");
            ctx.Subtitle = GetString(parameters, "subtitle");
            ctx.Author = GetString(parameters, "author");
            ctx.Date = GetString(parameters, "date");
            string bullets = GetString(parameters, "bullets");
            if (!string.IsNullOrWhiteSpace(bullets))
            {
                var list = new List<string>();
                foreach (string line in bullets.Replace("\r", "\n").Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line.Trim())) list.Add(line.Trim());
                if (list.Count > 0) ctx.Bullets["要点"] = list;
            }
            return ctx;
        }

        static OfficeTemplateKind ParseKind(string value)
        {
            OfficeTemplateKind k;
            if (Enum.TryParse<OfficeTemplateKind>(value ?? "", true, out k)) return k;
            if (!string.IsNullOrWhiteSpace(value))
            {
                try { return OfficeTemplateLibrary.RenderByName(value, null).Kind; }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("OfficeTemplateExecutor parse kind: " + ex.Message); }
            }
            return OfficeTemplateKind.Report;
        }

        static string GetString(IReadOnlyDictionary<string, object> values, string key)
        {
            object value;
            if (values != null && values.TryGetValue(key, out value) && value != null)
                return Convert.ToString(value);
            return "";
        }
    }
}
