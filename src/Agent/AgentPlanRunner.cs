using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ZhuaQianDesktopApp.Agent
{
    // Runs an AgentPlan through the AgentPipeline while tracking per-step
    // execution state (AgentPlanExecutionState) and persisting it as JSON.
    // Closes the "Partly Implemented: no full persistent per-step state engine"
    // gap noted in docs/CODE_COMPLETION_ALIGNMENT.md (Epic C / Agent Loop).
    public sealed class AgentPlanRunner
    {
        readonly AgentPipeline pipeline;
        readonly AgentPlanCommandMapper mapper = new AgentPlanCommandMapper();

        // Directory where per-run state JSON files are written.
        public string PlanRunsDir;

        public AgentPlanRunner(AgentPipeline pipeline, string planRunsDir = null)
        {
            this.pipeline = pipeline;
            PlanRunsDir = planRunsDir;
            if (string.IsNullOrWhiteSpace(PlanRunsDir))
            {
                PlanRunsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ZhuaQianDesktop", "plan-runs");
            }
        }

        public async Task<AgentPlanExecutionState> RunPlanAsync(
            AgentPlan plan,
            AgentPlanCommandMapperOptions options,
            CancellationToken token = default)
        {
            var state = AgentPlanExecutionState.FromPlan(plan);
            if (plan == null || options == null) return state;

            string id = SafeId(string.IsNullOrWhiteSpace(plan.Goal) ? "plan" : plan.Goal);
            try { if (!Directory.Exists(PlanRunsDir)) Directory.CreateDirectory(PlanRunsDir); }
            catch { /* persistence is best-effort */ }

            Persist(state, id);

            foreach (var step in plan.Steps)
            {
                IAgentCommand command = mapper.BuildCommandForStep(plan, step, options);
                if (command == null)
                {
                    state.MarkSkipped(step.StepId);
                    Persist(state, id);
                    continue;
                }

                state.MarkDoing(step.StepId);
                Persist(state, id);

                CommandResult result;
                try { result = await pipeline.RunAsync(command, token); }
                catch (Exception ex) { result = CommandResult.Failed(ex.Message); }
                if (result == null) result = CommandResult.Failed("no result");

                if (result.Status == CommandStatus.Success)
                    state.MarkDone(step.StepId, result.ResultPath ?? result.OutputText ?? "");
                else
                    state.MarkFailed(step.StepId, result.ErrorMessage ?? result.Status.ToString(), -1);

                Persist(state, id);

                if (result.Status != CommandStatus.Success) break;
            }

            return state;
        }

        void Persist(AgentPlanExecutionState state, string id)
        {
            try { File.WriteAllText(Path.Combine(PlanRunsDir, id + ".json"), state.ToJson()); }
            catch { /* persistence is best-effort */ }
        }

        static string SafeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "plan-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
            value = Regex.Replace(value, @"\s+", "-").Trim('-');
            if (value.Length > 48) value = value.Substring(0, 48).Trim('-');
            if (value.Length == 0) value = "plan-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            return value;
        }
    }
}
