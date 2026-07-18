using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Agent;

namespace ZhuaQianDesktopApp.Agent.Coding
{
    // Strategy interface: given the parsed build/test errors and the project
    // profile, suggest zero or more CodePatch fixes. The real fix engine will
    // be model-driven (a future ModelFixStrategy); for now a deterministic
    // RuleBasedFixStrategy handles common, safe fixes so the loop is
    // demonstrable end-to-end without a model.
    public interface IFixStrategy
    {
        string Name { get; }
        List<CodePatch> SuggestFixes(List<BuildError> errors, ProjectProfile profile);
    }

    // Deterministic, conservative fix strategy. Only applies fixes that are
    // guaranteed safe (no semantic guesswork): CS1002 missing semicolon, and
    // trivial missing-using for well-known namespaces. Every other error is
    // left unfixed so the loop reports "cannot fix" honestly rather than
    // applying a risky patch. This keeps the loop safe to run unattended.
    public sealed class RuleBasedFixStrategy : IFixStrategy
    {
        public string Name { get { return "rule-based"; } }

        static readonly Dictionary<string, string> KnownNamespaces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "File", "System.IO" },
            { "Directory", "System.IO" },
            { "Path", "System.IO" },
            { "Stream", "System.IO" },
            { "StreamReader", "System.IO" },
            { "StreamWriter", "System.IO" },
            { "Task", "System.Threading.Tasks" },
            { "List", "System.Collections.Generic" },
            { "Dictionary", "System.Collections.Generic" },
            { "HashSet", "System.Collections.Generic" },
            { "Regex", "System.Text.RegularExpressions" },
            { "StringBuilder", "System.Text" },
            { "DateTime", "System" },
            { "Guid", "System" },
            { "Math", "System" },
            { "Convert", "System" },
            { "Debug", "System.Diagnostics" },
            { "Process", "System.Diagnostics" },
            { "JavaScriptSerializer", "System.Web.Script.Serialization" }
        };

        public List<CodePatch> SuggestFixes(List<BuildError> errors, ProjectProfile profile)
        {
            var patches = new List<CodePatch>();
            if (errors == null) return patches;

            var usingPatchesByFile = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var err in errors)
            {
                if (err.Severity != ErrorSeverity.Error) continue;

                // CS1002: missing semicolon. Safe fix: if the reported line does
                // not end with ';' (ignoring trailing whitespace/comments), append one.
                if (err.Code == "CS1002" && err.HasLocation)
                {
                    var patch = TryFixMissingSemicolon(err, profile);
                    if (patch != null) patches.Add(patch);
                    continue;
                }

                // CS0246: type not found. Conservative: only add a using for
                // well-known framework types.
                if (err.Code == "CS0246" && err.HasLocation && profile.Language == ProjectLanguage.CSharp)
                {
                    string typeName = ExtractTypeName(err.Message);
                    if (!string.IsNullOrEmpty(typeName) && KnownNamespaces.ContainsKey(typeName))
                    {
                        string ns = KnownNamespaces[typeName];
                        if (!usingPatchesByFile.ContainsKey(err.File))
                            usingPatchesByFile[err.File] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (usingPatchesByFile[err.File].Add(ns))
                        {
                            var patch = TryAddUsing(err.File, ns, profile);
                            if (patch != null) patches.Add(patch);
                        }
                    }
                    continue;
                }

                // CS0103: name does not exist in current context. If it matches a
                // known type, same using fix applies.
                if (err.Code == "CS0103" && err.HasLocation && profile.Language == ProjectLanguage.CSharp)
                {
                    string typeName = ExtractCs0103Name(err.Message);
                    if (!string.IsNullOrEmpty(typeName) && KnownNamespaces.ContainsKey(typeName))
                    {
                        string ns = KnownNamespaces[typeName];
                        if (!usingPatchesByFile.ContainsKey(err.File))
                            usingPatchesByFile[err.File] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (usingPatchesByFile[err.File].Add(ns))
                        {
                            var patch = TryAddUsing(err.File, ns, profile);
                            if (patch != null) patches.Add(patch);
                        }
                    }
                    continue;
                }
            }
            return patches;
        }

        CodePatch TryFixMissingSemicolon(BuildError err, ProjectProfile profile)
        {
            string fullPath = ResolvePath(err.File, profile);
            if (!File.Exists(fullPath)) return null;
            try
            {
                string[] lines = File.ReadAllLines(fullPath);
                if (err.Line < 1 || err.Line > lines.Length) return null;
                string line = lines[err.Line - 1];
                string trimmed = StripTrailingComment(line).TrimEnd();
                if (trimmed.EndsWith(";")) return null;     // already has semicolon
                if (trimmed.EndsWith("{") || trimmed.EndsWith("}")) return null; // not a statement
                if (trimmed.Length == 0) return null;

                // Insert a semicolon at the end of the code (before any trailing comment).
                string newLine = InsertSemicolonBeforeComment(line);
                lines[err.Line - 1] = newLine;
                var patch = new CodePatch();
                patch.Operation = PatchOperation.Modify;
                patch.FilePath = Relativize(fullPath, profile);
                patch.NewContent = string.Join("\n", lines) + "\n";
                patch.OldContent = File.ReadAllText(fullPath);
                patch.Reason = "CS1002: add missing semicolon at line " + err.Line;
                patch.SourceError = err.ToString();
                return patch;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("RuleBasedFix CS1002: " + ex.Message); return null; }
        }

        CodePatch TryAddUsing(string relFile, string ns, ProjectProfile profile)
        {
            string fullPath = ResolvePath(relFile, profile);
            if (!File.Exists(fullPath)) return null;
            try
            {
                string content = File.ReadAllText(fullPath);
                if (content.Contains("using " + ns + ";")) return null; // already present

                // Insert after the last existing using, or after the namespace
                // opening if no usings exist, or at the top as a last resort.
                string[] lines = content.Replace("\r\n", "\n").Split('\n');
                int insertAt = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith("using ", StringComparison.OrdinalIgnoreCase))
                        insertAt = i + 1;
                }
                if (insertAt < 0)
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Contains("namespace "))
                        {
                            insertAt = i;
                            break;
                        }
                    }
                }
                if (insertAt < 0) insertAt = 0;

                var newLines = new List<string>(lines);
                newLines.Insert(insertAt, "using " + ns + ";");
                var patch = new CodePatch();
                patch.Operation = PatchOperation.Modify;
                patch.FilePath = Relativize(fullPath, profile);
                patch.NewContent = string.Join("\n", newLines);
                patch.OldContent = content;
                patch.Reason = "CS0246/CS0103: add missing using " + ns;
                patch.SourceError = "add using " + ns;
                return patch;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("RuleBasedFix using: " + ex.Message); return null; }
        }

        static string ExtractTypeName(string message)
        {
            // "The type or namespace name 'File' could not be found"
            int s = message.IndexOf('\'');
            if (s < 0) return "";
            int e = message.IndexOf('\'', s + 1);
            if (e < 0) return "";
            return message.Substring(s + 1, e - s - 1);
        }

        static string ExtractCs0103Name(string message)
        {
            // "The name 'File' does not exist in the current context"
            int s = message.IndexOf('\'');
            if (s < 0) return "";
            int e = message.IndexOf('\'', s + 1);
            if (e < 0) return "";
            return message.Substring(s + 1, e - s - 1);
        }

        static string StripTrailingComment(string line)
        {
            int idx = line.IndexOf("//");
            if (idx >= 0) return line.Substring(0, idx);
            return line;
        }

        static string InsertSemicolonBeforeComment(string line)
        {
            int commentIdx = line.IndexOf("//");
            if (commentIdx >= 0)
            {
                string code = line.Substring(0, commentIdx).TrimEnd();
                return code + ";" + line.Substring(commentIdx);
            }
            return line.TrimEnd() + ";";
        }

        static string ResolvePath(string relFile, ProjectProfile profile)
        {
            if (Path.IsPathRooted(relFile)) return relFile;
            return Path.Combine(profile.RootDirectory, relFile.Replace('/', Path.DirectorySeparatorChar));
        }

        static string Relativize(string fullPath, ProjectProfile profile)
        {
            if (string.IsNullOrEmpty(profile.RootDirectory)) return fullPath;
            string root = profile.RootDirectory.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(root.Length).Replace('\\', '/');
            return fullPath;
        }
    }

    public sealed class BuildFixLoopOptions
    {
        public string RootDirectory = "";
        public string BuildCommand = "";   // overrides profile if set
        public string TestCommand = "";
        public int MaxIterations = 3;
        public int TimeoutMs = 120000;
        public bool RunTests = true;
    }

    public enum BuildFixLoopStatus
    {
        NotStarted,
        Passed,
        BuildFailed,
        TestFailed,
        CannotFix,
        Exhausted,
        Denied
    }

    // Report from one BuildFixLoop run. Captures every iteration's build/test
    // results, every applied patch (with diff), the parsed errors, and the
    // final status. This is the audit trail that makes the loop reviewable.
    public sealed class BuildFixLoopReport
    {
        public ProjectProfile Profile;
        public BuildFixLoopStatus Status = BuildFixLoopStatus.NotStarted;
        public readonly List<AgentPlanStepResult> BuildResults = new List<AgentPlanStepResult>();
        public readonly List<AgentPlanStepResult> TestResults = new List<AgentPlanStepResult>();
        public readonly List<PatchResult> AppliedPatches = new List<PatchResult>();
        public readonly List<List<BuildError>> ParsedErrorsPerIteration = new List<List<BuildError>>();
        public int IterationsRun;
        public string StopReason = "";
        public DateTime StartedAt = DateTime.Now;
        public DateTime FinishedAt;

        public bool Succeeded { get { return Status == BuildFixLoopStatus.Passed; } }

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Build-Fix Loop Report");
            sb.AppendLine();
            sb.AppendLine("Status: **" + Status + "**");
            sb.AppendLine("Iterations: " + IterationsRun.ToString());
            if (!string.IsNullOrEmpty(StopReason)) sb.AppendLine("Stop reason: " + StopReason);
            sb.AppendLine();

            if (Profile != null)
            {
                sb.AppendLine("Build command: `" + Profile.BuildCommand + "`");
                sb.AppendLine("Test command: `" + Profile.TestCommand + "`");
                sb.AppendLine();
            }

            for (int i = 0; i < BuildResults.Count; i++)
            {
                sb.AppendLine("### Iteration " + (i + 1).ToString());
                var br = BuildResults[i];
                sb.AppendLine("Build: exit " + br.ExitCode.ToString() + " " + (br.Success ? "PASS" : "FAIL"));
                if (!string.IsNullOrEmpty(br.ErrorSummary)) sb.AppendLine("```\n" + Truncate(br.ErrorSummary, 800) + "\n```");
                if (i < TestResults.Count)
                {
                    var tr = TestResults[i];
                    sb.AppendLine("Test: exit " + tr.ExitCode.ToString() + " " + (tr.Success ? "PASS" : "FAIL"));
                    if (!string.IsNullOrEmpty(tr.ErrorSummary)) sb.AppendLine("```\n" + Truncate(tr.ErrorSummary, 800) + "\n```");
                }
                if (i < ParsedErrorsPerIteration.Count && ParsedErrorsPerIteration[i].Count > 0)
                {
                    sb.AppendLine("Parsed errors: " + ParsedErrorsPerIteration[i].Count.ToString());
                }
                sb.AppendLine();
            }

            if (AppliedPatches.Count > 0)
            {
                sb.AppendLine("### Applied Patches (" + AppliedPatches.Count.ToString() + ")");
                foreach (var p in AppliedPatches)
                {
                    sb.AppendLine("- " + p.Operation + " " + p.FilePath + (p.Success ? "" : " (FAILED: " + p.ErrorMessage + ")"));
                    if (!string.IsNullOrEmpty(p.DiffText))
                        sb.AppendLine("```diff\n" + Truncate(p.DiffText, 600) + "\n```");
                }
            }
            return sb.ToString().Trim();
        }

        static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length > max ? text.Substring(0, max) + "\n... (truncated)" : text;
        }
    }

    // The command-execution closed loop. Runs build (then test), parses errors,
    // asks the fix strategy for patches, applies them, and re-runs — up to
    // MaxIterations. Stops early when: build+test pass, the strategy returns
    // no patches (cannot fix), or the same errors recur two iterations in a row
    // (non-convergent). Every step is recorded in the report for audit.
    //
    // This closes the single biggest gap vs Codex / Claude Code: ZhuaQian can
    // now "run build -> read failure -> fix code -> re-run" autonomously.
    public sealed class BuildFixLoop
    {
        readonly ProjectAnalyzer analyzer = new ProjectAnalyzer();
        readonly ErrorParser errorParser = new ErrorParser();
        readonly CodePatcher patcher;
        readonly ICommandRecorder recorder;
        readonly IFixStrategy strategy;

        public BuildFixLoop(string rootDirectory, PermissionGate permissionGate, ICommandRecorder recorder, IFixStrategy strategy)
        {
            this.patcher = new CodePatcher(rootDirectory, permissionGate);
            this.recorder = recorder;
            this.strategy = strategy ?? new RuleBasedFixStrategy();
        }

        public BuildFixLoopReport Run(BuildFixLoopOptions options)
        {
            var report = new BuildFixLoopReport();
            report.StartedAt = DateTime.Now;
            if (options == null) options = new BuildFixLoopOptions();

            report.Profile = analyzer.Analyze(options.RootDirectory);
            string buildCmd = string.IsNullOrWhiteSpace(options.BuildCommand) ? report.Profile.BuildCommand : options.BuildCommand;
            string testCmd = string.IsNullOrWhiteSpace(options.TestCommand) ? report.Profile.TestCommand : options.TestCommand;

            if (string.IsNullOrWhiteSpace(buildCmd))
            {
                report.Status = BuildFixLoopStatus.CannotFix;
                report.StopReason = "no build command detected";
                report.FinishedAt = DateTime.Now;
                return report;
            }

            string prevErrorSignature = null;

            for (int iter = 0; iter < options.MaxIterations; iter++)
            {
                report.IterationsRun = iter + 1;

                // 1) Run build.
                var buildResult = RunCommand(buildCmd, options.RootDirectory);
                report.BuildResults.Add(buildResult);

                List<BuildError> errors = new List<BuildError>();
                if (!buildResult.Success)
                {
                    errors = errorParser.Parse(buildResult.OutputSummary, buildResult.ErrorSummary, report.Profile.Language.ToString());
                    report.ParsedErrorsPerIteration.Add(errors);

                    // Convergence check: same error signature as last iteration => stop.
                    string sig = ErrorSignature(errors);
                    if (sig != null && sig == prevErrorSignature)
                    {
                        report.Status = BuildFixLoopStatus.CannotFix;
                        report.StopReason = "non-convergent: same errors as previous iteration";
                        break;
                    }
                    prevErrorSignature = sig;

                    // Ask strategy for fixes.
                    var patches = strategy.SuggestFixes(errors, report.Profile);
                    if (patches == null || patches.Count == 0)
                    {
                        report.Status = BuildFixLoopStatus.CannotFix;
                        report.StopReason = "fix strategy returned no patches for " + errors.Count + " error(s)";
                        break;
                    }

                    // Apply patches.
                    var patchResults = patcher.ApplyAll(patches);
                    report.AppliedPatches.AddRange(patchResults);
                    bool anyFailed = false;
                    foreach (var pr in patchResults) if (!pr.Success) anyFailed = true;
                    if (anyFailed)
                    {
                        report.Status = BuildFixLoopStatus.BuildFailed;
                        report.StopReason = "patch application failed";
                        break;
                    }
                    continue; // re-run build next iteration
                }

                report.ParsedErrorsPerIteration.Add(new List<BuildError>());

                // 2) Build passed — run tests if configured.
                if (options.RunTests && !string.IsNullOrWhiteSpace(testCmd))
                {
                    var testResult = RunCommand(testCmd, options.RootDirectory);
                    report.TestResults.Add(testResult);
                    if (!testResult.Success)
                    {
                        errors = errorParser.Parse(testResult.OutputSummary, testResult.ErrorSummary, report.Profile.Language.ToString());
                        report.ParsedErrorsPerIteration[report.ParsedErrorsPerIteration.Count - 1] = errors;

                        string sig = ErrorSignature(errors);
                        if (sig != null && sig == prevErrorSignature)
                        {
                            report.Status = BuildFixLoopStatus.TestFailed;
                            report.StopReason = "non-convergent test failures";
                            break;
                        }
                        prevErrorSignature = sig;

                        var patches = strategy.SuggestFixes(errors, report.Profile);
                        if (patches == null || patches.Count == 0)
                        {
                            report.Status = BuildFixLoopStatus.TestFailed;
                            report.StopReason = "no fix for test failures";
                            break;
                        }
                        var patchResults = patcher.ApplyAll(patches);
                        report.AppliedPatches.AddRange(patchResults);
                        continue; // re-run build+test next iteration
                    }
                }

                // 3) Build + test passed.
                report.Status = BuildFixLoopStatus.Passed;
                report.StopReason = "build and test passed";
                break;
            }

            if (report.Status == BuildFixLoopStatus.NotStarted)
            {
                report.Status = BuildFixLoopStatus.Exhausted;
                report.StopReason = "reached MaxIterations without passing";
            }

            report.FinishedAt = DateTime.Now;
            return report;
        }

        AgentPlanStepResult RunCommand(string command, string workingDirectory)
        {
            // Split "powershell -NoProfile ... -File .\build.ps1" into exe + args.
            string exe, args;
            SplitCommand(command, out exe, out args);
            if (string.IsNullOrWhiteSpace(exe))
                return FailedResult("empty command");
            return recorder.Run(exe, args, workingDirectory);
        }

        static void SplitCommand(string command, out string exe, out string args)
        {
            exe = "";
            args = "";
            if (string.IsNullOrWhiteSpace(command)) return;
            command = command.Trim();
            if (command[0] == '"' || command[0] == '\'')
            {
                char quote = command[0];
                int end = command.IndexOf(quote, 1);
                if (end > 0)
                {
                    exe = command.Substring(1, end - 1);
                    args = command.Substring(end + 1).Trim();
                    return;
                }
            }
            int space = command.IndexOf(' ');
            if (space < 0) { exe = command; return; }
            exe = command.Substring(0, space);
            args = command.Substring(space + 1).Trim();
        }

        static AgentPlanStepResult FailedResult(string message)
        {
            var r = new AgentPlanStepResult();
            r.Success = false;
            r.ExitCode = -1;
            r.ErrorSummary = message;
            r.StartedAt = DateTime.Now;
            r.FinishedAt = DateTime.Now;
            return r;
        }

        static string ErrorSignature(List<BuildError> errors)
        {
            if (errors == null || errors.Count == 0) return null;
            var sb = new StringBuilder();
            foreach (var e in errors)
            {
                if (e.Severity != ErrorSeverity.Error) continue;
                sb.Append(e.File).Append(':').Append(e.Line).Append(':').Append(e.Code).Append('|');
            }
            return sb.ToString();
        }
    }
}
