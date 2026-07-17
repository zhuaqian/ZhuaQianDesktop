using System.Threading;
using System.Threading.Tasks;

namespace ZhuaQianDesktopApp.Agent
{
    // Executors whose work is inherently asynchronous (waits, long-running plugins,
    // polling) implement this interface. The pipeline awaits ExecuteAsync on a background
    // thread so the UI stays responsive while the next plan step still waits for this one
    // to truly finish, instead of each executor inventing its own ThreadPool hack.
    public interface IAsyncCommandExecutor : ICommandExecutor
    {
        Task<CommandResult> ExecuteAsync(IAgentCommand command, CancellationToken token);
    }
}
