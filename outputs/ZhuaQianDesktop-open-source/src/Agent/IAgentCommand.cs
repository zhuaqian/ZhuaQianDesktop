using System.Collections.Generic;

namespace ZhuaQianDesktopApp.Agent
{
    public interface IAgentCommand
    {
        string CommandType { get; }
        string PermissionName { get; }
        string TaskId { get; }
        string Target { get; }
        string DisplaySummary { get; }
        IReadOnlyDictionary<string, object> Parameters { get; }
    }

    public sealed class AgentCommand : IAgentCommand
    {
        public string CommandType { get; private set; }
        public string PermissionName { get; private set; }
        public string TaskId { get; private set; }
        public string Target { get; private set; }
        public string DisplaySummary { get; private set; }
        public IReadOnlyDictionary<string, object> Parameters { get; private set; }

        public AgentCommand(string commandType, string taskId, string target,
            string displaySummary, Dictionary<string, object> parameters)
            : this(commandType, commandType, taskId, target, displaySummary, parameters)
        {
        }

        public AgentCommand(string commandType, string permissionName, string taskId, string target,
            string displaySummary, Dictionary<string, object> parameters)
        {
            CommandType = commandType;
            PermissionName = string.IsNullOrWhiteSpace(permissionName) ? commandType : permissionName;
            TaskId = taskId ?? "";
            Target = target ?? "";
            DisplaySummary = displaySummary ?? "";
            Parameters = parameters ?? new Dictionary<string, object>();
        }
    }
}
