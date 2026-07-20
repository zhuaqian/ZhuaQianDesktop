using System;
using System.IO;
using ZhuaQianDesktopApp.Core;

// Validates the tamper-evident hash chain in AuditLog WITHOUT any real model or UI.
// Uses a throwaway temp file per test so it runs cleanly in the raw-csc test build.
static class TestAuditLog
{
    public static int RunAll()
    {
        int failures = 0;
        failures += TestChainIntactAfterWrites();
        failures += TestTamperDetected();
        failures += TestLegacyLinesSkipped();
        Console.WriteLine("[TestAuditLog] failures=" + failures);
        return failures;
    }

    static string TempPath()
    {
        return Path.Combine(Path.GetTempPath(), "zq-auditlog-test-" + Guid.NewGuid().ToString("N") + ".log");
    }

    static int TestChainIntactAfterWrites()
    {
        int fails = 0;
        string p = TempPath();
        try
        {
            var log = new AuditLog(p);
            log.Log("WriteFile", "saved report.md", "agent", "t1", "ok");
            log.Log("PublishRepo", "pushed to github", "user", "t1", "ok");
            log.Flush();
            var r = log.VerifyChain();
            Assert(r.Ok, "chain intact after multiple writes");
            Assert(r.FirstBrokenLine == -1, "no broken line when intact");
        }
        finally { if (File.Exists(p)) File.Delete(p); }
        return fails;
    }

    static int TestTamperDetected()
    {
        int fails = 0;
        string p = TempPath();
        try
        {
            var log = new AuditLog(p);
            log.Log("WriteFile", "saved report.md", "agent", "t1", "ok");
            log.Log("PublishRepo", "pushed to github", "user", "t1", "ok");
            log.Flush();
            // tamper the middle of the second record's detail
            var lines = File.ReadAllLines(p);
            lines[1] = lines[1].Replace("pushed to github", "pushed to EVIL");
            File.WriteAllLines(p, lines);
            var r = log.VerifyChain();
            Assert(!r.Ok, "tampered record detected");
            Assert(r.FirstBrokenLine == 1, "broken line index points at tampered record");
        }
        finally { if (File.Exists(p)) File.Delete(p); }
        return fails;
    }

    static int TestLegacyLinesSkipped()
    {
        int fails = 0;
        string p = TempPath();
        try
        {
            // simulate a legacy (pre-chain) line written without a hash field
            File.WriteAllText(p, "2026-01-01T00:00:00\tWriteFile\tuser\t\tok\tlegacy record without hash\n");
            var log = new AuditLog(p);
            log.Log("PublishRepo", "pushed", "user", "t1", "ok");
            log.Flush();
            var r = log.VerifyChain();
            Assert(r.Ok, "legacy line tolerated, chain still intact");
        }
        finally { if (File.Exists(p)) File.Delete(p); }
        return fails;
    }

    static void Assert(bool cond, string msg) { if (!cond) { Console.WriteLine("  FAIL: " + msg); } }
}
