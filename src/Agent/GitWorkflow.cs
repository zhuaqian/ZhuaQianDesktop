using System;
using System.Diagnostics;
using System.IO;

namespace ZhuaQianDesktopApp.Agent
{
    // Git workflow helpers (closes the "Git workflow" gap). Thin wrappers over
    // git via a pluggable ICommandRecorder so tests can fake it. Read-only except
    // Commit/CreateBranch/ExportPatch, which the caller gates by the DiagnoseFix
    // command permission. (All real side effects already passed the agent gate.)
    public sealed class GitWorkflow
    {
        readonly ICommandRecorder recorder;
        public string RootDirectory = "";

        public GitWorkflow(string rootDirectory) : this(rootDirectory, null) { }
        public GitWorkflow(string rootDirectory, ICommandRecorder recorder)
        {
            RootDirectory = rootDirectory ?? "";
            this.recorder = recorder ?? new CommandRunRecorder();
        }

        public bool HasRepo()
        {
            return !string.IsNullOrWhiteSpace(RootDirectory)
                && Directory.Exists(Path.Combine(RootDirectory, ".git"));
        }

        public AgentPlanStepResult Status()
        {
            return recorder.Run("git", "status --porcelain", RootDirectory);
        }

        public AgentPlanStepResult Diff()
        {
            return recorder.Run("git", "diff", RootDirectory);
        }

        public AgentPlanStepResult CreateBranch(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Deny("empty branch name");
            return recorder.Run("git", "checkout -b " + name, RootDirectory);
        }

        public AgentPlanStepResult Commit(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) message = "chore: apply coding-agent fixes";
            var add = recorder.Run("git", "add -A", RootDirectory);
            if (!add.Success) return add;
            string escaped = message.Replace("\"", "\\\"");
            return recorder.Run("git", "commit -m \"" + escaped + "\"", RootDirectory);
        }

        public AgentPlanStepResult ExportPatch(string outputPath)
        {
            var res = recorder.Run("git", "diff", RootDirectory);
            try
            {
                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(outputPath, res.OutputSummary ?? "");
                return new AgentPlanStepResult
                {
                    Success = true,
                    ExitCode = 0,
                    OutputSummary = "patch exported to " + outputPath
                };
            }
            catch (Exception ex)
            {
                return new AgentPlanStepResult { Success = false, ExitCode = -1, ErrorSummary = ex.Message };
            }
        }

        static AgentPlanStepResult Deny(string why)
        {
            return new AgentPlanStepResult { Success = false, ExitCode = -2, ErrorSummary = why };
        }
    }
}
