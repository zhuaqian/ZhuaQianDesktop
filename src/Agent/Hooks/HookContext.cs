using System.Collections.Generic;
using ZhuaQianDesktopApp.Agent;

namespace ZhuaQianDesktopApp.Agent.Hooks
{
    // Context passed to a hook when it fires. Fields are filled per HookKind:
    //   BeforeCommand / AfterCommand -> Command (+ Result for AfterCommand)
    //   BeforeModelCall / AfterModelCall -> ModelRequest (+ ModelResponse)
    //   BeforeFileWrite -> FilePath
    // Properties is a free-form bag for hook-specific data.
    public sealed class HookContext
    {
        public HookKind Kind;
        public IAgentCommand Command;
        public CommandResult Result;
        public string FilePath = "";
        public string ModelRequest = "";
        public string ModelResponse = "";
        public readonly Dictionary<string, object> Properties = new Dictionary<string, object>();
    }
}
