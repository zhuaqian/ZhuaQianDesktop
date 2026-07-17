namespace ZhuaQianDesktopApp.Agent
{
    public interface ICommandExecutor
    {
        string CommandType { get; }
        CommandResult Execute(IAgentCommand command);
    }
}
