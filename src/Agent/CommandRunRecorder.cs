using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace ZhuaQianDesktopApp.Agent
{
    // Abstraction over "run an external command and capture its result", so the
    // coding-agent session can be tested with a fake recorder (Epic D2).
    public interface ICommandRecorder
    {
        AgentPlanStepResult Run(string fileName, string arguments, string workingDirectory = "");
    }

    // Captures the output of an external command (build, test, git, ...) into an
    // AgentPlanStepResult so a coding-agent session can show Plan -> Command ->
    // Diff -> Test -> Review with real stdout/stderr/exit code (Epic D2).
    //
    // The two redirected streams are read asynchronously and concurrently before
    // WaitForExit, which avoids the classic deadlock where a full stderr buffer
    // blocks a process that is waiting for its stdout to be drained.
    public sealed class CommandRunRecorder : ICommandRecorder
    {
        public int MaxSummaryLength = 2000;

        public AgentPlanStepResult Run(string fileName, string arguments, string workingDirectory = "")
        {
            var result = new AgentPlanStepResult();
            result.StartedAt = DateTime.Now;
            try
            {
                var psi = new ProcessStartInfo(fileName, arguments)
                {
                    WorkingDirectory = workingDirectory ?? "",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var proc = new Process())
                {
                    proc.StartInfo = psi;
                    proc.Start();
                    var outTask = proc.StandardOutput.ReadToEndAsync();
                    var errTask = proc.StandardError.ReadToEndAsync();
                    proc.WaitForExit();
                    string stdout = outTask.Result;
                    string stderr = errTask.Result;
                    result.ExitCode = proc.ExitCode;
                    result.Success = proc.ExitCode == 0;
                    result.OutputSummary = Summarize(stdout);
                    result.ErrorSummary = Summarize(stderr);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ExitCode = -1;
                result.ErrorSummary = Summarize(ex.Message);
            }
            result.FinishedAt = DateTime.Now;
            return result;
        }

        string Summarize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Trim();
            if (text.Length <= MaxSummaryLength) return text;
            return text.Substring(0, MaxSummaryLength) + "\n... (truncated)";
        }
    }
}
