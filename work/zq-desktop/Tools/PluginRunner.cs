using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ZhuaQianDesktopApp.Tools
{
    // Runs a plugin script from the trusted plugin folder. Default-safe extensions
    // are .py and .ps1; high-risk extensions (.exe/.bat/.cmd) require an explicit
    // advanced flag. Callers must pass a PermissionGate and confirm via the
    // Approval Card before invoking Run.
    // Spec: docs/CURRENT_GAPS_ASSESSMENT.md (plugin safety) and CODE_COMPLETION_ALIGNMENT.md.
    public class PluginRunner
    {
        readonly string trustedPluginDir;
        public bool AllowAdvancedPlugins { get { return _AllowAdvancedPlugins; } set { _AllowAdvancedPlugins = value; } }
        bool _AllowAdvancedPlugins;
        public int MaxOutputChars = 20000;

        static readonly HashSet<string> SafeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".py", ".ps1"
        };

        static readonly HashSet<string> AdvancedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".bat", ".cmd"
        };

        public PluginRunner(string trustedPluginDir)
        {
            this.trustedPluginDir = trustedPluginDir ?? "";
        }

        // Validates that the plugin path is inside the trusted folder and uses an
        // allowed extension. Returns an error string, or "" when allowed.
        public string Validate(string pluginPath)
        {
            if (string.IsNullOrWhiteSpace(pluginPath))
                return "Plugin path is empty.";
            if (!File.Exists(pluginPath))
                return "Plugin file does not exist: " + pluginPath;
            if (string.IsNullOrWhiteSpace(trustedPluginDir) || !Directory.Exists(trustedPluginDir))
                return "Trusted plugin folder is not configured.";
            try
            {
                string trustedRoot = Path.GetFullPath(trustedPluginDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                string fullPluginPath = Path.GetFullPath(pluginPath);
                if (!fullPluginPath.StartsWith(trustedRoot, StringComparison.OrdinalIgnoreCase))
                    return "Plugin must be inside trusted plugin folder: " + trustedPluginDir;
            }
            catch (Exception ex)
            {
                return "Plugin path validation failed: " + ex.Message;
            }

            string ext = Path.GetExtension(pluginPath);
            if (SafeExtensions.Contains(ext))
                return "";

            if (AdvancedExtensions.Contains(ext))
            {
                if (!AllowAdvancedPlugins)
                    return "Advanced plugin extension blocked (enable advanced plugins): " + ext;
                return "";
            }

            return "Plugin extension not allowed: " + (string.IsNullOrEmpty(ext) ? "(none)" : ext);
        }

        // Executes the plugin and returns its combined stdout/stderr. Call Validate()
        // and obtain permission approval before calling this.
        // stdin: optional text to write to the plugin's standard input.
        public PluginResult Run(string pluginPath, string arguments, int timeoutMs = 60000, string stdin = null)
        {
            var result = new PluginResult();
            try
            {
                string ext = Path.GetExtension(pluginPath).ToLowerInvariant();
                string fileName = pluginPath;
                string args = arguments ?? "";
                if (ext == ".py") { fileName = "python"; args = "\"" + pluginPath + "\" " + args; }
                else if (ext == ".ps1") { fileName = "powershell"; args = "-NoProfile -ExecutionPolicy Bypass -File \"" + pluginPath + "\" " + args; }

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardInput = !string.IsNullOrEmpty(stdin),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var proc = new Process { StartInfo = psi, EnableRaisingEvents = true })
                {
                    var stdout = new StringBuilder();
                    var stderr = new StringBuilder();
                    proc.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                    proc.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                    proc.Start();
                    if (!string.IsNullOrEmpty(stdin))
                    {
                        proc.StandardInput.Write(stdin);
                        proc.StandardInput.Close();
                    }
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    if (!proc.WaitForExit(timeoutMs))
                    {
                        try { proc.Kill(); } catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("PluginRunner kill: " + _ex.Message); }
                        result.TimedOut = true;
                        try { proc.WaitForExit(); } catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("PluginRunner wait after kill: " + _ex.Message); }
                        result.StandardOutput = stdout.ToString();
                        result.StandardError = stderr.ToString();
                        return result;
                    }
                    try { proc.WaitForExit(); } catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("PluginRunner output flush wait: " + _ex.Message); }
                    result.ExitCode = proc.ExitCode;
                    result.StandardOutput = stdout.ToString();
                    result.StandardError = stderr.ToString();
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            return result;
        }
    }

    public class PluginResult
    {
        public int ExitCode;
        public string StandardOutput = "";
        public string StandardError = "";
        public string Error = "";
        public bool TimedOut;
        public bool Success { get { return string.IsNullOrEmpty(Error) && !TimedOut && ExitCode == 0; } }
    }
}
