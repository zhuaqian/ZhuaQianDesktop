using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Agent;

namespace ZhuaQianDesktopApp.Agent.Coding
{
    // Phases of the coding loop, matching the Analyze -> Plan -> ApplyPatch ->
    // RunTests -> Fix -> Review -> Done state machine. Each phase is auditable.
    public enum CodingLoopPhase
    {
        NotStarted,
        Analyze,
        Plan,
        Execute,     // ApplyPatch + RunTests + Fix (delegated to BuildFixLoop)
        Review,
        Done
    }

    // Final status of a coding-loop run.
    public enum CodingLoopStatus
    {
        NotStarted,
        Passed,
        BuildFailed,
        TestFailed,
        CannotFix,
        Denied,
        Error
    }

    // Top-level report for one coding-loop run. Composes the BuildFixLoop
    // report (which holds every iteration's build/test results and applied
    // patches), the project profile, and the git review summary. This is what
    // the UI shows when the user asks "check why this project fails to build
    // and fix it" — a single reviewable artifact.
    public sealed class CodingLoopReport
    {
        public string Goal = "";
        public CodingLoopPhase Phase = CodingLoopPhase.NotStarted;
        public CodingLoopStatus Status = CodingLoopStatus.NotStarted;
        public ProjectProfile Profile;
        public BuildFixLoopReport BuildFixReport;
        public string GitSummary = "";
        public string SuggestedCommitMessage = "";
        public string AllDiffs = "";
        public string ReviewNotes = "";
        public DateTime StartedAt = DateTime.Now;
        public DateTime FinishedAt;

        public bool Succeeded { get { return Status == CodingLoopStatus.Passed; } }

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Coding Loop Report");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(Goal)) sb.AppendLine("**Goal:** " + Goal);
            sb.AppendLine("**Status:** " + Status + " (phase: " + Phase + ")");
            sb.AppendLine();

            if (Profile != null)
            {
                sb.AppendLine("## Project");
                sb.AppendLine("- Language: " + Profile.Language);
                sb.AppendLine("- Framework: " + Profile.Framework);
                sb.AppendLine("- Build: `" + Profile.BuildCommand + "`");
                sb.AppendLine("- Test: `" + Profile.TestCommand + "`");
                sb.AppendLine();
            }

            if (BuildFixReport != null)
            {
                sb.AppendLine(BuildFixReport.ToMarkdown());
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(AllDiffs))
            {
                sb.AppendLine("## All Applied Diffs");
                sb.AppendLine("```diff");
                sb.AppendLine(Truncate(AllDiffs, 3000));
                sb.AppendLine("```");
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(GitSummary))
            {
                sb.AppendLine("## Git Review");
                sb.AppendLine(GitSummary);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(SuggestedCommitMessage))
            {
                sb.AppendLine("## Suggested Commit");
                sb.AppendLine("```");
                sb.AppendLine(SuggestedCommitMessage);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(ReviewNotes))
            {
                sb.AppendLine("## Review Notes");
                sb.AppendLine(ReviewNotes);
            }
            return sb.ToString().Trim();
        }

