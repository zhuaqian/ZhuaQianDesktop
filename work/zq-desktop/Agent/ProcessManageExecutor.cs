using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ZhuaQianDesktopApp.Agent
{
    public sealed class ProcessManageExecutor : ICommandExecutor
    {
        public string CommandType { get { return "EndProcess"; } }

        public CommandResult Execute(IAgentCommand command)
        {
            int pid;
            if (!TryGetPid(command, out pid) || pid <= 0)
                return CommandResult.Failed("missing or invalid pid");

            try
            {
                using (var process = Process.GetProcessById(pid))
                {
                    string name = process.ProcessName;
                    process.Kill();
                    return CommandResult.Ok(null, false, null, "process", 0, "Ended process " + name + " (PID " + pid + ").");
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Failed("End process failed for PID " + pid + ": " + ex.Message);
            }
        }

        static bool TryGetPid(IAgentCommand command, out int pid)
        {
            pid = 0;
            if (command == null) return false;
            if (int.TryParse(command.Target, out pid)) return true;

            object value;
            IReadOnlyDictionary<string, object> values = command.Parameters;
            if (values != null && values.TryGetValue("pid", out value) && value != null)
                return int.TryParse(Convert.ToString(value), out pid);
            return false;
        }
    }
}
