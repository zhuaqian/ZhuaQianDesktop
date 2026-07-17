using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp.Agent
{
    public sealed class AgentPipeline
    {
        readonly PermissionGate permissionGate;
        readonly AuditLog auditLog;
        readonly OutputsHub outputsHub;
        readonly Dictionary<string, ICommandExecutor> executors = new Dictionary<string, ICommandExecutor>();

        public Func<IAgentCommand, bool> RequestApproval;

        public AgentPipeline(PermissionGate permissionGate, AuditLog auditLog, OutputsHub outputsHub)
        {
            this.permissionGate = permissionGate;
            this.auditLog = auditLog;
            this.outputsHub = outputsHub;
        }

        public void Register(ICommandExecutor executor)
        {
            executors[executor.CommandType] = executor;
        }

        public bool HasExecutor(string commandType)
        {
            return !string.IsNullOrWhiteSpace(commandType) && executors.ContainsKey(commandType);
        }

        public CommandResult Run(IAgentCommand command)
        {
            string permissionName = string.IsNullOrWhiteSpace(command.PermissionName) ? command.CommandType : command.PermissionName;
            var decision = permissionGate.Check(permissionName, command.Target);

            if (decision == PermissionDecision.Deny)
            {
                auditLog.Log(command.CommandType, command.DisplaySummary, "agent", command.TaskId, "denied");
                auditLog.Flush();
                return CommandResult.Denied("permission denied for " + permissionName);
            }

            if (decision == PermissionDecision.Ask)
            {
                if (RequestApproval == null || !RequestApproval(command))
                {
                    auditLog.Log(command.CommandType, command.DisplaySummary, "agent", command.TaskId, "cancelled");
                    auditLog.Flush();
                    return CommandResult.Cancelled();
                }
            }

            ICommandExecutor executor;
            if (!executors.TryGetValue(command.CommandType, out executor))
            {
                var missing = CommandResult.Failed("no executor registered for " + command.CommandType);
                auditLog.Log(command.CommandType, command.DisplaySummary, "agent", command.TaskId, "failed");
                auditLog.Flush();
                return missing;
            }

            CommandResult result;
            try
            {
                result = executor.Execute(command);
            }
            catch (Exception ex)
            {
                result = CommandResult.Failed(ex.Message);
            }

            var status = result.Status == CommandStatus.Success ? "ok" : "failed";
            auditLog.Log(command.CommandType, command.DisplaySummary, "agent", command.TaskId, status);
            auditLog.Flush();

            if (result.Status == CommandStatus.Success && !string.IsNullOrEmpty(result.ResultPath))
            {
                string taskTitle = "";
                object taskTitleObj;
                if (command.Parameters != null && command.Parameters.TryGetValue("taskTitle", out taskTitleObj) && taskTitleObj != null)
                    taskTitle = taskTitleObj.ToString();
                outputsHub.RecordOutput(command.CommandType, string.IsNullOrWhiteSpace(result.OutputType) ? "file" : result.OutputType, result.ResultPath, command.TaskId, taskTitle, "", result.SizeBytes);
            }

            return result;
        }

        public async Task<CommandResult> RunAsync(IAgentCommand command, CancellationToken token = default)
        {
            string permissionName = string.IsNullOrWhiteSpace(command.PermissionName) ? command.CommandType : command.PermissionName;
            var decision = permissionGate.Check(permissionName, command.Target);

            if (decision == PermissionDecision.Deny)
            {
                auditLog.Log(command.CommandType, command.DisplaySummary, "agent", command.TaskId, "denied");
                auditLog.Flush();
                return CommandResult.Denied("permission denied for " + permissionName);
            }

            if (decision == PermissionDecision.Ask)
            {
                if (RequestApproval == null || !RequestApproval(command))
                {
                    auditLog.Log(command.CommandType, command.DisplaySummary, "agent", command.TaskId, "cancelled");
                    auditLog.Flush();
                    return CommandResult.Cancelled();
                }
            }

            ICommandExecutor executor;
            if (!executors.TryGetValue(command.CommandType, out executor))
            {
                var missing = CommandResult.Failed("no executor registered for " + command.CommandType);
                auditLog.Log(command.CommandType, command.DisplaySummary, "agent", command.TaskId, "failed");
                auditLog.Flush();
                return missing;
            }

            CommandResult result;
            try
            {
                var asyncExec = executor as IAsyncCommandExecutor;
                if (asyncExec != null)
                    result = await Task.Run(() => asyncExec.ExecuteAsync(command, token), token);
                else
                    result = executor.Execute(command);
            }
            catch (Exception ex)
            {
                result = CommandResult.Failed(ex.Message);
            }

            var status = result.Status == CommandStatus.Success ? "ok" : "failed";
            auditLog.Log(command.CommandType, command.DisplaySummary, "agent", command.TaskId, status);
            auditLog.Flush();

            if (result.Status == CommandStatus.Success && !string.IsNullOrEmpty(result.ResultPath))
            {
                string taskTitle = "";
                object taskTitleObj;
                if (command.Parameters != null && command.Parameters.TryGetValue("taskTitle", out taskTitleObj) && taskTitleObj != null)
                    taskTitle = taskTitleObj.ToString();
                outputsHub.RecordOutput(command.CommandType, string.IsNullOrWhiteSpace(result.OutputType) ? "file" : result.OutputType, result.ResultPath, command.TaskId, taskTitle, "", result.SizeBytes);
            }

            return result;
        }
    }
}
