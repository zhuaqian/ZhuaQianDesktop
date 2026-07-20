using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp.Agent
{
    // Writes a text (or base64 binary) file to disk through the single command
    // pipeline. This is the generic "save to disk" executor that backs the
    // "save markdown document", "build website" and "optimize office" flows, so
    // every file write is gated by PermissionGate (permFileWrite) and audited.
    //
    // Parameters:
    //   content : UTF-8 text to write (text mode)
    //   base64  : base64-encoded bytes to write instead (binary mode, e.g. an
    //             optimized .docx/.pptx/.xlsx produced in memory)
    public sealed class WriteFileExecutor : ICommandExecutor
    {
        public string CommandType { get { return "WriteFile"; } }

        public CommandResult Execute(IAgentCommand command)
        {
            string path = command.Target;
            if (string.IsNullOrWhiteSpace(path))
                return CommandResult.Failed("missing target path for WriteFile");

            string base64 = GetString(command.Parameters, "base64");
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            int size = 0;
            if (!string.IsNullOrEmpty(base64))
            {
                byte[] data;
                try { data = Convert.FromBase64String(base64); }
                catch (Exception ex) { return CommandResult.Failed("invalid base64 content: " + ex.Message); }
                File.WriteAllBytes(path, data);
                size = data.Length;
            }
            else
            {
                string content = GetString(command.Parameters, "content") ?? "";
                File.WriteAllText(path, content, Encoding.UTF8);
                size = Encoding.UTF8.GetByteCount(content);
            }

            return CommandResult.Ok(path, false, null, "file", size,
                "wrote " + path + " (" + size + " bytes)");
        }

        static string GetString(IReadOnlyDictionary<string, object> values, string key)
        {
            object value;
            if (values != null && values.TryGetValue(key, out value) && value != null)
                return Convert.ToString(value);
            return null;
        }
    }
}
