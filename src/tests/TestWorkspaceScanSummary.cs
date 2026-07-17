using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using ZhuaQianDesktopApp.Agent;

// Standalone test class for WorkspaceScanSummary (Epic D1). Exposes RunAll()
// returning a failure count; TestRunner.Main() calls it (see
// docs/patches/EPIC_D_INTEGRATION.md). No entry point here, so it compiles
// cleanly alongside TestRunner.cs.
class TestWorkspaceScanSummary
{
    static int failures = 0;

    static void Assert(bool cond, string msg)
    {
        if (!cond) { failures++; Console.WriteLine("  FAIL: " + msg); }
    }

    public static int RunAll()
    {
        failures = 0;
        Console.WriteLine("[WorkspaceScanSummary]");

        // Pure risk assessment with planted files (no git needed)
        string dir = Path.Combine(Path.GetTempPath(), "zq_scan_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            string mainForm = Path.Combine(dir, "ZhuaQianDesktop.cs");
            File.WriteAllText(mainForm, "namespace x { class y { } }"); // within budget

            string big = Path.Combine(dir, "src", "Big.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(big));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 950; i++) sb.AppendLine("// line " + i.ToString());
            File.WriteAllText(big, sb.ToString());

            var changed = new List<string> { "src/Big.cs" };
            var notes = WorkspaceScanSummary.CollectRiskNotes(dir, changed);
            bool sawBig = false;
            foreach (var n in notes) if (n.Contains("src/Big.cs") && n.Contains("> 900")) sawBig = true;
            Assert(sawBig, "oversized changed .cs flagged as > 900 lines");

            bool sawMain = false;
            foreach (var n in notes) if (n.Contains("Main form") && n.Contains("within 3895")) sawMain = true;
            Assert(sawMain, "main form within budget is noted");

            var clean = WorkspaceScanSummary.CollectRiskNotes(dir, new List<string>());
            Assert(clean.Count == 1 && clean[0].Contains("No line-budget"), "clean changes produce a single no-risk note");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        // Capture() against a planted git repo (best-effort; skipped if git absent)
        string repo = Path.Combine(Path.GetTempPath(), "zq_repo_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(repo);
        try
        {
            if (RunGit(repo, "init") == 0
                && RunGit(repo, "config user.email t@t.com") == 0
                && RunGit(repo, "config user.name t") == 0)
            {
                string f = Path.Combine(repo, "a.txt");
                File.WriteAllText(f, "v1");
                if (RunGit(repo, "add -A") == 0 && RunGit(repo, "commit -q -m init") == 0)
                {
                    File.WriteAllText(f, "v2");
                    var summary = WorkspaceScanSummary.Capture(repo);
                    Assert(summary.ChangedFileCount >= 1, "git change detected by Capture");
                    Assert(summary.ToMarkdown().Contains("Workspace Scan Summary"), "markdown report produced");
                }
            }
        }
        catch { }
        finally { try { Directory.Delete(repo, true); } catch { } }

        Console.WriteLine("  [WorkspaceScanSummary] failures=" + failures);
        return failures;
    }

    static int RunGit(string repo, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = repo,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) return -1;
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode;
            }
        }
        catch { return -1; }
    }
}
