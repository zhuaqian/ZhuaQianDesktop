# Epic E — Deferred Integration Patch

Updated: 2026-07-17

The Epic E source modules are written as net-new files:

- `src/Plugins/PluginManifest.cs`
- `src/Agent/Hooks/HookKind.cs`
- `src/Agent/Hooks/HookContext.cs`
- `src/Agent/Hooks/IPluginHook.cs`
- `src/Agent/Hooks/HookRegistry.cs`

They are **not** yet part of the build because registering them requires editing
`src/ZhuaQianDesktop.csproj`, `src/build.ps1`, and `src/tests/TestRunner.cs` —
files currently owned by the in-flight refactor (main-form export extraction +
`outputs/` regen). To coordinate, **do not apply this patch while that refactor is
active.** Apply it after the other process's changes have landed and the build is
green.

Each step is idempotent in intent: only insert where the marker/anchor is present
and the insertion is not already there.

---

## Step 1 — Register new files in `src/ZhuaQianDesktop.csproj`

After the line:

```xml
    <Compile Include="Plugins\PluginRegistry.cs" />
```

add:

```xml
    <Compile Include="Plugins\PluginManifest.cs" />
    <Compile Include="Agent\Hooks\HookKind.cs" />
    <Compile Include="Agent\Hooks\HookContext.cs" />
    <Compile Include="Agent\Hooks\IPluginHook.cs" />
    <Compile Include="Agent\Hooks\HookRegistry.cs" />
```

## Step 2 — Register new files in `src/build.ps1`

After the line:

```powershell
    "Plugins\PluginRegistry.cs"
```

add:

```powershell
    "Plugins\PluginManifest.cs"
    "Agent\Hooks\HookKind.cs"
    "Agent\Hooks\HookContext.cs"
    "Agent\Hooks\IPluginHook.cs"
    "Agent\Hooks\HookRegistry.cs"
```

## Step 3 — Add usings to `src/tests/TestRunner.cs`

The file already has `using ZhuaQianDesktopApp.Agent;` etc. Add:

```csharp
using ZhuaQianDesktopApp.Plugins;
using ZhuaQianDesktopApp.Agent.Hooks;
```

## Step 4 — Wire hooks into `src/Agent/AgentPipeline.cs`

Add the using at the top of the file (after `using ZhuaQianDesktopApp.Core;`):

```csharp
using ZhuaQianDesktopApp.Agent.Hooks;
```

Add a public field next to `RequestApproval`:

```csharp
        public HookRegistry Hooks;
```

In `Run(...)`, immediately before `CommandResult result;` (the `try` block that
calls `executor.Execute(command)`), insert:

```csharp
            if (Hooks != null)
                Hooks.Run(HookKind.BeforeCommand, new HookContext { Kind = HookKind.BeforeCommand, Command = command });
```

Immediately after the `catch (Exception ex) { result = CommandResult.Failed(ex.Message); }`
block (before computing `status`), insert:

```csharp
            if (Hooks != null)
                Hooks.Run(HookKind.AfterCommand, new HookContext { Kind = HookKind.AfterCommand, Command = command, Result = result });
```

In `RunAsync(...)`, apply the same two insertions around the
`if (asyncExec != null) ... else ...` block:

```csharp
            if (Hooks != null)
                Hooks.Run(HookKind.BeforeCommand, new HookContext { Kind = HookKind.BeforeCommand, Command = command });
```

and after the result is assigned:

```csharp
            if (Hooks != null)
                Hooks.Run(HookKind.AfterCommand, new HookContext { Kind = HookKind.AfterCommand, Command = command, Result = result });
```

## Step 5 — Add tests to `src/tests/TestRunner.cs`

Add two calls in `Main()` (next to the other `TestX();` lines):

```csharp
        TestPluginManifest();
        TestHookRegistry();
```

Add the test methods and helper hooks anywhere inside `class TestRunner`:

