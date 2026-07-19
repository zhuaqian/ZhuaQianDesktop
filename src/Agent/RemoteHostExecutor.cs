using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ZhuaQianDesktopApp.Agent
{
    // Remote host capability for server/cloud-host interaction. It deliberately
    // uses the user's existing OpenSSH/scp setup and never stores passwords or
    // private keys. All invocations enter through AgentPipeline so permission,
    // approval, audit, and output recording remain consistent with local actions.
    public sealed class RemoteHostExecutor : ICommandExecutor
    {
        public string CommandType { get { return "RemoteHost"; } }

        readonly string outputDir;
        public int TimeoutMs = 120000;
        public int MaxOutputChars = 6000;

        public RemoteHostExecutor(string outputDir)
        {
            this.outputDir = string.IsNullOrWhiteSpace(outputDir)
                ? Path.Combine(Directory.GetCurrentDirectory(), "remote-output")
                : outputDir;
        }

        public CommandResult Execute(IAgentCommand command)
        {
            string action = GetString(command.Parameters, "action").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(action)) action = "check";

            if (action == "check") return CheckTools();
            if (action == "run") return RunRemote(command);
            if (action == "pull") return CopyFromRemote(command);
            if (action == "push") return CopyToRemote(command);

            return CommandResult.Failed("unknown remote action: " + action);
        }

        CommandResult CheckTools()
        {
            var ssh = RunProcess("ssh", "-V", "", 10000);
            var scp = RunProcess("scp", "-V", "", 10000);
            var sb = new StringBuilder();
            sb.AppendLine("ssh: " + (ssh.Ok ? "available" : "missing") + " " + FirstNonEmpty(ssh.Stderr, ssh.Stdout));
            sb.AppendLine("scp: " + (scp.Ok ? "available" : "missing") + " " + FirstNonEmpty(scp.Stderr, scp.Stdout));
            return ssh.Ok || scp.Ok
                ? CommandResult.Ok(null, false, null, "remote-check", 0, sb.ToString().Trim())
                : CommandResult.Failed(sb.ToString().Trim());
        }

        CommandResult RunRemote(IAgentCommand command)
        {
            string host = GetString(command.Parameters, "host");
            string remoteCommand = GetString(command.Parameters, "command");
            if (!IsSafeHost(host)) return CommandResult.Failed("invalid host. Use user@host or host, without spaces or shell characters.");
            if (string.IsNullOrWhiteSpace(remoteCommand)) return CommandResult.Failed("remote run requires command parameter");

            var args = "-o BatchMode=yes -o ConnectTimeout=10 " + Quote(host) + " " + Quote(remoteCommand);
            var result = RunProcess("ssh", args, "", TimeoutMs);
            string output = BuildRemoteOutput("ssh " + host, result);
            string path = WriteOutput("remote-run", output);
            return result.Ok
                ? CommandResult.Ok(path, false, null, "remote-log", Encoding.UTF8.GetByteCount(output), output)
                : CommandResult.Failed(output);
        }

        CommandResult CopyFromRemote(IAgentCommand command)
        {
            string host = GetString(command.Parameters, "host");
            string remotePath = GetString(command.Parameters, "remotePath");
            string localPath = GetString(command.Parameters, "localPath");
            if (!IsSafeHost(host)) return CommandResult.Failed("invalid host. Use user@host or host, without spaces or shell characters.");
            if (!IsSafeRemotePath(remotePath)) return CommandResult.Failed("invalid remotePath");
            if (string.IsNullOrWhiteSpace(localPath)) localPath = outputDir;
            localPath = Path.GetFullPath(localPath);
            try
            {
                string dir = Path.HasExtension(localPath) ? Path.GetDirectoryName(localPath) : localPath;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception ex) { return CommandResult.Failed(ex.Message); }

            var args = "-o BatchMode=yes -o ConnectTimeout=10 " + Quote(host + ":" + remotePath) + " " + Quote(localPath);
            var result = RunProcess("scp", args, "", TimeoutMs);
            string output = BuildRemoteOutput("scp pull " + host + ":" + remotePath, result);
            string logPath = WriteOutput("remote-pull", output);
            return result.Ok
                ? CommandResult.Ok(logPath, false, null, "remote-log", Encoding.UTF8.GetByteCount(output), output)
                : CommandResult.Failed(output);
        }

        CommandResult CopyToRemote(IAgentCommand command)
        {
            string host = GetString(command.Parameters, "host");
            string localPath = GetString(command.Parameters, "localPath");
            string remotePath = GetString(command.Parameters, "remotePath");
            if (!IsSafeHost(host)) return CommandResult.Failed("invalid host. Use user@host or host, without spaces or shell characters.");
            if (!File.Exists(localPath)) return CommandResult.Failed("localPath does not exist: " + localPath);
            if (!IsSafeRemotePath(remotePath)) return CommandResult.Failed("invalid remotePath");

            var args = "-o BatchMode=yes -o ConnectTimeout=10 " + Quote(Path.GetFullPath(localPath)) + " " + Quote(host + ":" + remotePath);
            var result = RunProcess("scp", args, "", TimeoutMs);
            string output = BuildRemoteOutput("scp push " + localPath + " -> " + host + ":" + remotePath, result);
            string logPath = WriteOutput("remote-push", output);
            return result.Ok
                ? CommandResult.Ok(logPath, false, null, "remote-log", Encoding.UTF8.GetByteCount(output), output)
                : CommandResult.Failed(output);
        }

        ProcessResult RunProcess(string fileName, string arguments, string workingDirectory, int timeoutMs)
        {
            var result = new ProcessResult();
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
                    if (!proc.WaitForExit(timeoutMs))
                    {
                        try { proc.Kill(); }
                        catch (Exception ex) { Debug.WriteLine("RemoteHostExecutor kill: " + ex.Message); }
                        result.ExitCode = -3;
                        result.Stderr = "remote command timed out after " + timeoutMs + " ms";
                        return result;
                    }
                    result.Stdout = outTask.Result;
                    result.Stderr = errTask.Result;
                    result.ExitCode = proc.ExitCode;
                    result.Ok = proc.ExitCode == 0 || (fileName == "ssh" && arguments == "-V" && !string.IsNullOrWhiteSpace(result.Stderr));
                }
            }
            catch (Exception ex)
            {
                result.ExitCode = -1;
                result.Stderr = ex.Message;
            }
            return result;
        }

        string WriteOutput(string prefix, string text)
        {
            Directory.CreateDirectory(outputDir);
            string path = Path.Combine(outputDir, prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
            File.WriteAllText(path, text ?? "", Encoding.UTF8);
            return path;
        }

        string BuildRemoteOutput(string title, ProcessResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine("exitCode: " + result.ExitCode);
            if (!string.IsNullOrWhiteSpace(result.Stdout))
            {
                sb.AppendLine();
                sb.AppendLine("[stdout]");
                sb.AppendLine(Trim(result.Stdout));
            }
            if (!string.IsNullOrWhiteSpace(result.Stderr))
            {
                sb.AppendLine();
                sb.AppendLine("[stderr]");
                sb.AppendLine(Trim(result.Stderr));
            }
            return sb.ToString().Trim();
        }

        string Trim(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Trim();
            if (text.Length <= MaxOutputChars) return text;
            return text.Substring(0, MaxOutputChars) + "\n... (truncated)";
        }

        static string FirstNonEmpty(string a, string b)
        {
            if (!string.IsNullOrWhiteSpace(a)) return a.Trim();
            if (!string.IsNullOrWhiteSpace(b)) return b.Trim();
            return "";
        }

        static bool IsSafeHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            if (host.IndexOfAny(new[] { ' ', '\t', '\r', '\n', ';', '&', '|', '<', '>', '`', '$' }) >= 0) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(host, @"^[A-Za-z0-9_.@:-]+$");
        }

        static bool IsSafeRemotePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.IndexOfAny(new[] { '\r', '\n', ';', '&', '|', '<', '>', '`', '$' }) >= 0) return false;
            return true;
        }

        static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        static string GetString(IReadOnlyDictionary<string, object> values, string key)
        {
            object value;
            if (values != null && values.TryGetValue(key, out value) && value != null)
                return Convert.ToString(value);
            return "";
        }

        sealed class ProcessResult
        {
            public bool Ok;
            public int ExitCode;
            public string Stdout = "";
            public string Stderr = "";
        }
    }
}
