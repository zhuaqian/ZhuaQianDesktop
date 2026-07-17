namespace ZhuaQianDesktopApp.Agent.Hooks
{
    // A plugin hook. Hooks are lightweight, synchronous observers.
    //
    // Contract:
    //  - Invoke must be fast and must not block the pipeline.
    //  - If a hook needs async work it should start its own Task and return.
    //  - A throwing hook is isolated by HookRegistry and never aborts the command
    //    it observes.
    public interface IPluginHook
    {
        string Id { get; }
        HookKind Kind { get; }
        void Invoke(HookContext context);
    }
}
