using System;
using System.Collections.Generic;
using System.IO;
using ZhuaQianDesktopApp.Core;

// Verifies the natural-language policy compiler (roadmap 2.4) turns phrases
// into IPermissionRule objects that behave as described. Global static class so
// TestRunner can call it unqualified (matches the existing test convention).
static class TestPolicyCompiler
{
    public static int RunAll()
    {
        int failures = 0;

        // 1) delete-larger-than rule denies big files, allows small ones.
        {
            string big = Path.Combine(Path.GetTempPath(), "zq-pol-big-" + Guid.NewGuid().ToString("N") + ".tmp");
            string small = Path.Combine(Path.GetTempPath(), "zq-pol-small-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(big, new byte[11 * 1024 * 1024]); // 11 MB
                File.WriteAllText(small, "tiny");
                var rules = PolicyCompiler.Compile("不要删除超过 10 MB 的文件", null, new List<string>());
                Assert(rules.Count == 1, "expected 1 rule", ref failures);
                Assert(rules[0].Evaluate("permFileMoveDelete", big) == PermissionDecision.Deny, "big file should be denied", ref failures);
                Assert(rules[0].Evaluate("permFileMoveDelete", small) != PermissionDecision.Deny, "small file should NOT be denied", ref failures);
                Assert(rules[0].Evaluate("permFileWrite", big) == null, "unrelated action should abstain", ref failures);
            }
            finally { TryDelete(big); TryDelete(small); }
        }

        // 2) no-network-after-hour denies at night, allows by day (fixed clock).
        {
            var nightClock = new PolicyCompiler.NowProvider(() => new DateTime(2026, 1, 1, 23, 0, 0));
            var dayClock = new PolicyCompiler.NowProvider(() => new DateTime(2026, 1, 1, 9, 0, 0));
            var nightRules = PolicyCompiler.Compile("晚上10点后不要联网", nightClock, new List<string>());
            var dayRules = PolicyCompiler.Compile("晚上10点后不要联网", dayClock, new List<string>());
            Assert(nightRules.Count == 1, "expected 1 network rule", ref failures);
            Assert(nightRules[0].Evaluate("permNetworkUpload", "http://x") == PermissionDecision.Deny, "night upload denied", ref failures);
            Assert(dayRules[0].Evaluate("permNetworkUpload", "http://x") != PermissionDecision.Deny, "day upload allowed", ref failures);
        }

        // 3) forbid-plugin keyword denies plugin runs.
        {
            var rules = PolicyCompiler.Compile("禁止运行插件", null, new List<string>());
            Assert(rules.Count == 1, "expected 1 plugin rule", ref failures);
            Assert(rules[0].Evaluate("permPluginRun", "x.py") == PermissionDecision.Deny, "plugin run denied", ref failures);
            Assert(rules[0].Evaluate("permFileWrite", "x.txt") == null, "plugin rule abstains on other actions", ref failures);
        }

        // 4) empty input yields no rules.
        {
            var rules = PolicyCompiler.Compile("", null, new List<string>());
            Assert(rules.Count == 0, "empty input -> no rules", ref failures);
        }

        return failures;
    }

    static void Assert(bool cond, string msg, ref int failures)
    {
        if (!cond) { failures++; Console.Error.WriteLine("TestPolicyCompiler FAIL: " + msg); }
    }

    static void TryDelete(string p)
    {
        try { if (File.Exists(p)) File.Delete(p); } catch { }
    }
}
