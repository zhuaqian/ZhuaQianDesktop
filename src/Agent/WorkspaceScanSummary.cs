using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ZhuaQianDesktopApp.Agent
{
    // Workspace scan summary (Epic D1). Lists changed files vs. the working tree,
    // reports the canonical build/test commands, and flags line-budget and
    // anti-pattern risks. Pure and side-effect free except for a read-only git
    // status call inside Capture(); it never writes files.
    public sealed class WorkspaceScanSummary
    {
        public string RootDirectory = "";
        public List<string> ChangedFiles = new List<string>();
        public int ChangedFileCount { get { return ChangedFiles.Count; } }
        public string BuildCommand = @"powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1";
        public string TestCommand = @"powershell -NoProfile -ExecutionPolicy Bypass -File .\src\scripts\run-tests.ps1";
        public List<string> RiskNotes = new List<string>();

        // Capture a summary of the working tree (changed files via git, then risk
        // assessment). Returns an empty changed-files list if git is unavailable
        // or the path is not a repo.
        public static WorkspaceScanSummary Capture(string rootDirectory)
        {
            var summary = new WorkspaceScanSummary();
            summary.RootDirectory = rootDirectory ?? "";
            summary.ChangedFiles = GitChangedFiles(rootDirectory);
            summary.RiskNotes = CollectRiskNotes(rootDirectory, summary.ChangedFiles);
            return summary;
        }

        // Pure risk assessment. Kept static and public so it can be unit-tested
        // directly with planted files, without invoking git.
        public static List<string> CollectRiskNotes(string rootDirectory, List<string> changedFiles)
        {
            var notes = new List<string>();
            if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory)) return notes;

            bool hasChangedFiles = changedFiles != null && changedFiles.Count > 0;
            string mainForm = Path.Combine(rootDirectory, "ZhuaQianDesktop.cs");
            if (hasChangedFiles && File.Exists(mainForm))
            {
                int lines = CountLines(mainForm);
                if (lines > 3895)
                    notes.Add(string.Format("Main form {0} is {1} lines (> 3895 budget).", Path.GetFileName(mainForm), lines));
                else
                    notes.Add(string.Format("Main form {0} = {1} lines (within 3895 budget).", Path.GetFileName(mainForm), lines));
            }

            foreach (var rel in changedFiles ?? new List<string>())
            {
                string full = Path.Combine(rootDirectory, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!full.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) continue;
                int lines = CountLines(full);
                if (lines > 900)
                    notes.Add(string.Format("Changed file {0} is {1} lines (> 900 budget).", rel, lines));
            }

            foreach (var rel in changedFiles ?? new List<string>())
            {
                if (rel.IndexOf("src" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!rel.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                string full = Path.Combine(rootDirectory, rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full) && FileContains(full, "new Tools."))
                    notes.Add(string.Format("Changed file {0} still constructs Tools.* directly (Epic B anti-pattern).", rel));
            }

            if (notes.Count == 0)
                notes.Add("No line-budget or anti-pattern risks detected in changed files.");
            return notes;
        }

        static List<string> GitChangedFiles(string root)
        {
            var files = new List<string>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return files;
            try
            {
                var psi = new ProcessStartInfo("git", "status --porcelain")
                {
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return files;
                    string outp = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    foreach (var raw in outp.Split('\n'))
                    {
                        var line = raw.Trim();
                        if (line.Length < 4) continue;
                        string path = line.Substring(3).Trim();
                        int arrow = path.IndexOf(" -> ");
                        if (arrow >= 0) path = path.Substring(arrow + 4);
                        if (path.Length > 0) files.Add(path.Replace('\\', '/'));
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("WorkspaceScanSummary git status: " + ex.Message); }
            return files;
        }

        static int CountLines(string path)
        {
            int n = 0;
            try { using (var r = new StreamReader(path)) { while (r.ReadLine() != null) n++; } }
            catch (Exception ex) { Debug.WriteLine("WorkspaceScanSummary count lines: " + ex.Message); }
            return n;
        }

        static bool FileContains(string path, string needle)
        {
            try
            {
                using (var r = new StreamReader(path))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                        if (line.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            catch (Exception ex) { Debug.WriteLine("WorkspaceScanSummary file contains: " + ex.Message); }
            return false;
        }

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Workspace Scan Summary");
            sb.AppendLine();
            sb.AppendLine("Root: " + (string.IsNullOrWhiteSpace(RootDirectory) ? "(unknown)" : RootDirectory));
            sb.AppendLine("Changed files: " + ChangedFileCount.ToString());
            if (ChangedFileCount > 0)
            {
                int shown = 0;
                foreach (var f in ChangedFiles)
                {
                    if (shown >= 20) { sb.AppendLine("  ... and " + (ChangedFileCount - shown).ToString() + " more"); break; }
                    sb.AppendLine("  - " + f);
                    shown++;
                }
            }
            sb.AppendLine();
            sb.AppendLine("Build command: `" + BuildCommand + "`");
            sb.AppendLine("Test command: `" + TestCommand + "`");
            sb.AppendLine();
            sb.AppendLine("Risk notes:");
            foreach (var note in RiskNotes) sb.AppendLine("  - " + note);
            return sb.ToString().Trim();
        }
    }
}
