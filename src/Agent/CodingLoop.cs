using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZhuaQianDesktopApp.Agent
{
    // Closed-loop coding-agent state machine (closes the "agent loop" gap):
    //   Analyze -> Plan -> RunTests -> (Fix -> ApplyPatch -> RunTests)* -> Review -> Done
    // Each transition is recorded in the report (auditable), build/test run via
    // a pluggable ICommandRecorder, patches apply via PatchApplier, and the final
    // diff comes from GitWorkflow. The "decide the fix" step is an injected
    // ICodingFixDecider so the product can plug its LLM; tests use Scripted.
    public enum CodingLoopState
    {
        Analyze, Plan, ApplyPatch, RunTests, Fix, Review, Done, Failed
    }

    // Context handed to the fix decider each iteration.
    public sealed class CodingLoopContext
    {
        public string RootDirectory = "";
        public ProjectScan Scan;
        public int Iteration;
        public string LastBuildOutput = "";
        public string LastBuildError = "";
        public string LastTestOutput = "";
        public string LastTestError = "";
        public bool BuildOk;
        public bool TestOk;
        public readonly List<UnifiedPatch> AppliedPatches = new List<UnifiedPatch>();
    }

    public sealed class FixDecision
    {
        public bool Stop; // true => no automated fix; hand back to human
        public readonly List<UnifiedPatch> Patches = new List<UnifiedPatch>();
        public string Rationale = "";
    }

    // Strategy that decides how to fix a failing build/test. The product plugs
    // its LLM here; tests use ScriptedCodingFixDecider.
    public interface ICodingFixDecider
    {
        Task<FixDecision> DecideFixAsync(CodingLoopContext context, CancellationToken token);
    }

    public sealed class ScriptedCodingFixDecider : ICodingFixDecider
    {
        readonly Queue<List<UnifiedPatch>> queue = new Queue<List<UnifiedPatch>>();
        public ScriptedCodingFixDecider(IEnumerable<List<UnifiedPatch>> plan)
        {
            if (plan != null) foreach (var p in plan) queue.Enqueue(p);
        }
        public Task<FixDecision> DecideFixAsync(CodingLoopContext context, CancellationToken token)
        {
            if (queue.Count == 0)
                return Task.FromResult(new FixDecision { Stop = true, Rationale = "no more scripted fixes" });
            var patches = queue.Dequeue();
            var d = new FixDecision { Stop = false, Rationale = "scripted fix iteration " + context.Iteration.ToString() };
            d.Patches.AddRange(patches);
            return Task.FromResult(d);
        }
    }

    public sealed class InteractiveCodingFixDecider : ICodingFixDecider
    {
        readonly Func<CodingLoopContext, FixDecision> fn;
        public InteractiveCodingFixDecider(Func<CodingLoopContext, FixDecision> fn) { this.fn = fn; }
        public Task<FixDecision> DecideFixAsync(CodingLoopContext context, CancellationToken token)
        {
            return Task.FromResult(fn == null ? new FixDecision { Stop = true } : fn(context));
        }
    }

    public sealed class CodingLoopReport
    {
        public string RootDirectory = "";
        public CodingLoopState FinalState = CodingLoopState.Done;
        public int Iterations;
        public readonly List<AgentPlanStepResult> Steps = new List<AgentPlanStepResult>();
        public readonly List<string> PatchLog = new List<string>();
        public readonly List<string> ChangedFiles = new List<string>();
        public string DiffSummary = "";

        public string ToMarkdown()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## Coding Loop Report");
            sb.AppendLine();
            sb.AppendLine("Root: " + (string.IsNullOrWhiteSpace(RootDirectory) ? "(unknown)" : RootDirectory));
            sb.AppendLine("Final state: " + FinalState.ToString());
            sb.AppendLine("Iterations: " + Iterations.ToString());
            sb.AppendLine();
            sb.AppendLine("### Steps");
            int idx = 0;
            foreach (var s in Steps)
            {
                idx++;
                sb.AppendLine(string.Format("{0}. exit={1} ok={2}", idx, s.ExitCode, s.Success));
                if (!string.IsNullOrEmpty(s.ErrorSummary)) sb.AppendLine("   err: " + Truncate(s.ErrorSummary, 700));
            }
            if (PatchLog.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("### Patches applied");
                foreach (var p in PatchLog) sb.AppendLine("  - " + p);
            }
            if (!string.IsNullOrEmpty(DiffSummary))
            {
                sb.AppendLine();
                sb.AppendLine("### Diff");
                sb.AppendLine(Truncate(DiffSummary, 4000));
            }
            return sb.ToString().Trim();
        }

        static string Truncate(string t, int max)
        {
            if (string.IsNullOrEmpty(t)) return "";
            return t.Length > max ? t.Substring(0, max) + "..." : t;
        }
    }

    public sealed class CodingLoop
    {
        public string RootDirectory = "";
        public string BuildCommand = "";
        public string TestCommand = "";
        public int MaxIterations = 5;
        public ICodingFixDecider Decider;
        readonly ICommandRecorder recorder;

        public CodingLoop(string rootDirectory, ICommandRecorder recorder = null)
        {
            RootDirectory = rootDirectory ?? "";
            this.recorder = recorder ?? new CommandRunRecorder();
        }

        public CodingLoopReport Run(ProjectScan scan = null)
        {
            var report = new CodingLoopReport { RootDirectory = RootDirectory };
            var ctx = new CodingLoopContext { RootDirectory = RootDirectory };

            scan = scan ?? ProjectScanner.Scan(RootDirectory);
            ctx.Scan = scan;
            if (string.IsNullOrWhiteSpace(BuildCommand)) BuildCommand = scan.BuildCommand;
            if (string.IsNullOrWhiteSpace(TestCommand)) TestCommand = scan.TestCommand;

            int iteration = 0;
            while (true)
            {
                iteration++;
                report.Iterations = iteration;

                var buildRes = RunCmd(BuildCommand, "build #" + iteration.ToString());
                var testRes = RunCmd(TestCommand, "test #" + iteration.ToString());
                report.Steps.Add(buildRes);
                if (!string.IsNullOrEmpty(TestCommand)) report.Steps.Add(testRes);

                ctx.BuildOk = buildRes.Success;
                ctx.TestOk = string.IsNullOrEmpty(TestCommand) ? buildRes.Success : testRes.Success;
                ctx.LastBuildOutput = buildRes.OutputSummary;
                ctx.LastBuildError = buildRes.ErrorSummary;
                ctx.LastTestOutput = testRes.OutputSummary;
                ctx.LastTestError = testRes.ErrorSummary;
                ctx.Iteration = iteration;

                if (ctx.BuildOk && ctx.TestOk)
                {
                    report.FinalState = CodingLoopState.Done;
                    break;
                }

                if (iteration > MaxIterations)
                {
                    report.FinalState = CodingLoopState.Failed;
                    report.PatchLog.Add("max iterations reached; handing back to human");
                    break;
                }

                var decision = Decider == null
                    ? new FixDecision { Stop = true }
                    : Decider.DecideFixAsync(ctx, CancellationToken.None).GetAwaiter().GetResult();
                if (decision == null || decision.Stop || decision.Patches.Count == 0)
                {
                    report.FinalState = CodingLoopState.Failed;
                    report.PatchLog.Add("no automated fix proposed; handing back to human");
                    break;
                }

                foreach (var patch in decision.Patches)
                {
                    string status = PatchApplier.ApplyToWorkspace(RootDirectory, patch);
                    report.PatchLog.Add(status);
                    ctx.AppliedPatches.Add(patch);
                    if (!string.IsNullOrEmpty(patch.FilePath) && !report.ChangedFiles.Contains(patch.FilePath))
                        report.ChangedFiles.Add(patch.FilePath);
                }
            }

            var git = new GitWorkflow(RootDirectory, recorder);
            report.DiffSummary = git.HasRepo()
                ? (git.Diff().OutputSummary ?? "(empty diff)")
                : "not a git repo; changed files: " + string.Join(", ", report.ChangedFiles);
            return report;
        }

        AgentPlanStepResult RunCmd(string command, string label)
        {
            if (string.IsNullOrWhiteSpace(command))
                return new AgentPlanStepResult { Success = true, ExitCode = 0, OutputSummary = "skipped (" + label + ": no command)" };
            int sp = command.IndexOf(' ');
            string file = sp < 0 ? command : command.Substring(0, sp);
            string args = sp < 0 ? "" : command.Substring(sp + 1);
            return recorder.Run(file, args, RootDirectory);
        }
    }
}
