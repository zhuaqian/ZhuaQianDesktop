using System;
using System.Collections.Generic;
using ZhuaQianDesktopApp.Tools;

namespace ZhuaQianDesktopApp.Agent
{
    public sealed class PluginRunExecutor : ICommandExecutor
    {
        readonly string trustedPluginDir;
        readonly string outputDir;
        readonly bool allowAdvancedPlugins;
        readonly int timeoutMs;
        readonly int maxOutputChars;

        public PluginRunExecutor(string trustedPluginDir, bool allowAdvancedPlugins, int timeoutMs, int maxOutputChars)
            : this(trustedPluginDir, allowAdvancedPlugins, timeoutMs, maxOutputChars, null)
        {
        }

        public PluginRunExecutor(string trustedPluginDir, bool allowAdvancedPlugins, int timeoutMs, int maxOutputChars, string outputDir)
        {
            this.trustedPluginDir = trustedPluginDir ?? "";
            this.outputDir = string.IsNullOrWhiteSpace(outputDir) ? this.trustedPluginDir : outputDir;
            this.allowAdvancedPlugins = allowAdvancedPlugins;
            this.timeoutMs = timeoutMs;
            this.maxOutputChars = maxOutputChars;
        }

        public string CommandType { get { return "RunPlugin"; } }

        public CommandResult Execute(IAgentCommand command)
        {
            string path = command.Target;
            if (string.IsNullOrWhiteSpace(path))
                path = GetString(command.Parameters, "path");
            string stdin = GetString(command.Parameters, "stdin");
            string arguments = GetString(command.Parameters, "arguments");

            var runner = new PluginRunner(trustedPluginDir)
            {
                AllowAdvancedPlugins = allowAdvancedPlugins,
                MaxOutputChars = maxOutputChars
            };

            string validation = runner.Validate(path);
            if (!string.IsNullOrEmpty(validation))
                return CommandResult.Failed(validation);

            var result = runner.Run(path, arguments, timeoutMs, stdin);
            if (result.TimedOut)
                return CommandResult.Failed("Plugin timed out after " + (timeoutMs / 1000).ToString() + " seconds.");
            if (!string.IsNullOrEmpty(result.Error))
                return CommandResult.Failed(result.Error);
            if (result.ExitCode != 0)
                return CommandResult.Failed("Plugin exited with code " + result.ExitCode.ToString() + ".\r\n" + Trim(result.StandardError, maxOutputChars));

            string output = result.StandardOutput ?? "";
            if (!string.IsNullOrWhiteSpace(result.StandardError))
                output += "\r\n[stderr]\r\n" + result.StandardError;

            output = Trim(output, maxOutputChars);
            string outputPath = WritePluginOutput(path, output);
            int size = 0;
            try
            {
                var info = new System.IO.FileInfo(outputPath);
                if (info.Exists && info.Length <= int.MaxValue) size = (int)info.Length;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("PluginRunExecutor output size: " + ex.Message);
            }
            return CommandResult.Ok(outputPath, false, null, "plugin-log", size, output);
        }

        static string GetString(IReadOnlyDictionary<string, object> values, string key)
        {
            object value;
            if (values != null && values.TryGetValue(key, out value) && value != null)
                return Convert.ToString(value);
            return "";
        }

        static string Trim(string text, int maxChars)
        {
            if (text == null) return "";
            if (maxChars <= 0 || text.Length <= maxChars) return text;
            return text.Substring(0, maxChars) + "\r\n[content truncated]";
        }

        string WritePluginOutput(string pluginPath, string output)
        {
            string dir = outputDir;
            if (string.IsNullOrWhiteSpace(dir)) dir = System.IO.Path.GetTempPath();
            System.IO.Directory.CreateDirectory(dir);
            string name = "plugin-" + System.IO.Path.GetFileNameWithoutExtension(pluginPath) + "-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" +
                Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt";
            string path = System.IO.Path.Combine(dir, name);
            System.IO.File.WriteAllText(path, output ?? "", System.Text.Encoding.UTF8);
            return path;
        }
    }
}
