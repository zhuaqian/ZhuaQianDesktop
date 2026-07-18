using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ZhuaQianDesktopApp.Core;

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

    // Permission-aware wrapper for the coding-agent build/test runner. It keeps
    // today's Full Review useful by allowing only this repository's known build
    // and test scripts automatically. Any future arbitrary command must be
    // explicitly allowed by PermissionGate, otherwise it is denied before
    // Process.Start is reached.
    public sealed class GuardedCommandRunRecorder : ICommandRecorder
    {
        readonly ICommandRecorder inner;
        readonly PermissionGate permissionGate;
        readonly string rootDirectory;

        public GuardedCommandRunRecorder(string rootDirectory)
            : this(rootDirectory, null, null)
        {
        }

        public GuardedCommandRunRecorder(string rootDirectory, PermissionGate permissionGate, ICommandRecorder inner)
        {
            this.rootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? "" : Path.GetFullPath(rootDirectory);
            this.permissionGate = permissionGate ?? new PermissionGate();
            this.inner = inner ?? new CommandRunRecorder();
        }

        public AgentPlanStepResult Run(string fileName, string arguments, string workingDirectory = "")
        {
            string workDir = string.IsNullOrWhiteSpace(workingDirectory) ? rootDirectory : Path.GetFullPath(workingDirectory);
            string target = (fileName ?? "") + " " + (arguments ?? "");
            if (!IsKnownProjectBuildOrTest(fileName, arguments, workDir))
            {
                PermissionDecision decision = permissionGate.Check("permCommandRun", target);
                if (decision != PermissionDecision.Allow)
                    return Denied(target);
            }
            return inner.Run(fileName, arguments, workDir);
        }

        bool IsKnownProjectBuildOrTest(string fileName, string arguments, string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(workingDirectory))
                return false;
            if (!IsWithinRoot(workingDirectory)) return false;
            string exe = Path.GetFileName(fileName ?? "");
            if (!string.Equals(exe, "powershell", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(exe, "powershell.exe", StringComparison.OrdinalIgnoreCase))
                return false;

            string args = arguments ?? "";
            if (ContainsScript(args, @".\build.ps1")) return true;
            if (ContainsScript(args, @".\src\scripts\run-tests.ps1")) return true;
            if (ContainsScript(args, @"build.ps1")) return true;
            if (ContainsScript(args, @"src\scripts\run-tests.ps1")) return true;
            return false;
        }

        bool ContainsScript(string arguments, string script)
        {
            return arguments.IndexOf(script, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   arguments.Replace('/', '\\').IndexOf(script.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        bool IsWithinRoot(string path)
        {
            try
            {
                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string root = rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ||
                       full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                       full.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GuardedCommandRunRecorder root check: " + ex.Message);
                return false;
            }
        }

        AgentPlanStepResult Denied(string target)
        {
            var result = new AgentPlanStepResult();
            result.StartedAt = DateTime.Now;
            result.FinishedAt = DateTime.Now;
            result.Success = false;
            result.ExitCode = -2;
            result.ErrorSummary = "command denied by PermissionGate: " + target;
            return result;
        }
    }
}
