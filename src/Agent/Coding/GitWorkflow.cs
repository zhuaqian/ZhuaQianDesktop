using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Agent;

namespace ZhuaQianDesktopApp.Agent.Coding
{
    // Result of a git operation: exit code, stdout, stderr, and success flag.
    public sealed class GitResult
    {
        public bool Success;
        public int ExitCode;
        public string Stdout = "";
        public string Stderr = "";
        public string Command = "";

        public bool Ok { get { return Success && ExitCode == 0; } }
    }

    // Lightweight git working-tree operations used by the coding loop: diff,
    // status, commit-message suggestion, branch creation, and patch export.
    // Every git invocation goes through PermissionGate (permCommandRun) so
    // git side-effects stay inside the same permission pipeline as every
    // other command. The command runner is injectable so tests can supply a
    // fake without a real git repo.
    //
    // This closes the "Git workflow is not a product path" gap: the coding
    // loop can now show a diff, suggest a commit message, and export a patch
    // without leaving the app.
    public sealed class GitWorkflow
    {
        readonly string rootDirectory;
        readonly PermissionGate permissionGate;
        readonly ICommandRecorder recorder;

        public GitWorkflow(string rootDirectory, PermissionGate permissionGate, ICommandRecorder recorder)
        {
            this.rootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? "" : Path.GetFullPath(rootDirectory);
            this.permissionGate = permissionGate ?? new PermissionGate();
            this.recorder = recorder ?? new CommandRunRecorder();
        }

        // Returns the porcelain status: a list of (status code, path) pairs.
        public List<GitStatusEntry> Status()
        {
            var entries = new List<GitStatusEntry>();
            var r = RunGit("status --porcelain");
            if (!r.Ok) return entries;
            foreach (var raw in r.Stdout.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Length < 4) continue;
                string code = line.Substring(0, 2);
                string path = line.Substring(3).Trim();
                int arrow = path.IndexOf(" -> ");
                if (arrow >= 0) path = path.Substring(arrow + 4);
                if (path.Length > 0) entries.Add(new GitStatusEntry { Code = code, Path = path.Replace('\\', '/') });
            }
            return entries;
        }

        // Returns the unified diff of the working tree (or a specific path).
        public string Diff(string pathspec = "")
        {
            string args = "diff";
            if (!string.IsNullOrWhiteSpace(pathspec)) args += " -- " + pathspec;
            var r = RunGit(args);
            return r.Ok ? r.Stdout : "";
        }

        // Returns the diff stat (files changed, insertions, deletions).
        public string DiffStat()
        {
            var r = RunGit("diff --stat");
            return r.Ok ? r.Stdout : "";
        }

        // Suggests a Conventional Commits message from the working-tree changes.
        // Infers type (feat/fix/docs/test/refactor/chore) and scope from the
        // changed paths, and builds a subject from the most significant file.
        public string SuggestCommitMessage()
        {
            var entries = Status();
            if (entries.Count == 0) return "chore: no changes to commit";

            string type = InferType(entries);
            string scope = InferScope(entries);
            string subject = InferSubject(entries);

            string header = type;
            if (!string.IsNullOrEmpty(scope)) header += "(" + scope + ")";
            header += ": " + subject;
            return header;
        }

