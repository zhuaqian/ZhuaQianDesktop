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

        Console.WriteLine("  [CommandRunRecorder] failures=" + failures);
        return failures;
    }
}
