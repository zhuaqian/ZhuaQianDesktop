using System;
using System.Collections.Generic;

namespace ZhuaQianDesktopApp.Agent.Hooks
{
    // Registry of plugin hooks, keyed by HookKind.
    //
    // Invoked from AgentPipeline (BeforeCommand / AfterCommand), from executors
    // (BeforeFileWrite), and from the provider layer (Before/AfterModelCall).
    // Each hook runs inside a try/catch so a bad hook can never break the command
    // it observes. This mirrors the project rule that every side-effect path must
    // be guarded and survivable.
    //
    // Command hooks, model hooks, and file-write hooks all implement IPluginHook
    // and are registered here by their HookKind. A parallel draft
    // (src/Agent/ICommandHook.cs) was deleted as dead code during the in-flight
    // refactor; IPluginHook is the single hook contract for the project, so there
    // is no competing command-hook interface to reconcile.
    public sealed class HookRegistry
    {
        readonly Dictionary<HookKind, List<IPluginHook>> _hooks =
            new Dictionary<HookKind, List<IPluginHook>>();

        public void Register(IPluginHook hook)
        {
            if (hook == null) return;
            if (!_hooks.TryGetValue(hook.Kind, out var list))
            {
                list = new List<IPluginHook>();
                _hooks[hook.Kind] = list;
            }
            list.Add(hook);
        }

        public IReadOnlyList<IPluginHook> Get(HookKind kind)
        {
            if (_hooks.TryGetValue(kind, out var list)) return list.AsReadOnly();
            return new List<IPluginHook>().AsReadOnly();
        }

        public int Count(HookKind kind)
        {
            return _hooks.TryGetValue(kind, out var list) ? list.Count : 0;
        }

        // Run every hook for the kind. Synchronous and isolated: a throwing hook
        // is caught and recorded but does not affect other hooks or the pipeline.
        public void Run(HookKind kind, HookContext context)
        {
            if (context == null) return;
            if (!_hooks.TryGetValue(kind, out var list)) return;
            foreach (var hook in list)
            {
                try { hook.Invoke(context); }
                catch (Exception ex)
                {
                    try { LastErrors.Add(new HookError { HookId = hook.Id ?? "", Kind = kind, Message = ex.Message }); }
                    catch { }
                }
            }
        }

        public readonly List<HookError> LastErrors = new List<HookError>();
    }

    public sealed class HookError
    {
        public string HookId = "";
        public HookKind Kind;
        public string Message = "";
    }
}