        // Creates and checks out a new branch. Returns the git result.
        public GitResult CreateBranch(string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName)) return Failed("empty branch name");
            if (!IsValidBranchName(branchName)) return Failed("invalid branch name: " + branchName);
            return RunGit("checkout -b " + branchName);
        }

        // Exports the current working-tree diff to a patch file.
        public GitResult ExportPatch(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath)) return Failed("empty output path");
            var diffResult = RunGit("diff");
            if (!diffResult.Ok) return diffResult;
            try
            {
                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(outputPath, diffResult.Stdout);
                return new GitResult { Success = true, ExitCode = 0, Stdout = "patch written to " + outputPath, Command = "git diff > " + outputPath };
            }
            catch (Exception ex)
            {
                return Failed("write patch failed: " + ex.Message);
            }
        }

        // Stage specific files (git add). Returns the git result.
        public GitResult Add(params string[] paths)
        {
            if (paths == null || paths.Length == 0) return RunGit("add -A");
            string args = "add " + string.Join(" ", paths);
            return RunGit(args);
        }

        // Commit with a message. Returns the git result.
        public GitResult Commit(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return Failed("empty commit message");
            // Escape double quotes in the message for the -m argument.
            string escaped = message.Replace("\"", "\\\"");
            return RunGit("commit -m \"" + escaped + "\"");
        }

        GitResult RunGit(string args)
        {
            var result = new GitResult();
            result.Command = "git " + args;

            // Permission gate: every git command goes through permCommandRun.
            var decision = permissionGate.Check("permCommandRun", result.Command);
            if (decision != PermissionDecision.Allow)
            {
                result.Success = false;
                result.ExitCode = -2;
                result.Stderr = "permission denied for permCommandRun: " + result.Command;
                return result;
            }

            var stepResult = recorder.Run("git", args, rootDirectory);
            result.Success = stepResult.Success;
            result.ExitCode = stepResult.ExitCode;
            result.Stdout = stepResult.OutputSummary ?? "";
            result.Stderr = stepResult.ErrorSummary ?? "";
            return result;
        }

        static GitResult Failed(string message)
        {
            return new GitResult { Success = false, ExitCode = -1, Stderr = message };
        }

        static string InferType(List<GitStatusEntry> entries)
        {
            bool hasSrc = false, hasDocs = false, hasTests = false, hasInstaller = false, hasConfig = false;
            foreach (var e in entries)
            {
                string p = e.Path.ToLowerInvariant();
                if (p.StartsWith("docs/") || p.EndsWith(".md")) hasDocs = true;
                else if (p.Contains("test") || p.StartsWith("tests/") || p.Contains("spec")) hasTests = true;
                else if (p.StartsWith("installer/") || p.Contains("install")) hasInstaller = true;
                else if (p.EndsWith(".cs") || p.EndsWith(".js") || p.EndsWith(".ts") || p.EndsWith(".py") || p.EndsWith(".go") || p.EndsWith(".rs")) hasSrc = true;
                else if (p.EndsWith(".json") || p.EndsWith(".yml") || p.EndsWith(".yaml") || p.EndsWith(".csproj") || p.EndsWith(".sln") || p.EndsWith(".toml")) hasConfig = true;
            }
            // Priority: source changes drive feat/fix; pure docs => docs; pure tests => test.
            if (hasSrc) return "fix";   // most coding-loop changes are fixes; can be overridden
            if (hasInstaller) return "feat";
            if (hasDocs) return "docs";
            if (hasTests) return "test";
            if (hasConfig) return "chore";
            return "chore";
        }

        static string InferScope(List<GitStatusEntry> entries)
        {
            // Pick the most common top-level directory among changed paths.
            var dirCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                string dir = e.Path.Contains("/") ? e.Path.Substring(0, e.Path.IndexOf('/')) : "";
                if (string.IsNullOrEmpty(dir)) continue;
                int n;
                dirCounts.TryGetValue(dir, out n);
                dirCounts[dir] = n + 1;
            }
            string best = "";
            int bestCount = 0;
            foreach (var kv in dirCounts)
            {
                if (kv.Value > bestCount) { bestCount = kv.Value; best = kv.Key; }
            }
            return best;
        }

        static string InferSubject(List<GitStatusEntry> entries)
        {
            // Use the first changed file's base name as the subject seed, plus count.
            if (entries.Count == 1)
            {
                string name = Path.GetFileNameWithoutExtension(entries[0].Path);
                return "update " + name;
            }
            // Group by extension to summarize.
            bool allCs = true, allMd = true;
            foreach (var e in entries)
            {
                if (!e.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) allCs = false;
                if (!e.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) allMd = false;
            }
            string kind = allCs ? "source files" : allMd ? "docs" : "files";
            return "update " + entries.Count + " " + kind;
        }

        static bool IsValidBranchName(string name)
        {
            // Reject names with spaces or shell metacharacters.
            foreach (char c in name)
            {
                if (char.IsWhiteSpace(c)) return false;
                if (c == ';' || c == '&' || c == '|' || c == '`' || c == '$' || c == '(' || c == ')') return false;
            }
            if (name.StartsWith("-")) return false;
            return true;
        }

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Git Working Tree");
            sb.AppendLine();
            var entries = Status();
            sb.AppendLine("Changed files: " + entries.Count.ToString());
            if (entries.Count > 0)
            {
                int shown = 0;
                foreach (var e in entries)
                {
                    if (shown >= 30) { sb.AppendLine("  ... and " + (entries.Count - shown) + " more"); break; }
                    sb.AppendLine("  " + e.Code + " " + e.Path);
                    shown++;
                }
            }
            string msg = SuggestCommitMessage();
            sb.AppendLine();
            sb.AppendLine("Suggested commit: `" + msg + "`");
            return sb.ToString().Trim();
        }
    }

    public sealed class GitStatusEntry
    {
        public string Code = "";   // " M", "??", "A ", etc.
        public string Path = "";
        public bool IsNew { get { return Code.Trim() == "??" || Code.Contains("A"); } }
        public bool IsModified { get { return Code.Contains("M"); } }
        public bool IsDeleted { get { return Code.Contains("D"); } }
    }
}
