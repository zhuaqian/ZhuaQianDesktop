using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ZhuaQianDesktopApp.Agent
{
    // Git workflow capability (Epic D5). WorkspaceScanSummary already reads
    // `git status --porcelain` for diff display; this executor turns git into
    // an actionable agent command: diff, commit, branch, and export a patch
    // file. All calls go through PermissionGate via the AgentPipeline, and the
    // resulting patch/diff is recorded to OutputsHub like any other artifact.
    //
    // Supported sub-commands (Parameters["action"]):
    //   diff        -> git diff (working tree vs HEAD)
    //   status      -> git status --porcelain
    //   branch name -> git checkout -b name
    //   commit msg  -> git add -A && git commit -m msg
    //   export-patch-> git diff > <outputs>/<name>.patch
    public sealed class GitWorkflowExecutor : ICommandExecutor
    {
        public string CommandType { get { return "GitWorkflow"; } }

        readonly string rootDirectory;

        public GitWorkflowExecutor(string rootDirectory)
        {
            this.rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? Directory.GetCurrentDirectory() : Path.GetFullPath(rootDirectory);
        }

        public CommandResult Execute(IAgentCommand command)
        {
            string action = GetString(command.Parameters, "action").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(action)) action = "diff";

            string args;
            switch (action)
            {
                case "status": args = "status --porcelain"; break;
                case "diff": args = "diff"; break;
                case "branch":
                    string branch = GetString(command.Parameters, "branch");
                    if (string.IsNullOrWhiteSpace(branch)) return CommandResult.Failed("branch requires a 'branch' parameter");
                    args = "checkout -b " + Quote(branch); break;
                case "commit":
                    string msg = GetString(command.Parameters, "message");
                    if (string.IsNullOrWhiteSpace(msg)) return CommandResult.Failed("commit requires a 'message' parameter");
                    return RunSequence(new[] { "add -A", "commit -m " + Quote(msg) });
                case "export-patch":
                    return ExportPatch(command);
                default:
                    return CommandResult.Failed("unknown git action: " + action);
            }

            return RunGit(args);
        }

        CommandResult RunSequence(string[] gitArgs)
        {
            var sb = new StringBuilder();
            foreach (var a in gitArgs)
            {
                var r = RunGit(a);
                sb.AppendLine("git " + a + " -> " + r.Status.ToString());
                if (r.Status != CommandStatus.Success) return CommandResult.Failed(sb.ToString());
            }
            return CommandResult.Ok(null, false, null, "text", 0, sb.ToString().Trim());
        }

        CommandResult ExportPatch(IAgentCommand command)
        {
            string name = GetString(command.Parameters, "name");
            if (string.IsNullOrWhiteSpace(name)) name = "zq-change";
            string outDir = Path.Combine(rootDirectory, "outputs");
            try { if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir); }
            catch (Exception ex) { return CommandResult.Failed(ex.Message); }
            string patchPath = Path.Combine(outDir, name + ".patch");
            var r = RunGit("diff");
            if (r.Status != CommandStatus.Success) return CommandResult.Failed(r.ErrorMessage);
            try
            {
                File.WriteAllText(patchPath, r.OutputText ?? "", Encoding.UTF8);
                return CommandResult.Ok(patchPath, false, null, "patch",
                    Encoding.UTF8.GetByteCount(r.OutputText ?? ""), r.OutputText);
            }
            catch (Exception ex) { return CommandResult.Failed(ex.Message); }
        }

        CommandResult RunGit(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = rootDirectory,
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
                    bool ok = proc.ExitCode == 0;
                    return ok
                        ? CommandResult.Ok(null, false, null, "text", 0, stdout)
                        : CommandResult.Failed(stderr);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Failed(ex.Message);
            }
        }

        static string Quote(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }

        static string GetString(IReadOnlyDictionary<string, object> values, string key)
        {
            object value;
            if (values != null && values.TryGetValue(key, out value) && value != null)
                return Convert.ToString(value);
            return "";
        }
    }
}
