using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace ZhuaQianDesktopApp.Agent
{
    // Per-step execution state for an agent plan.
    // Bridges with the existing string-based AgentPlanStep.Status (default "pending").
    // This is the persistent per-step state engine called out as "Partly Implemented"
    // in docs/CODE_COMPLETION_ALIGNMENT.md.
    public enum AgentPlanStepState
    {
        Pending,
        Doing,
        Done,
        Failed,
        Skipped,
        Blocked
    }

    // Result record for a single executed plan step.
    // Feeds Epic D2 (command-run records with stdout/stderr summary and exit code).
    public sealed class AgentPlanStepResult
    {
        public bool Success;
        public string Message = "";
        public DateTime StartedAt = DateTime.MinValue;
        public DateTime FinishedAt = DateTime.MinValue;
        public string OutputSummary = "";
        public string ErrorSummary = "";
        public int ExitCode;

        public TimeSpan Duration
        {
            get
            {
                if (StartedAt == DateTime.MinValue || FinishedAt == DateTime.MinValue) return TimeSpan.Zero;
                return FinishedAt - StartedAt;
            }
        }
    }

    // Runtime view of one plan step: its state plus the last execution result.
    public sealed class AgentPlanStepRuntime
    {
        public string StepId = "";
        public string Title = "";
        public AgentPlanStepState State = AgentPlanStepState.Pending;
        public readonly AgentPlanStepResult Result = new AgentPlanStepResult();

        public AgentPlanStepRuntime() { }

        public AgentPlanStepRuntime(string stepId, string title)
        {
            StepId = stepId ?? "";
            Title = title ?? "";
        }
    }

    // Aggregated execution state for a whole plan. Serializable for persistence
    // (System.Web.Script.Serialization, matching Epic E's PluginManifest).
    public sealed class AgentPlanExecutionState
    {
        public string PlanGoal = "";
        public readonly List<AgentPlanStepRuntime> Steps = new List<AgentPlanStepRuntime>();
        public DateTime CreatedAt = DateTime.Now;
        public DateTime UpdatedAt = DateTime.Now;

        public AgentPlanExecutionState() { }

        // Build from an AgentPlan, mapping each step's existing Status string.
        public static AgentPlanExecutionState FromPlan(AgentPlan plan)
        {
            var state = new AgentPlanExecutionState();
            if (plan == null) return state;
            state.PlanGoal = plan.Goal ?? "";
            foreach (var step in plan.Steps)
            {
                var runtime = new AgentPlanStepRuntime(step.StepId, step.Title);
                runtime.State = MapStatus(step.Status);
                state.Steps.Add(runtime);
            }
            state.Touch();
            return state;
        }

        static AgentPlanStepState MapStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return AgentPlanStepState.Pending;
            switch (status.Trim().ToLowerInvariant())
            {
                case "doing":
                case "running":
                case "inprogress":
                case "in-progress":
                    return AgentPlanStepState.Doing;
                case "done":
                case "complete":
                case "completed":
                case "success":
                case "succeeded":
                    return AgentPlanStepState.Done;
                case "failed":
                case "error":
                case "errored":
                    return AgentPlanStepState.Failed;
                case "skipped":
                    return AgentPlanStepState.Skipped;
                case "blocked":
                    return AgentPlanStepState.Blocked;
                default:
                    return AgentPlanStepState.Pending;
            }
        }

        public AgentPlanStepRuntime Find(string stepId)
        {
            if (string.IsNullOrWhiteSpace(stepId)) return null;
            foreach (var s in Steps)
                if (string.Equals(s.StepId, stepId, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        public void MarkDoing(string stepId)
        {
            var s = Find(stepId);
            if (s != null)
            {
                s.State = AgentPlanStepState.Doing;
                if (s.Result.StartedAt == DateTime.MinValue) s.Result.StartedAt = DateTime.Now;
            }
            Touch();
        }

        public void MarkDone(string stepId, string outputSummary = "")
        {
            var s = Find(stepId);
            if (s != null)
            {
                s.State = AgentPlanStepState.Done;
                s.Result.Success = true;
                s.Result.FinishedAt = DateTime.Now;
                if (!string.IsNullOrEmpty(outputSummary)) s.Result.OutputSummary = outputSummary;
            }
            Touch();
        }

        public void MarkFailed(string stepId, string errorSummary = "", int exitCode = -1)
        {
            var s = Find(stepId);
            if (s != null)
            {
                s.State = AgentPlanStepState.Failed;
                s.Result.Success = false;
                s.Result.FinishedAt = DateTime.Now;
                if (!string.IsNullOrEmpty(errorSummary)) s.Result.ErrorSummary = errorSummary;
                s.Result.ExitCode = exitCode;
            }
            Touch();
        }

        public void MarkSkipped(string stepId)
        {
            var s = Find(stepId);
            if (s != null) s.State = AgentPlanStepState.Skipped;
            Touch();
        }

        public void MarkBlocked(string stepId)
        {
            var s = Find(stepId);
            if (s != null) s.State = AgentPlanStepState.Blocked;
            Touch();
        }

        public int TotalSteps { get { return Steps.Count; } }

        public int DoneCount
        {
            get { int n = 0; foreach (var s in Steps) if (s.State == AgentPlanStepState.Done) n++; return n; }
        }

        public int FailedCount
        {
            get { int n = 0; foreach (var s in Steps) if (s.State == AgentPlanStepState.Failed) n++; return n; }
        }

        public string ProgressSummary()
        {
            return string.Format("{0}/{1} done, {2} failed", DoneCount, TotalSteps, FailedCount);
        }

        void Touch() { UpdatedAt = DateTime.Now; }

        // JSON persistence (System.Web.Script.Serialization, matching Epic E PluginManifest).
        public string ToJson()
        {
            try { return new JavaScriptSerializer().Serialize(this); }
            catch { return "{}"; }
        }

        public static AgentPlanExecutionState FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new AgentPlanExecutionState();
            try
            {
                return new JavaScriptSerializer().Deserialize<AgentPlanExecutionState>(json) ?? new AgentPlanExecutionState();
            }
            catch { return new AgentPlanExecutionState(); }
        }
    }
}