```csharp
    static void TestPluginManifest()
    {
        Console.WriteLine("[PluginManifest]");
        string dir = Path.Combine(Path.GetTempPath(), "zq_manifest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            string manifestPath = Path.Combine(dir, "plugin.json");
            File.WriteAllText(manifestPath,
                "{\"id\":\"summarize\",\"name\":\"Summarize\",\"version\":\"1.0.0\",\"author\":\"me\"," +
                "\"description\":\"summarize text\",\"entry\":\"run.ps1\",\"entryType\":\"ps1\"," +
                "\"requiredPermissions\":[\"permFileWrite\"],\"hooks\":[\"AfterCommand\"],\"trusted\":true}");
            File.WriteAllText(Path.Combine(dir, "run.ps1"), "Write-Output hi");

            var parser = new PluginManifestParser();
            var ok = parser.ParseFromFile(manifestPath);
            Assert(ok.Success, "valid manifest parses");
            Assert(ok.Manifest != null && ok.Manifest.Id == "summarize", "id parsed");
            Assert(ok.Manifest.EntryType == PluginEntryType.Ps1, "entryType parsed");
            Assert(ok.Manifest.RequiredPermissions.Contains("permFileWrite"), "permission parsed");
            Assert(ok.Manifest.Hooks.Contains("AfterCommand"), "hook parsed");
            Assert(ok.Manifest.Trusted, "trusted parsed");

            var round = parser.ParseFromString(ok.Manifest.ToJson());
            Assert(round.Success && round.Manifest.Id == "summarize", "round-trips via ToJson");

            var bad = parser.ParseFromString("{\"name\":\"x\",\"entry\":\"run.ps1\",\"entryType\":\"ps1\"}");
            Assert(!bad.Success && bad.Errors.Exists(e => e.Contains("id")), "missing id rejected");

            var badPerm = parser.ParseFromString("{\"id\":\"x\",\"name\":\"x\",\"version\":\"1.0.0\",\"entry\":\"run.ps1\",\"entryType\":\"ps1\",\"requiredPermissions\":[\"permNope\"]}");
            Assert(!badPerm.Success && badPerm.Errors.Exists(e => e.Contains("permNope")), "unknown permission rejected");

            var badHook = parser.ParseFromString("{\"id\":\"x\",\"name\":\"x\",\"version\":\"1.0.0\",\"entry\":\"run.ps1\",\"entryType\":\"ps1\",\"hooks\":[\"OnTuesday\"]}");
            Assert(!badHook.Success && badHook.Errors.Exists(e => e.Contains("OnTuesday")), "unknown hook rejected");

            var traverse = parser.ParseFromString("{\"id\":\"x\",\"name\":\"x\",\"version\":\"1.0.0\",\"entry\":\"..\\\\..\\\\evil.ps1\",\"entryType\":\"ps1\"}");
            Assert(!traverse.Success, "path traversal entry rejected");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static void TestHookRegistry()
    {
        Console.WriteLine("[HookRegistry]");
        var reg = new HookRegistry();
        int fired = 0;
        reg.Register(new TestAfterCommandHook("h1", () => fired++));
        Assert(reg.Count(HookKind.AfterCommand) == 1, "hook registered");
        Assert(reg.Get(HookKind.AfterCommand).Count == 1, "Get returns registered");
        reg.Run(HookKind.AfterCommand, new HookContext { Kind = HookKind.AfterCommand });
        Assert(fired == 1, "hook invoked on Run");

        int fired2 = 0;
        reg.Register(new TestAfterCommandHook("h2", () => fired2++));
        reg.Register(new ThrowingHook("hbad"));
        reg.Run(HookKind.AfterCommand, new HookContext { Kind = HookKind.AfterCommand });
        Assert(fired2 == 1, "good hook still runs after a bad one throws");
        Assert(reg.LastErrors.Exists(e => e.HookId == "hbad"), "bad hook error recorded");

        reg.Run(HookKind.BeforeCommand, new HookContext { Kind = HookKind.BeforeCommand });
        Assert(true, "Run on empty kind is safe");
    }

    sealed class TestAfterCommandHook : IPluginHook
    {
        readonly Action _onInvoke;
        public TestAfterCommandHook(string id, Action onInvoke) { Id = id; _onInvoke = onInvoke; }
        public string Id { get; private set; }
        public HookKind Kind { get { return HookKind.AfterCommand; } }
        public void Invoke(HookContext ctx) { _onInvoke(); }
    }

    sealed class ThrowingHook : IPluginHook
    {
        public string Id { get; private set; }
        public ThrowingHook(string id) { Id = id; }
        public HookKind Kind { get { return HookKind.AfterCommand; } }
        public void Invoke(HookContext ctx) { throw new InvalidOperationException("boom"); }
    }
```

## Step 6 — Verify

Run from the repository root (requires the real build machine; the sandbox blocks
`csc.exe`):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\scripts\run-tests.ps1
```

Expected: build OK, `186 + 2` tests passed (the two new module tests), architecture
checks pass. Confirm `ZhuaQianDesktop.cs` line count is unchanged (hooks live in
new files), and `check-architecture.ps1` still passes.
