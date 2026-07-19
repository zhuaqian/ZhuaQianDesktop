using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ZhuaQianDesktopApp.Agent
{
    // The iterative self-healing loop (Epic D4). This is the piece
    // CodingAgentSession lacked: it does not stop at the first build/test
    // failure. It runs the command, parses the error, asks the injected
    // IFixStrategy for a patch, applies it through PatchExecutor, and re-runs
    // until green or maxIterations is reached. Every step is gated by
    // PermissionGate and recorded to AuditLog via the AgentPipeline.
    //
    // State machine: Analyze -> Plan -> ApplyPatch -> RunTests -> Fix -> Review -> Done
    public sealed class FixLoopRunner
    {
        readonly AgentPipeline pipeline;
        readonly string rootDirectory;
        readonly ICommandRecorder recorder;
        readonly IFixStrategy strategy;
        readonly int maxIterations;

        public List<string> Log = new List<string>();

        public FixLoopRunner(AgentPipeline pipeline, string rootDirectory,
            ICommandRecorder recorder = null, IFixStrategy strategy = null, int maxIterations = 4)
        {
            this.pipeline = pipeline;
            this.rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? Directory.GetCurrentDirectory() : Path.GetFullPath(rootDirectory);
            this.recorder = recorder ?? new GuardedCommandRunRecorder(this.rootDirectory);
            this.strategy = strategy ?? new NoOpFixStrategy();
            this.maxIterations = maxIterations;
        }

        public FixLoopReport Run(string buildCommand, string testCommand)
        {
            var report = new FixLoopReport { RootDirectory = rootDirectory };
            Log.Clear();

            string command = !string.IsNullOrWhiteSpace(testCommand) ? testCommand
                          : (!string.IsNullOrWhiteSpace(buildCommand) ? buildCommand : "");
            if (string.IsNullOrWhiteSpace(command))
            {
                Log.Add("[Analyze] no build/test command found");
                report.Status = "no-command";
                return report;
            }

            for (int i = 1; i <= maxIterations; i++)
            {
                Log.Add("[RunTests] iteration " + i + ": " + command);
                var step = recorder.Run("powershell", "-NoProfile -ExecutionPolicy Bypass -Command " + command, rootDirectory);
                report.Iterations.Add(new FixLoopIteration
                {
                    Iteration = i,
                    Command = command,
                    ExitCode = step.ExitCode,
                    Success = step.Success
                });

                if (step.Success)
                {
                    Log.Add("[Review] iteration " + i + ": PASSED");
                    report.Status = "passed";
                    break;
                }

                Log.Add("[Fix] iteration " + i + ": FAILED (exit " + step.ExitCode + ")");
                var errors = ParseErrors(step);
                if (errors.Count == 0)
                {
                    Log.Add("[Fix] no structured error parsed; stopping");
                    report.Status = "unparseable-error";
                    break;
                }

                var patch = strategy.Propose(this.rootDirectory, step, errors);
                if (patch == null || patch.Count == 0)
                {
                    Log.Add("[Fix] strategy proposed no patch; stopping");
                    report.Status = "no-fix-proposed";
                    break;
                }

                foreach (var op in patch)
                {
                    var cmd = new AgentCommand("PatchFile", "permPatchFile", "fix-loop", op.Target,
                        "apply patch " + op.Op + " " + op.Target,
                        new Dictionary<string, object>
                        {
                            { "op", op.Op },
                            { "oldText", op.OldText ?? "" },
                            { "newText", op.NewText ?? "" }
                        });
                    var res = pipeline.Run(cmd);
                    if (res == null || res.Status != CommandStatus.Success)
                    {
                        Log.Add("[ApplyPatch] FAILED on " + op.Target + ": " +
                                (res == null ? "no result" : res.ErrorMessage));
                        report.Status = "patch-failed";
                        return report;
                    }
                    report.ChangedFiles.Add(op.Target);
                    Log.Add("[ApplyPatch] applied " + op.Op + " on " + op.Target);
                }
            }

            if (report.Status == "pending")
                report.Status = "max-iterations-reached";
            return report;
        }

        // Mirrors the Python ErrorParser: csc "file(line,col): error CSxxxx"
        // and Python unittest/pytest "FAIL: module.Class.test" lines.
        static List<BuildError> ParseErrors(AgentPlanStepResult step)
        {
            var errors = new List<BuildError>();
            string blob = (step.OutputSummary ?? "") + "\n" + (step.ErrorSummary ?? "");
            foreach (var line in blob.Split('\n'))
            {
                var m = Regex.Match(line, @"([^(%\s]+)\((\d+),(\d+)\):\s*error\s+(\w+):\s*(.*)");
                if (m.Success)
                {
                    errors.Add(new BuildError { File = m.Groups[1].Value, Line = int.Parse(m.Groups[2].Value),
                        Message = m.Groups[4].Value + ": " + m.Groups[5].Value });
                    continue;
                }
                var f = Regex.Match(line, @"(?:FAIL|ERROR):\s+([\w.]+)");
                if (f.Success)
                {
                    string msg = "";
                    var am = Regex.Match(blob.Substring(blob.IndexOf(line)), @"(AssertionError|NameError|TypeError|ValueError|KeyError):\s*(.*)");
                    if (am.Success) msg = am.Groups[1].Value + ": " + am.Groups[2].Value;
                    errors.Add(new BuildError { File = InferTarget(blob), Line = null, Message = msg });
                }
            }
            return errors;
        }

        static string InferTarget(string blob)
        {
            foreach (var line in blob.Split('\n'))
            {
                var m = Regex.Match(line, @"(?:from|import)\s+([A-Za-z_]\w*)");
                if (m.Success && m.Groups[1].Value != "from" && m.Groups[1].Value != "import")
                    return m.Groups[1].Value + ".py";
            }
            return "unknown.py";
        }

        // ---- strategy contract (production: LLM fills this) ----
        public interface IFixStrategy
        {
            List<PatchOp> Propose(string root, AgentPlanStepResult failedStep, List<BuildError> errors);
        }

        sealed class NoOpFixStrategy : IFixStrategy
        {
            public List<PatchOp> Propose(string root, AgentPlanStepResult failedStep, List<BuildError> errors)
            { return new List<PatchOp>(); }
        }

        public sealed class PatchOp
        {
            public string Op;        // add | edit | delete
            public string Target;    // relative path
            public string OldText;
            public string NewText;
        }

        public sealed class BuildError
        {
            public string File;
            public int? Line;
            public string Message;
        }

        public sealed class FixLoopIteration
        {
            public int Iteration;
            public string Command;
            public int ExitCode;
            public bool Success;
        }

        public sealed class FixLoopReport
        {
            public string RootDirectory = "";
            public string Status = "pending";
            public List<FixLoopIteration> Iterations = new List<FixLoopIteration>();
            public List<string> ChangedFiles = new List<string>();

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("## Diagnose & Fix Loop Report");
                sb.AppendLine();
                sb.AppendLine("Root: " + (string.IsNullOrWhiteSpace(RootDirectory) ? "(unknown)" : RootDirectory));
                sb.AppendLine("Status: **" + Status + "**");
                sb.AppendLine("Iterations: " + Iterations.Count.ToString());
                if (ChangedFiles.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("### Changed files (" + ChangedFiles.Count.ToString() + ")");
                    foreach (var f in ChangedFiles) sb.AppendLine("- " + f);
                }
                if (Iterations.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("### Iterations");
                    foreach (var it in Iterations)
                        sb.AppendLine("- #" + it.Iteration.ToString() + " `" + it.Command + "` -> exit " + it.ExitCode.ToString() + " " + (it.Success ? "PASS" : "FAIL"));
                }
                return sb.ToString().Trim();
            }
        }
    }
}
