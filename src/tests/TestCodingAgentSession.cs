using System;
using System.IO;
using ZhuaQianDesktopApp.Agent;

// Standalone test class for CodingAgentSession (Epic D orchestrator). RunAll()
// returns the failure count; TestRunner.Main() calls it (see
// docs/patches/EPIC_D_INTEGRATION.md). Uses a fake ICommandRecorder so the test
// never runs the (heavy) real build/test commands.
class TestCodingAgentSession
{
    static int failures = 0;

    static void Assert(bool cond, string msg)
    {
        if (!cond) { failures++; Console.WriteLine("  FAIL: " + msg); }
    }

    // Fake recorder returns a fixed result based on whether the arguments name
    // build.ps1 (build) or run-tests.ps1 (test). Never executes anything.
    sealed class FakeRecorder : ICommandRecorder
    {
        public bool BuildOk = true;
        public bool TestOk = true;

        public AgentPlanStepResult Run(string fileName, string arguments, string workingDirectory = "")
        {
            bool isBuild = (arguments ?? "").Contains("build.ps1");
            bool ok = isBuild ? BuildOk : TestOk;
            var r = new AgentPlanStepResult();
            r.Success = ok;
            r.ExitCode = ok ? 0 : 1;
            r.OutputSummary = isBuild ? "fake build output" : "fake test output";
            r.StartedAt = DateTime.Now;
            r.FinishedAt = DateTime.Now;
            return r;
        }
    }

    public static int RunAll()
    {
        failures = 0;
        Console.WriteLine("[CodingAgentSession]");

        string dir = Path.Combine(Path.GetTempPath(), "zq_coding_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            var plan = new AgentPlanParser().Parse("Goal: demo coding session\n1. Generate a TXT file");

            var session = new CodingAgentSession();
            session.RootDirectory = dir;
            session.Recorder = new FakeRecorder { BuildOk = true, TestOk = true };
            var report = session.Run(plan);

            Assert(report.Status == "passed", "fake green build+test -> status passed");
            string md = report.ToMarkdown();
            Assert(md.Contains("Coding Agent Session Report"), "report markdown produced");
            Assert(md.Contains("Plan Review"), "plan review included");
            Assert(md.Contains("Diff:"), "diff section included");
            Assert(md.Contains("### Build") && md.Contains("### Test"), "build and test sections included");

            var failing = new CodingAgentSession();
            failing.RootDirectory = dir;
            failing.Recorder = new FakeRecorder { BuildOk = false, TestOk = true };
            var failReport = failing.Run(plan);
            Assert(failReport.Status == "build-failed", "failing build -> status build-failed");

            var testFail = new CodingAgentSession();
            testFail.RootDirectory = dir;
            testFail.Recorder = new FakeRecorder { BuildOk = true, TestOk = false };
            var testFailReport = testFail.Run(plan);
            Assert(testFailReport.Status == "test-failed", "failing test -> status test-failed");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        Console.WriteLine("  [CodingAgentSession] failures=" + failures);
        return failures;
    }
}
