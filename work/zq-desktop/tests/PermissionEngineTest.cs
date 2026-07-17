// PermissionEngineTest.cs
// Deterministic unit tests for the real Core/PermissionGate engine covering
// P0.1 three-tier, P0.2 glob patterns, P0.3 temp/always/deny cache,
// P0.4 auto mode, P0.5 external-directory scope, and JSON persistence.

using System;
using System.Collections.Generic;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp.Tests
{
    internal class PermissionEngineTest
    {
        static int passed = 0;
        static int failed = 0;

        static void Check(string name, bool ok, string detail = null)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + (detail != null ? "  -> " + detail : "")); }
        }

        static void Main()
        {
            TestThreeTier();
            TestGlobPatterns();
            TestAutoMode();
            TestExternalDirectory();
            TestRememberAlways();
            TestSessionOnce();
            TestJsonRoundtrip();

            Console.WriteLine("");
            Console.WriteLine("=== PERMISSION ENGINE RESULT: passed=" + passed + " failed=" + failed + " ===");
            Environment.Exit(failed == 0 ? 0 : 1);
        }

        // P0.1
        static void TestThreeTier()
        {
            Console.WriteLine("[P0.1 three-tier]");
            var g = new PermissionGate();
            g.Set("permFileRead", PermissionLevel.Allow);
            g.Set("permFileWrite", PermissionLevel.Deny);
            g.Set("permPluginRun", PermissionLevel.Ask);
            Check("Allow -> Allow", g.Check("permFileRead", "x.txt") == PermissionDecision.Allow);
            Check("Deny -> Deny", g.Check("permFileWrite", "x.txt") == PermissionDecision.Deny);
            Check("Ask -> Ask (needs confirmation)", g.Check("permPluginRun", "p.py") == PermissionDecision.Ask);
            Check("unknown -> Ask by default", g.Check("permUnknown", "x") == PermissionDecision.Ask);
        }

        // P0.2
        static void TestGlobPatterns()
        {
            Console.WriteLine("[P0.2 glob patterns]");
            var g = new PermissionGate();
            g.Set("permPluginRun", PermissionLevel.Allow);
            g.Remember("permPluginRun", "*.exe", PermissionLevel.Deny);
            g.Remember("permPluginRun", "backup-*", PermissionLevel.Allow);

            Check("base Allow for .py", g.Check("permPluginRun", "tool.py") == PermissionDecision.Allow);
            Check("pattern denies .exe", g.Check("permPluginRun", "virus.exe") == PermissionDecision.Deny);
            Check("pattern allows backup-*", g.Check("permPluginRun", "backup-reports.ps1") == PermissionDecision.Allow);

            // base Deny, but an allow pattern opens a safe subset.
            var g2 = new PermissionGate();
            g2.Set("permFileWrite", PermissionLevel.Deny);
            g2.Remember("permFileWrite", "C:\\safe\\*", PermissionLevel.Allow);
            Check("base Deny blocks outside", g2.Check("permFileWrite", "C:\\other\\a.txt") == PermissionDecision.Deny);
            Check("allow pattern opens safe dir", g2.Check("permFileWrite", "C:\\safe\\a.txt") == PermissionDecision.Allow);
        }

        // P0.4
        static void TestAutoMode()
        {
            Console.WriteLine("[P0.4 auto mode]");
            var g = new PermissionGate();
            g.Set("permPluginRun", PermissionLevel.Ask);
            g.Set("permFileWrite", PermissionLevel.Deny);
            Check("Ask stays Ask without auto", g.Check("permPluginRun", "p.py") == PermissionDecision.Ask);
            g.AutoMode = true;
            Check("auto converts Ask -> Allow", g.Check("permPluginRun", "p.py") == PermissionDecision.Allow);
            Check("auto never overrides Deny", g.Check("permFileWrite", "a.txt") == PermissionDecision.Deny);
        }

        // P0.5
        static void TestExternalDirectory()
        {
            Console.WriteLine("[P0.5 external directory]");
            var g = new PermissionGate();
            g.Set("permFileWrite", PermissionLevel.Allow);
            Check("no scope set -> any path Allow", g.Check("permFileWrite", "D:\\far\\a.txt") == PermissionDecision.Allow);

            g.AllowedDirectories = new List<string> { "C:\\projects" };
            Check("inside allowed dir stays Allow", g.Check("permFileWrite", "C:\\projects\\a.txt") == PermissionDecision.Allow);
            Check("outside allowed dir escalates Allow -> Ask", g.Check("permFileWrite", "D:\\far\\a.txt") == PermissionDecision.Ask);

            // Deny outside scope stays Deny (no downgrade).
            var g2 = new PermissionGate();
            g2.Set("permFileWrite", PermissionLevel.Deny);
            g2.AllowedDirectories = new List<string> { "C:\\projects" };
            Check("Deny outside scope stays Deny", g2.Check("permFileWrite", "D:\\far\\a.txt") == PermissionDecision.Deny);

            // Non-file action is not affected by external-dir scope.
            var g3 = new PermissionGate();
            g3.Set("permNetworkUpload", PermissionLevel.Allow);
            g3.AllowedDirectories = new List<string> { "C:\\projects" };
            Check("non-file action ignores scope", g3.Check("permNetworkUpload", "anything") == PermissionDecision.Allow);
        }

        // P0.3 - persistent remember (always)
        static void TestRememberAlways()
        {
            Console.WriteLine("[P0.3 remember always]");
            var g = new PermissionGate();
            g.Set("permFileWrite", PermissionLevel.Allow);
            g.RememberDeny("permFileWrite", "C:\\secret\\*");
            Check("remember-deny overrides base Allow", g.Check("permFileWrite", "C:\\secret\\x.txt") == PermissionDecision.Deny);
            Check("other paths still Allow", g.Check("permFileWrite", "C:\\pub\\x.txt") == PermissionDecision.Allow);

            var g2 = new PermissionGate();
            g2.Set("permFileWrite", PermissionLevel.Deny);
            g2.RememberAllow("permFileWrite", "C:\\pub\\*");
            Check("remember-allow overrides base Deny", g2.Check("permFileWrite", "C:\\pub\\x.txt") == PermissionDecision.Allow);
        }

        // P0.3 - session-only once overrides
        static void TestSessionOnce()
        {
            Console.WriteLine("[P0.3 allow/deny once (session)]");
            var g = new PermissionGate();
            g.Set("permFileWrite", PermissionLevel.Deny);
            g.AllowOnce("permFileWrite", "C:\\pub\\x.txt");
            Check("AllowOnce overrides Deny for that target", g.Check("permFileWrite", "C:\\pub\\x.txt") == PermissionDecision.Allow);
            Check("AllowOnce does not leak to other paths", g.Check("permFileWrite", "C:\\pub\\y.txt") == PermissionDecision.Deny);

            var g2 = new PermissionGate();
            g2.Set("permFileWrite", PermissionLevel.Allow);
            g2.DenyOnce("permFileWrite", "C:\\secret\\x.txt");
            Check("DenyOnce overrides Allow for that target", g2.Check("permFileWrite", "C:\\secret\\x.txt") == PermissionDecision.Deny);

            g2.ClearSession();
            Check("ClearSession restores base Allow", g2.Check("permFileWrite", "C:\\secret\\x.txt") == PermissionDecision.Allow);
        }

        static void TestJsonRoundtrip()
        {
            Console.WriteLine("[persistence: JSON roundtrip]");
            var g = new PermissionGate();
            g.Set("permFileRead", PermissionLevel.Allow);
            g.Set("permPluginRun", PermissionLevel.Ask);
            g.Remember("permPluginRun", "*.exe", PermissionLevel.Deny);
            g.AutoMode = true;
            g.AllowedDirectories = new List<string> { "C:\\projects" };
            // session cache must NOT be persisted
            g.AllowOnce("permFileWrite", "C:\\tmp\\x.txt");

            string json = g.ToJson();
            var r = PermissionGate.FromJson(json);

            Check("perms roundtrip (Allow)", r.Get("permFileRead") == PermissionLevel.Allow);
            Check("perms roundtrip (Ask)", r.Get("permPluginRun") == PermissionLevel.Ask);
            Check("pattern roundtrip denies .exe", r.Check("permPluginRun", "virus.exe") == PermissionDecision.Deny);
            Check("autoMode roundtrip", r.AutoMode);
            Check("allowed dirs roundtrip", r.AllowedDirectories.Count == 1 && r.AllowedDirectories[0] == "C:\\projects");
            Check("session cache NOT persisted", r.Check("permFileWrite", "C:\\tmp\\x.txt") == PermissionDecision.Ask);
        }
    }
}
