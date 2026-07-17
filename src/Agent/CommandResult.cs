namespace ZhuaQianDesktopApp.Agent
{
    public enum CommandStatus
    {
        Success,
        Denied,
        Cancelled,
        Failed
    }

    // Progress state of a command result. Completed/Failed are terminal; Pending is
    // reserved for future non-blocking progress reporting before a result settles.
    public enum CommandExecutionState
    {
        Completed,
        Failed,
        Pending
    }

    public sealed class CommandResult
    {
        public CommandStatus Status;
        public CommandExecutionState State;
        public string ResultPath;
        public string OutputType;
        public string OutputText;
        public int SizeBytes;
        public string ErrorMessage;
        public bool CanRollback;
        public string RollbackManifestPath;

        public static CommandResult Ok(string resultPath = null, bool canRollback = false, string manifestPath = null, string outputType = "file", int sizeBytes = 0, string outputText = null)
        {
            return new CommandResult { Status = CommandStatus.Success, State = CommandExecutionState.Completed, ResultPath = resultPath, CanRollback = canRollback, RollbackManifestPath = manifestPath, OutputType = outputType ?? "file", SizeBytes = sizeBytes, OutputText = outputText ?? "" };
        }

        public static CommandResult Denied(string reason)
        {
            return new CommandResult { Status = CommandStatus.Denied, State = CommandExecutionState.Failed, ErrorMessage = reason };
        }

        public static CommandResult Cancelled()
        {
            return new CommandResult { Status = CommandStatus.Cancelled, State = CommandExecutionState.Failed };
        }

        public static CommandResult Failed(string errorMessage)
        {
            return new CommandResult { Status = CommandStatus.Failed, State = CommandExecutionState.Failed, ErrorMessage = errorMessage ?? "unknown error" };
        }
    }
}