        static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length > max ? text.Substring(0, max) + "\n... (truncated)" : text;
        }
    }

    // The top-level coding-loop orchestrator. Ties together the project
    // analyzer, the build-fix loop, and the git workflow into the state machine
    // a user invokes when they say "check why this project fails to build and
    // fix it":
    //
    //   Analyze  -> ProjectAnalyzer identifies language/build/test.
    //   Plan     -> IFixStrategy is selected (rule-based today, model-based later).
    //   Execute  -> BuildFixLoop runs build -> parse errors -> apply patches -> re-run.
    //   Review   -> GitWorkflow shows the diff and suggests a commit message.
    //   Done     -> CodingLoopReport is returned for the UI to render.
    //
    // Every side-effect (file write via CodePatcher, git command via
    // GitWorkflow) flows through PermissionGate, so the loop inherits the
    // project's existing permission/audit pipeline.
    public sealed class CodingLoopSession
    {
        readonly string rootDirectory;
        readonly PermissionGate permissionGate;
        readonly ICommandRecorder recorder;
        readonly IFixStrategy strategy;
        readonly ProjectAnalyzer analyzer = new ProjectAnalyzer();

        public CodingLoopSession(string rootDirectory, PermissionGate permissionGate, ICommandRecorder recorder, IFixStrategy strategy)
        {
            this.rootDirectory = rootDirectory ?? "";
            this.permissionGate = permissionGate ?? new PermissionGate();
            this.recorder = recorder;
            this.strategy = strategy ?? new RuleBasedFixStrategy();
        }

        public CodingLoopReport Run(string goal, BuildFixLoopOptions options = null)
        {
            var report = new CodingLoopReport();
            report.Goal = goal ?? "";
            report.StartedAt = DateTime.Now;

            try
            {
                // Phase: Analyze
                report.Phase = CodingLoopPhase.Analyze;
                report.Profile = analyzer.Analyze(rootDirectory);

                // Phase: Plan (the strategy IS the plan today; a future model-driven
                // plan step can replace the strategy before Execute).
                report.Phase = CodingLoopPhase.Plan;

                // Phase: Execute (ApplyPatch + RunTests + Fix, iterated by BuildFixLoop)
                report.Phase = CodingLoopPhase.Execute;
                if (options == null)
                {
                    options = new BuildFixLoopOptions();
                    options.RootDirectory = rootDirectory;
                }
                else if (string.IsNullOrWhiteSpace(options.RootDirectory))
                {
                    options.RootDirectory = rootDirectory;
                }

                var loop = new BuildFixLoop(rootDirectory, permissionGate, recorder, strategy);
                report.BuildFixReport = loop.Run(options);

                // Map BuildFixLoop status to CodingLoop status.
                switch (report.BuildFixReport.Status)
                {
                    case BuildFixLoopStatus.Passed: report.Status = CodingLoopStatus.Passed; break;
                    case BuildFixLoopStatus.BuildFailed: report.Status = CodingLoopStatus.BuildFailed; break;
                    case BuildFixLoopStatus.TestFailed: report.Status = CodingLoopStatus.TestFailed; break;
                    case BuildFixLoopStatus.CannotFix: report.Status = CodingLoopStatus.CannotFix; break;
                    case BuildFixLoopStatus.Exhausted: report.Status = CodingLoopStatus.CannotFix; break;
                    case BuildFixLoopStatus.Denied: report.Status = CodingLoopStatus.Denied; break;
                    default: report.Status = CodingLoopStatus.Error; break;
                }

                // Phase: Review (collect diffs + git summary)
                report.Phase = CodingLoopPhase.Review;
                CollectDiffs(report);
                CollectGitSummary(report);

                // Phase: Done
                report.Phase = CodingLoopPhase.Done;
                report.ReviewNotes = BuildReviewNotes(report);
            }
            catch (Exception ex)
            {
                report.Status = CodingLoopStatus.Error;
                report.ReviewNotes = "Coding loop crashed: " + ex.Message;
                report.Phase = CodingLoopPhase.Done;
            }

            report.FinishedAt = DateTime.Now;
            return report;
        }

        void CollectDiffs(CodingLoopReport report)
        {
            if (report.BuildFixReport == null || report.BuildFixReport.AppliedPatches.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var p in report.BuildFixReport.AppliedPatches)
            {
                if (string.IsNullOrEmpty(p.DiffText)) continue;
                sb.AppendLine(p.DiffText);
                sb.AppendLine();
            }
            report.AllDiffs = sb.ToString().Trim();
        }

        void CollectGitSummary(CodingLoopReport report)
        {
            try
            {
                var git = new GitWorkflow(rootDirectory, permissionGate, recorder);
                report.GitSummary = git.ToMarkdown();
                report.SuggestedCommitMessage = git.SuggestCommitMessage();
            }
            catch (Exception ex)
            {
                report.GitSummary = "Git review unavailable: " + ex.Message;
            }
        }

        static string BuildReviewNotes(CodingLoopReport report)
        {
            var sb = new StringBuilder();
            if (report.BuildFixReport == null) return "No build-fix report.";
            var bfr = report.BuildFixReport;
            sb.AppendLine("Loop ran " + bfr.IterationsRun + " iteration(s) and finished with status: " + bfr.Status + ".");
            if (!string.IsNullOrEmpty(bfr.StopReason)) sb.AppendLine("Stop reason: " + bfr.StopReason);
            if (bfr.AppliedPatches.Count > 0)
            {
                sb.AppendLine("Applied " + bfr.AppliedPatches.Count + " patch(es):");
                foreach (var p in bfr.AppliedPatches)
                    sb.AppendLine("  - " + p.Operation + " " + p.FilePath + (p.Success ? "" : " (FAILED)"));
            }
            else
            {
                sb.AppendLine("No patches were applied.");
            }
            if (report.Status == CodingLoopStatus.Passed)
                sb.AppendLine("Build and tests pass. Ready to commit with the suggested message.");
            else if (report.Status == CodingLoopStatus.CannotFix)
                sb.AppendLine("The loop could not fix all errors automatically. Manual review needed.");
            else
                sb.AppendLine("The loop did not reach a passing state. Check the build/test output above.");
            return sb.ToString().Trim();
        }
    }
}
