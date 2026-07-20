using System;
using System.Collections.Generic;
using System.IO;
using ZhuaQianDesktopApp.Plugins;
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

        // roadmap 1.4: optional trust store (enforces manifest signature at run time)
        // and per-run capability consent callback. Both default to null = unchanged
        // behavior (no trust check, no prompt).
        public PluginTrustStore TrustStore { get; set; }
        public Func<PluginManifest, bool> CapabilityConfirm { get; set; }

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

            // roadmap 1.4: when a manifest sits next to the plugin, enforce its
            // signature (via TrustStore) and ask for per-run capability consent.
            string manifestPath = FindManifest(path);
            if (manifestPath != null)
            {
                var pres = new PluginManifestParser(TrustStore).ParseFromFile(manifestPath);
                if (!pres.Success)
                    return CommandResult.Failed("Plugin manifest rejected: " + string.Join("; ", pres.Errors));
                if (CapabilityConfirm != null && !CapabilityConfirm(pres.Manifest))
                    return CommandResult.Failed("Plugin capabilities were not approved by the user.");
            }

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

        // Locates a manifest beside the plugin: "<plugin>.json" preferred, then a
        // shared "manifest.json" in the same directory.
        static string FindManifest(string pluginPath)
        {
            if (string.IsNullOrWhiteSpace(pluginPath)) return null;
            try
            {
                string dir = Path.GetDirectoryName(pluginPath);
                if (string.IsNullOrEmpty(dir)) return null;
                string baseName = Path.GetFileNameWithoutExtension(pluginPath);
                string sidecar = Path.Combine(dir, baseName + ".json");
                if (File.Exists(sidecar)) return sidecar;
                string shared = Path.Combine(dir, "manifest.json");
                if (File.Exists(shared)) return shared;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("PluginRunExecutor.FindManifest: " + ex.Message); }
            return null;
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
