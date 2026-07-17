namespace ZhuaQianDesktopApp.Agent.Hooks
{
    // Pipeline hook extension points (Epic E2).
    //
    // These are the surfaces a safe plugin may observe. The first integration
    // (see docs/patches/EPIC_E_INTEGRATION.md) wires BeforeCommand / AfterCommand
    // into AgentPipeline. BeforeFileWrite and Before/AfterModelCall are defined
    // here so executors and the provider layer can adopt them without further
    // schema changes.
    public enum HookKind
    {
        BeforeModelCall,   // before a provider/model request is sent
        AfterModelCall,    // after a provider/model response is received
        BeforeCommand,     // before an IAgentCommand executes in AgentPipeline
        AfterCommand,      // after an IAgentCommand finishes (success or failure)
        BeforeFileWrite    // before a real local file is written by an executor
    }
}
