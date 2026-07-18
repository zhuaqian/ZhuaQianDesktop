using System;
using System.IO;
using ZhuaQianDesktopApp.Agent;

// Standalone test class for CommandRunRecorder (Epic D2). RunAll() returns the
// failure count; TestRunner.Main() calls it (see docs/patches/EPIC_D_INTEGRATION.md).
class TestCommandRunRecorder
{
    static int failures = 0;

    static void Assert(bool cond, string msg)
    {
        if (!cond) { failures++; Console.WriteLine("  FAIL: " + msg); }
    }

    public static int RunAll()
    {
        failures = 0;
        Console.WriteLine("[CommandRunRecorder]");

        var recorder = new CommandRunRecorder();

        // Lightweight, deterministic command that does not depend on build.ps1.
        var ok = recorder.Run("powershell", "-NoProfile -Command \"Write-Output hello\"", "");
        Assert(ok.ExitCode == 0, "powershell echo exits 0");
        Assert(ok.Success, "powershell echo is reported successful");
        Assert((ok.OutputSummary ?? "").Contains("hello"), "stdout captured into OutputSummary");

        // Failing command surfaces a non-zero exit code and Success=false.
        var bad = recorder.Run("powershell", "-NoProfile -Command \"exit 3\"", "");
        Assert(!bad.Success, "non-zero exit is not success");
        Assert(bad.ExitCode == 3, "exit code captured into result");

        // Missing executable is caught, not thrown.
        var missing = recorder.Run("this-exe-does-not-exist-xyz", "", "");
        Assert(!missing.Success, "missing executable is reported as failed");
        Assert(missing.ExitCode == -1, "missing executable yields exit code -1");

        string dir = Path.Combine(Path.GetTempPath(), "zq_guarded_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(Path.Combine(dir, "src", "scripts"));
        try
        {
            var guarded = new GuardedCommandRunRecorder(dir, null, new FakeInnerRecorder());
            var allowed = guarded.Run("powershell", "-NoProfile -ExecutionPolicy Bypass -File .\\build.ps1", dir);
            Assert(allowed.Success, "guarded recorder allows known project build script");

            var denied = guarded.Run("cmd.exe", "/c echo nope", dir);
            Assert(!denied.Success, "guarded recorder denies arbitrary command by default");
            Assert(denied.ExitCode == -2, "guarded denial uses explicit exit code");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        Console.WriteLine("  [CommandRunRecorder] failures=" + failures);
        return failures;
    }

    sealed class FakeInnerRecorder : ICommandRecorder
    {
        public AgentPlanStepResult Run(string fileName, string arguments, string workingDirectory = "")
        {
            var r = new AgentPlanStepResult();
            r.Success = true;
            r.ExitCode = 0;
            r.OutputSummary = "fake guarded output";
            r.StartedAt = DateTime.Now;
            r.FinishedAt = DateTime.Now;
            return r;
        }
    }
}
