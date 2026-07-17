namespace ZhuaQianDesktopApp.Agent
{
    public enum CommandStatus
    {
        Success,
        Denied,
        Cancelled,
        Failed
    }

    public sealed class CommandResult
    {
        public CommandStatus Status;
        public string ResultPath;
        public string OutputType;
        public string OutputText;
        public int SizeBytes;
        public string ErrorMessage;
        public bool CanRollback;
        public string RollbackManifestPath;

        public static CommandResult Ok(string resultPath = null, bool canRollback = false, string manifestPath = null, string outputType = "file", int sizeBytes = 0, string outputText = null)
        {
            return new CommandResult { Status = CommandStatus.Success, ResultPath = resultPath, CanRollback = canRollback, RollbackManifestPath = manifestPath, OutputType = outputType ?? "file", SizeBytes = sizeBytes, OutputText = outputText ?? "" };
        }

        public static CommandResult Denied(string reason)
        {
            return new CommandResult { Status = CommandStatus.Denied, ErrorMessage = reason };
        }

        public static CommandResult Cancelled()
        {
            return new CommandResult { Status = CommandStatus.Cancelled };
        }

        public static CommandResult Failed(string errorMessage)
        {
            return new CommandResult { Status = CommandStatus.Failed, ErrorMessage = errorMessage ?? "unknown error" };
        }
    }
}
