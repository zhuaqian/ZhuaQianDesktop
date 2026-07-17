using System;
using System.Collections.Generic;
using System.IO;
using ZhuaQianDesktopApp.Documents;

namespace ZhuaQianDesktopApp.Agent
{
    public sealed class ExportFileExecutor : ICommandExecutor
    {
        readonly OfficeExporter officeExporter;

        public ExportFileExecutor(OfficeExporter officeExporter)
        {
            this.officeExporter = officeExporter;
        }

        public string CommandType { get { return "ExportFile"; } }

        public CommandResult Execute(IAgentCommand command)
        {
            string path = command.Target;
            if (string.IsNullOrWhiteSpace(path))
                return CommandResult.Failed("missing target path");

            string format = GetString(command.Parameters, "format");
            string text = GetString(command.Parameters, "text");
            if (string.IsNullOrWhiteSpace(format)) format = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(format)) format = "txt";
            format = format.ToLowerInvariant();

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (format == "docx") officeExporter.SaveDocx(path, text);
            else if (format == "pptx") officeExporter.SavePptx(path, text);
            else if (format == "xlsx") officeExporter.SaveXlsx(path, text);
            else if (format == "pdf") officeExporter.SavePdf(path, text);
            else if (format == "png") officeExporter.SavePng(path, text);
            else if (format == "md") officeExporter.SaveMd(path, text);
            else officeExporter.SaveTxt(path, text);

            int size = 0;
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length <= int.MaxValue) size = (int)info.Length;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ExportFileExecutor output size: " + ex.Message);
            }

            return CommandResult.Ok(path, false, null, format, size);
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
