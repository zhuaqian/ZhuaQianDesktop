using System;
using System.IO;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Documents;
using ZhuaQianDesktopApp.Plugins;
using ZhuaQianDesktopApp.Tools;

// Verifies that PluginRunExecutor enforces manifest trust at run time once a
// trust store is wired in (the production default via AgentPipelineFactory).
// Mirrors TestPluginTrust but drives the real executor through the pipeline so
// the gating in PluginRunExecutor.Execute is exercised end to end.
static class TestPluginTrustEnforcement
{
    public static int RunAll()
    {
        int failures = 0;
        failures += TestNoManifestBlockedWhenEnforced();
        failures += TestUnsignedManifestBlockedWithoutConsent();
        failures += TestUnsignedManifestAllowedWithConsent();
        failures += TestSignedKnownManifestRuns();
        Console.WriteLine("[TestPluginTrustEnforcement] failures=" + failures);
        return failures;
    }

    static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "zq-trust-env-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Directory.CreateDirectory(d);
        return d;
    }

    static string WriteEchoPlugin(string dir)
    {
        string p = Path.Combine(dir, "echo.py");
        File.WriteAllText(p, "import sys\nsys.stdout.write('plugin:' + sys.stdin.read())", System.Text.Encoding.UTF8);
        return p;
    }

    // No manifest + enforcement on (allowUntrustedPlugins=false) -> rejected.
    static int TestNoManifestBlockedWhenEnforced()
    {
        int fails = 0;
        string dir = TempDir();
        try
        {
            string plugin = WriteEchoPlugin(dir);
            var gate = new PermissionGate();
            gate.Set("permPluginRun", PermissionLevel.Allow);
            var factory = new AgentPipelineFactory(Path.Combine(dir, "audit.log"), dir, new OutputsHub(dir), new OfficeExporter(), new WebSearchClient());
            var pipeline = factory.Create(gate, dir, false, null, null, false); // enforcement ON
            var cmd = new AgentCommand("RunPlugin", "permPluginRun", "task1", plugin, "run", new System.Collections.Generic.Dictionary<string, object> { { "stdin", "hi" } });
            var res = pipeline.Run(cmd);
            Assert(res.Status == CommandStatus.Failed, "no-manifest plugin is blocked under trust enforcement");
            Assert((res.ErrorMessage ?? "").IndexOf("no manifest", StringComparison.OrdinalIgnoreCase) >= 0, "block reason mentions missing manifest");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
        return fails;
    }

    // Manifest present but unsigned, no consent callback -> rejected.
    static int TestUnsignedManifestBlockedWithoutConsent()
    {
        int fails = 0;
        string dir = TempDir();
        try
        {
            string plugin = WriteEchoPlugin(dir);
            var m = new PluginManifest { Id = "u1", Name = "Unsigned", Entry = "echo.py", EntryType = PluginEntryType.Py };
            File.WriteAllText(Path.Combine(dir, "echo.json"), m.ToJson());
            var gate = new PermissionGate();
            gate.Set("permPluginRun", PermissionLevel.Allow);
            var factory = new AgentPipelineFactory(Path.Combine(dir, "audit.log"), dir, new OutputsHub(dir), new OfficeExporter(), new WebSearchClient());
            var pipeline = factory.Create(gate, dir, false, null, null, false);
            var cmd = new AgentCommand("RunPlugin", "permPluginRun", "task1", plugin, "run", new System.Collections.Generic.Dictionary<string, object> { { "stdin", "hi" } });
            var res = pipeline.Run(cmd);
            Assert(res.Status == CommandStatus.Failed, "unsigned manifest without consent is blocked");
            Assert((res.ErrorMessage ?? "").IndexOf("Untrusted plugin blocked", StringComparison.OrdinalIgnoreCase) >= 0, "block reason mentions untrusted");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
        return fails;
    }

    // Manifest present but unsigned, consent callback approves -> runs.
    static int TestUnsignedManifestAllowedWithConsent()
    {
        int fails = 0;
        string dir = TempDir();
        try
        {
            string plugin = WriteEchoPlugin(dir);
            var m = new PluginManifest { Id = "u2", Name = "Unsigned", Entry = "echo.py", EntryType = PluginEntryType.Py };
            File.WriteAllText(Path.Combine(dir, "echo.json"), m.ToJson());
            var gate = new PermissionGate();
            gate.Set("permPluginRun", PermissionLevel.Allow);
            var factory = new AgentPipelineFactory(Path.Combine(dir, "audit.log"), dir, new OutputsHub(dir), new OfficeExporter(), new WebSearchClient());
            var pipeline = factory.Create(gate, dir, false, null, (man) => true, false); // consent approves
            var cmd = new AgentCommand("RunPlugin", "permPluginRun", "task1", plugin, "run", new System.Collections.Generic.Dictionary<string, object> { { "stdin", "hi" } });
            var res = pipeline.Run(cmd);
            Assert(res.Status == CommandStatus.Success, "unsigned manifest with consent runs; status=" + res.Status + " err=" + (res.OutputText ?? ""));
            Assert((res.OutputText ?? "").Contains("plugin:hi"), "plugin output returned");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
        return fails;
    }

    // Signed manifest from a known publisher -> runs without a consent prompt.
    static int TestSignedKnownManifestRuns()
    {
        int fails = 0;
        string dir = TempDir();
        try
        {
            string plugin = WriteEchoPlugin(dir);
            var keys = PluginTrust.GenerateKeyPair();
            var storePath = Path.Combine(dir, "trusted-publishers.json");
            var store = new PluginTrustStore(storePath);
            store.AddPublisher("alice", keys[1]);
            var m = new PluginManifest { Id = "s1", Name = "Signed", Publisher = "alice", Entry = "echo.py", EntryType = PluginEntryType.Py };
            string json = m.ToJson();
            m.Signature = PluginTrust.SignManifestJson(json, keys[0]);
            File.WriteAllText(Path.Combine(dir, "echo.json"), m.ToJson());

            var gate = new PermissionGate();
            gate.Set("permPluginRun", PermissionLevel.Allow);
            var factory = new AgentPipelineFactory(Path.Combine(dir, "audit.log"), dir, new OutputsHub(dir), new OfficeExporter(), new WebSearchClient());
            var pipeline = factory.Create(gate, dir, false, null, null, false);
            var cmd = new AgentCommand("RunPlugin", "permPluginRun", "task1", plugin, "run", new System.Collections.Generic.Dictionary<string, object> { { "stdin", "hi" } });
            var res = pipeline.Run(cmd);
            Assert(res.Status == CommandStatus.Success, "signed known-publisher manifest runs; status=" + res.Status + " err=" + (res.OutputText ?? ""));
            Assert((res.OutputText ?? "").Contains("plugin:hi"), "plugin output returned");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
        return fails;
    }

    static void Assert(bool cond, string msg) { if (!cond) Console.WriteLine("  FAIL: " + msg); }
}
