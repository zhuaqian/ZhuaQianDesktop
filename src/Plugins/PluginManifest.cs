using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace ZhuaQianDesktopApp.Plugins
{
    // Kind of entry point a plugin exposes (Epic E1).
    // Serialized by System.Web.Script.Serialization as its underlying int,
    // and parsed back from both the int and the name string (case-insensitive).
    public enum PluginEntryType
    {
        Ps1 = 0,
        Bat = 1,
        Cmd = 2,
        Exe = 3,
        Py = 4,
        Js = 5,
        Other = 6
    }

    // Result of parsing a plugin manifest (Epic E1).
    public sealed class PluginManifestParseResult
    {
        public bool Success;
        public PluginManifest Manifest;
        public readonly List<string> Errors = new List<string>();
    }

    // Simple plugin manifest contract (Epic E1).
    //
    // A plugin is a trusted local script/executable described by a manifest so the
    // runner can surface required permissions, author, and an optional signature
    // before execution. Manifests live next to the plugin entry under the trusted
    // plugin folder. The parser validates id, known permissions, known hooks, and
    // guards against path traversal in the entry path.
    public class PluginManifest
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }

        // Relative path (from the manifest) to the script/exe that runs the plugin.
        public string Entry { get; set; }

        // "ps1" | "bat" | "cmd" | "exe" | "py" | "js" — tells the runner how to launch it.
        public PluginEntryType EntryType { get; set; }

        // Permission keys the plugin needs, e.g. "permFileWrite", "permProcessManage".
        public List<string> RequiredPermissions { get; set; } = new List<string>();

        // Hook kinds the plugin wants to observe, e.g. "AfterCommand".
        public List<string> Hooks { get; set; } = new List<string>();

        // Minimum app version that understands this manifest (optional).
        public string MinAppVersion { get; set; }

        // Optional trusted signature; empty for unsigned (untrusted) plugins.
        public string Signature { get; set; }

        // True when the plugin ships from a signed/trusted source (Epic E4).
        public bool Trusted { get; set; }

        public static PluginManifest FromJson(string json)
        {
            var ser = new JavaScriptSerializer();
            return ser.Deserialize<PluginManifest>(json ?? "{}");
        }

        public string ToJson()
        {
            var ser = new JavaScriptSerializer();
            return ser.Serialize(this);
        }
    }

    // Validating parser for plugin manifests (Epic E1).
    //
    // Known sets keep manifests honest: an unknown permission or hook name is
    // rejected, and an entry path that escapes the plugin folder (absolute path or
    // ".." segments) is rejected. The parser never throws on bad input; it returns
    // a result with Success=false and a list of human-readable errors.
    public class PluginManifestParser
    {
        static readonly HashSet<string> KnownPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "permFileWrite", "permFileRead", "permFileMoveDelete",
            "permProcessManage", "permProcessList",
            "permNetwork", "permShell", "permAutomationInput", "permPluginRun",
            "permDiagnostics", "permSystemInfo"
        };

        static readonly HashSet<string> KnownHooks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BeforeModelCall", "AfterModelCall", "BeforeCommand", "AfterCommand", "BeforeFileWrite"
        };

        public PluginManifestParseResult ParseFromFile(string path)
        {
            try { return ParseFromString(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                return new PluginManifestParseResult { Success = false, Errors = new List<string> { "read failed: " + ex.Message } };
            }
        }

        public PluginManifestParseResult ParseFromString(string json)
        {
            var result = new PluginManifestParseResult();
            if (string.IsNullOrWhiteSpace(json))
            {
                result.Errors.Add("empty manifest");
                return result;
            }

            PluginManifest m;
            try { m = PluginManifest.FromJson(json); }
            catch (Exception ex)
            {
                result.Errors.Add("invalid json: " + ex.Message);
                return result;
            }
            result.Manifest = m;

            if (string.IsNullOrWhiteSpace(m.Id)) result.Errors.Add("missing id");
            if (string.IsNullOrWhiteSpace(m.Name)) result.Errors.Add("missing name");

            if (m.EntryType == PluginEntryType.Other) result.Errors.Add("unknown entryType: " + m.EntryType);

            if (m.RequiredPermissions != null)
                foreach (var p in m.RequiredPermissions)
                    if (!KnownPermissions.Contains(p)) result.Errors.Add("unknown permission: " + p);

            if (m.Hooks != null)
                foreach (var h in m.Hooks)
                    if (!KnownHooks.Contains(h)) result.Errors.Add("unknown hook: " + h);

            if (!string.IsNullOrWhiteSpace(m.Entry) && IsPathTraversal(m.Entry))
                result.Errors.Add("entry path escapes plugin folder");

            result.Success = result.Errors.Count == 0;
            return result;
        }

        static bool IsPathTraversal(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry)) return false;
            if (Path.IsPathRooted(entry)) return true;
            var parts = entry.Split(new[] { '\\', '/' }, StringSplitOptions.None);
            foreach (var p in parts)
                if (p == ".." || p == ".") return true;
            return false;
        }
    }
}
