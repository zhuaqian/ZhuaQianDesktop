using System;
using System.Collections.Generic;
using System.IO;
using ZhuaQianDesktopApp.Agent.Coding;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp.Agent
{
    // Command entry for the coding-agent closed loop (CommandType=DiagnoseFix).
    // Drives FixLoopRunner (Epic D4), which applies patches and runs git THROUGH
    // the single AgentPipeline (Command -> PermissionGate -> Executor ->
    // AuditLog), so every file write and git action is audited and user-approved.
    // The "decide the fix" step uses DiagnoseFixFixStrategy (safe, deterministic
    // CS1002/CS0246/CS0103 fixes that reuse the production RuleBasedFixStrategy).
    // A manual unified diff may be supplied via the `patch` parameter to drive a
    // real fix through the loop (round-trip from the Diagnose & Fix dialog).
    public sealed class DiagnoseFixExecutor : ICommandExecutor
    {
        readonly AgentPipeline pipeline;
        readonly string projectRoot;

        public DiagnoseFixExecutor(AgentPipeline pipeline, string projectRoot)
        {
            this.pipeline = pipeline;
            this.projectRoot = string.IsNullOrWhiteSpace(projectRoot)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(projectRoot);
        }

        public string CommandType { get { return "DiagnoseFix"; } }

        public CommandResult Execute(IAgentCommand command)
        {
            string root = GetString(command, "root");
            if (string.IsNullOrWhiteSpace(root)) root = command.Target;
            if (string.IsNullOrWhiteSpace(root)) root = projectRoot;
            if (string.IsNullOrWhiteSpace(root))
                return CommandResult.Failed("DiagnoseFix requires a root directory (Target or root param)");
            if (!Directory.Exists(root))
                return CommandResult.Failed("DiagnoseFix root directory does not exist: " + root);

            // Manual unified diff supplied from the Diagnose & Fix dialog: apply it
            // through the pipeline (PatchFile command), then re-run the loop to verify.
            string patchText = GetString(command, "patch");
            if (!string.IsNullOrWhiteSpace(patchText))
            {
                var ops = ParseUnifiedDiffToOps(patchText);
                foreach (var op in ops)
                {
                    var patchParams = new Dictionary<string, object>
                    {
                        { "op", op.Op },
                        { "oldText", op.OldText ?? "" },
                        { "newText", op.NewText ?? "" }
                    };
                    var patchCmd = new AgentCommand("PatchFile", "permPatchFile",
                        command.TaskId ?? "diagnose-fix", op.Target, "apply manual patch", patchParams);
                    var pr = pipeline.Run(patchCmd);
                    if (pr == null || pr.Status != CommandStatus.Success)
                        return CommandResult.Failed("manual patch failed on " + op.Target + ": " +
                            (pr == null ? "no result" : pr.ErrorMessage));
                }
            }

            // Analyze the project to auto-detect the build/test command.
            var analyzer = new ProjectAnalyzer();
            var profile = analyzer.Analyze(root);

            string buildCommand = NormalizeCommand(GetString(command, "buildCommand"));
            if (string.IsNullOrWhiteSpace(buildCommand)) buildCommand = NormalizeCommand(profile.BuildCommand);
            string testCommand = NormalizeCommand(GetString(command, "testCommand"));
            if (string.IsNullOrWhiteSpace(testCommand)) testCommand = NormalizeCommand(profile.TestCommand);

            // Allow the detected build/test executables so the loop can actually
            // run them on the target repo (dotnet, npm, cargo, ...) instead of
            // being denied by the default PermissionGate. Within-root only.
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddDetectedProgram(allowed, profile.BuildCommand);
            AddDetectedProgram(allowed, profile.TestCommand);

            int maxIt;
            if (!int.TryParse(GetString(command, "maxIterations"), out maxIt) || maxIt <= 0) maxIt = 3;

            var recorder = new GuardedCommandRunRecorder(root, null, null, allowed);
            var strategy = new DiagnoseFixFixStrategy(profile);
            var fixLoop = new FixLoopRunner(pipeline, root, recorder, strategy, maxIt);

            var report = fixLoop.Run(buildCommand, testCommand);
            string markdown = report.ToMarkdown();
            if (!string.IsNullOrWhiteSpace(patchText))
                markdown = "# Manual patch applied\n\n" + markdown;
            return CommandResult.Ok(null, false, null, "report", 0, markdown);
        }

        // FixLoopRunner wraps the command as `powershell ... -Command <cmd>`, so
        // strip any leading `powershell -File/-Command` wrapper from the detected
        // build command and keep only the inner expression (e.g. `.\build.ps1`).
        static string NormalizeCommand(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string lower = raw.ToLowerInvariant();
            if (lower.StartsWith("powershell") || lower.StartsWith("pwsh"))
            {
                int fileIdx = lower.IndexOf("-file");
                if (fileIdx >= 0)
                {
                    string after = raw.Substring(fileIdx + 5).Trim();
                    int sp = after.IndexOf(' ');
                    return sp > 0 ? after.Substring(0, sp) : after;
                }
                int cmdIdx = lower.IndexOf("-command");
                if (cmdIdx >= 0) return raw.Substring(cmdIdx + 8).Trim();
            }
            return raw;
        }

        // Convert a unified diff (from the report dialog) into PatchOp edit
        // operations. Removed lines (-) become OldText; added lines (+) become
        // NewText; context lines (space) are kept on both sides. PatchExecutor
        // then replaces that exact block in the file. Single-hunk diffs (the
        // format produced by this app's report) apply cleanly; if the block is
        // not found verbatim, PatchExecutor returns a clear error.
        static List<FixLoopRunner.PatchOp> ParseUnifiedDiffToOps(string diffText)
        {
            var ops = new List<FixLoopRunner.PatchOp>();
            string[] lines = diffText.Replace("\r\n", "\n").Split('\n');
            string currentFile = "";
            var removed = new List<string>();
            var added = new List<string>();
            bool inHunk = false;

            void Flush()
            {
                if (string.IsNullOrEmpty(currentFile)) return;
                if (removed.Count == 0 && added.Count == 0) return;
                ops.Add(new FixLoopRunner.PatchOp
                {
                    Op = "edit",
                    Target = currentFile,
                    OldText = string.Join("\n", removed),
                    NewText = string.Join("\n", added)
                });
            }

            foreach (var raw in lines)
            {
                if (raw.StartsWith("--- "))
                {
                    Flush();
                    removed.Clear();
                    added.Clear();
                    currentFile = StripPath(raw.Substring(4));
                    inHunk = false;
                    continue;
                }
                if (raw.StartsWith("+++ "))
                {
                    if (string.IsNullOrEmpty(currentFile)) currentFile = StripPath(raw.Substring(4));
                    inHunk = false;
                    continue;
                }
                if (raw.StartsWith("@@"))
                {
                    inHunk = true;
                    continue;
                }
                if (!inHunk) continue;
                if (raw.StartsWith("-")) removed.Add(raw.Substring(1));
                else if (raw.StartsWith("+")) added.Add(raw.Substring(1));
                else if (raw.StartsWith(" ")) { string c = raw.Substring(1); removed.Add(c); added.Add(c); }
            }
            Flush();
            return ops;
        }

        static string StripPath(string p)
        {
            p = p.Trim();
            if (p.StartsWith("a/") || p.StartsWith("b/")) p = p.Substring(2);
            return p;
        }

        // Extract the executable name from a detected command (e.g. "dotnet build"
        // -> "dotnet", "powershell -File .\build.ps1" -> "powershell") so it can
        // be whitelisted in GuardedCommandRunRecorder.
        static void AddDetectedProgram(HashSet<string> set, string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return;
            string s = cmd.Trim();
            int sp = s.IndexOf(' ');
            string prog = sp > 0 ? s.Substring(0, sp) : s;
            prog = prog.Trim().ToLowerInvariant();
            if (prog.EndsWith(".exe")) prog = prog.Substring(0, prog.Length - 4);
            set.Add(prog);
        }

        static string GetString(IAgentCommand command, string key)
        {
            if (command.Parameters == null) return "";
            object v;
            if (command.Parameters.TryGetValue(key, out v) && v != null) return v.ToString();
            return "";
        }

        // Safe, deterministic fix strategy: maps FixLoopRunner.BuildError (parsed
        // from build output) onto Coding.BuildError and delegates to the
        // production RuleBasedFixStrategy, then converts its CodePatch results
        // into PatchOp edits that FixLoopRunner applies via the pipeline.
        internal sealed class DiagnoseFixFixStrategy : FixLoopRunner.IFixStrategy
        {
            readonly ProjectProfile profile;
            readonly RuleBasedFixStrategy rule = new RuleBasedFixStrategy();
            readonly ModelFixStrategy model = new ModelFixStrategy();

            public DiagnoseFixFixStrategy(ProjectProfile profile) { this.profile = profile; }

            public List<FixLoopRunner.PatchOp> Propose(string root, AgentPlanStepResult failedStep, List<FixLoopRunner.BuildError> errors)
            {
                var ops = new List<FixLoopRunner.PatchOp>();
                if (errors == null) return ops;
                var mapped = new List<Coding.BuildError>();
                foreach (var e in errors)
                {
                    var m = new Coding.BuildError();
                    m.File = e.File ?? "";
                    m.Line = e.Line.HasValue ? e.Line.Value : 0;
                    m.Message = e.Message ?? "";
                    m.Severity = ErrorSeverity.Error;
                    if (!string.IsNullOrEmpty(e.Message))
                    {
                        int colon = e.Message.IndexOf(':');
                        m.Code = colon > 0 ? e.Message.Substring(0, colon).Trim() : e.Message.Trim();
                    }
                    mapped.Add(m);
                }
                var patches = rule.SuggestFixes(mapped, profile);
                if (patches != null)
                {
                    foreach (var p in patches)
                    {
                        ops.Add(new FixLoopRunner.PatchOp
                        {
                            Op = "edit",
                            Target = p.FilePath,
                            OldText = p.OldContent ?? "",
                            NewText = p.NewContent ?? ""
                        });
                    }
                }
                // Deterministic strategy found nothing safe -> ask the model for edits.
                if (ops.Count == 0)
                {
                    var modelOps = model.Propose(root, failedStep, errors);
                    if (modelOps != null) ops.AddRange(modelOps);
                }
                return ops;
            }
        }
    }
}
