using System;
using System.Collections.Generic;
using System.IO;
using ZhuaQianDesktopApp.Tools;

namespace ZhuaQianDesktopApp.Agent
{
    public sealed class RollbackExecutor : ICommandExecutor
    {
        readonly string rollbackDir;
        readonly string configDir;

        public RollbackExecutor(string configDir)
        {
            this.configDir = configDir ?? "";
            rollbackDir = Path.Combine(this.configDir, "rollback");
        }

        public string CommandType { get { return "RollbackFiles"; } }

        public CommandResult Execute(IAgentCommand command)
        {
            string manifestPath = command.Target;
            if (string.IsNullOrWhiteSpace(manifestPath))
                manifestPath = GetString(command.Parameters, "manifestPath");
            if (string.IsNullOrWhiteSpace(manifestPath))
                return CommandResult.Failed("missing rollback manifest path");
            if (!File.Exists(manifestPath))
                return CommandResult.Failed("rollback manifest does not exist: " + manifestPath);
            if (!IsInsideRollbackDirectory(manifestPath))
                return CommandResult.Failed("rollback manifest must be inside the rollback directory: " + rollbackDir);

            var organizer = new FolderOrganizer(configDir);
            int restored = organizer.Rollback(manifestPath);
            int size = 0;
            try
            {
                var info = new FileInfo(manifestPath);
                if (info.Exists && info.Length <= int.MaxValue) size = (int)info.Length;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RollbackExecutor manifest size: " + ex.Message);
            }

            string output = "Restored " + restored + " files from rollback manifest.";
            return CommandResult.Ok(manifestPath, false, null, "rollback", size, output);
        }

        bool IsInsideRollbackDirectory(string manifestPath)
        {
            string root = Path.GetFullPath(rollbackDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(manifestPath);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
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
