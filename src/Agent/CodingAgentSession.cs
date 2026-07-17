using System;
using System.Collections.Generic;
using System.Text;

namespace ZhuaQianDesktopApp.Agent
{
    // Lightweight coding-agent session reporter (Epic D). Closes the Codex/Claude
    // Code gap by producing a single Plan -> Command -> Diff -> Test -> Review
    // narrative the UI can show without leaving the app. It composes the plan
    // schema (AgentPlan), the per-step state engine (AgentPlanExecutionState /
    // AgentPlanStepResult), the workspace scan (WorkspaceScanSummary), and the
    // command recorder (CommandRunRecorder) into one reviewable report.
    public sealed class CodingAgentSession
    {
        public string RootDirectory = "";
        public string BuildCommand = @"powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1";
        public string TestCommand = @"powershell -NoProfile -ExecutionPolicy Bypass -File .\src\scripts\run-tests.ps1";

        // Recorder used to run build/test. Defaults to a real CommandRunRecorder;
        // tests inject a fake to avoid running the (heavy) real build/test.
        public ICommandRecorder Recorder = new CommandRunRecorder();

        public CodingAgentSessionReport Run(AgentPlan plan)
        {
            var report = new CodingAgentSessionReport();
            report.Goal = plan == null ? "" : plan.Goal ?? "";

            var scan = WorkspaceScanSummary.Capture(RootDirectory);
            report.WorkspaceScan = scan.ToMarkdown();
            report.DiffSummary = BuildDiffSummary(scan);
            report.PlanReview = plan == null ? "No plan provided." : plan.ToReviewMarkdown();

            if (Recorder != null)
            {
                report.BuildResult = Recorder.Run("powershell", "-NoProfile -ExecutionPolicy Bypass -File .\\build.ps1", RootDirectory);
                report.TestResult = Recorder.Run("powershell", "-NoProfile -ExecutionPolicy Bypass -File .\\src\\scripts\\run-tests.ps1", RootDirectory);
            }
            else
            {
                report.BuildResult = new AgentPlanStepResult();
                report.TestResult = new AgentPlanStepResult();
            }

            report.Finalize();
            return report;
        }

        string BuildDiffSummary(WorkspaceScanSummary scan)
        {
            if (scan == null || scan.ChangedFileCount == 0)
                return "Diff: no uncommitted changes detected (workspace clean or git unavailable).";
            var sb = new StringBuilder();
            sb.AppendLine("Diff: " + scan.ChangedFileCount.ToString() + " file(s) changed");
            int shown = 0;
            foreach (var f in scan.ChangedFiles)
            {
                if (shown >= 30) { sb.AppendLine("  ... and " + (scan.ChangedFileCount - shown).ToString() + " more"); break; }
                sb.AppendLine("  M " + f);
                shown++;
            }
            return sb.ToString().Trim();
        }
    }

    public sealed class CodingAgentSessionReport
    {
        public string Goal = "";
        public string PlanReview = "";
        public string WorkspaceScan = "";
        public string DiffSummary = "";
        public AgentPlanStepResult BuildResult = new AgentPlanStepResult();
        public AgentPlanStepResult TestResult = new AgentPlanStepResult();
        public string Status = "pending";
        public string ReviewNotes = "";

        public void Finalize()
        {
            bool buildOk = BuildResult != null && BuildResult.Success;
            bool testOk = TestResult != null && TestResult.Success;
            if (!buildOk) Status = "build-failed";
            else if (!testOk) Status = "test-failed";
            else Status = "passed";
            var sb = new StringBuilder();
            sb.AppendLine("Build: " + (buildOk ? "passed (exit " + BuildResult.ExitCode.ToString() + ")" : "FAILED (exit " + BuildResult.ExitCode.ToString() + ")"));
            sb.AppendLine("Tests: " + (testOk ? "passed (exit " + TestResult.ExitCode.ToString() + ")" : "FAILED (exit " + TestResult.ExitCode.ToString() + ")"));
            sb.AppendLine("Progress: " + Status);
            ReviewNotes = sb.ToString().Trim();
        }

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Coding Agent Session Report");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(Goal)) sb.AppendLine("Goal: " + Goal).AppendLine();
            sb.AppendLine(PlanReview);
            sb.AppendLine();
            sb.AppendLine(WorkspaceScan);
            sb.AppendLine();
            sb.AppendLine(DiffSummary);
            sb.AppendLine();
            sb.AppendLine("### Build");
            sb.AppendLine("Exit code: " + BuildResult.ExitCode.ToString() + "  Success: " + BuildResult.Success.ToString());
            sb.AppendLine(Truncate(BuildResult.OutputSummary));
            sb.AppendLine(Truncate(BuildResult.ErrorSummary));
            sb.AppendLine();
            sb.AppendLine("### Test");
            sb.AppendLine("Exit code: " + TestResult.ExitCode.ToString() + "  Success: " + TestResult.Success.ToString());
            sb.AppendLine(Truncate(TestResult.OutputSummary));
            sb.AppendLine(Truncate(TestResult.ErrorSummary));
            sb.AppendLine();
            sb.AppendLine("### Review");
            sb.AppendLine(ReviewNotes);
            return sb.ToString().Trim();
        }

        static string Truncate(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length > 1500 ? text.Substring(0, 1500) + "\n... (truncated)" : text;
        }
    }
}
