using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace ZhuaQianDesktopApp.Core
{
    // Three-tier permission model (mirrors OpenCode's allow/ask/deny) plus:
    //  - glob pattern matching ("allow *.ps1", "deny *.exe")
    //  - a session remember-cache for "allow once" / "deny once"
    //  - persistent "remember always" rules (stored as patterns)
    //  - auto mode (Ask -> Allow, Deny still denied)
    //  - external_directory scope (file actions outside allowed dirs escalate to Ask)
    public enum PermissionLevel
    {
        Allow,
        Ask,
        Deny
    }

    public enum PermissionDecision
    {
        Allow,
        Ask,
        Deny
    }

    public class PermissionGate
    {
        // Base level per action ("permFileWrite" -> Allow/Ask/Deny).
        readonly Dictionary<string, PermissionLevel> perms =
            new Dictionary<string, PermissionLevel>(StringComparer.OrdinalIgnoreCase);

        // Persistent remember-always rules: action -> (glob -> level).
        readonly List<KeyValuePair<string, KeyValuePair<string, PermissionLevel>>> patterns =
            new List<KeyValuePair<string, KeyValuePair<string, PermissionLevel>>>();

        // Session-only "once" overrides (never serialized).
        readonly List<KeyValuePair<string, string>> sessionAllow = new List<KeyValuePair<string, string>>();
        readonly List<KeyValuePair<string, string>> sessionDeny = new List<KeyValuePair<string, string>>();

        // Auto mode: an Ask decision is treated as Allow (Deny still denied).
        public bool AutoMode;

        // External directory scope. Empty = no restriction (all paths allowed).
        public List<string> AllowedDirectories = new List<string>();

        // Actions that operate on local file paths, for external-dir escalation.
        static readonly HashSet<string> FileActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "permFileRead", "permFileWrite", "permFileMoveDelete", "permPluginRun"
        };

        public void Set(string action, PermissionLevel level)
        {
            perms[action] = level;
        }

        public PermissionLevel Get(string action)
        {
            PermissionLevel v;
            if (perms.TryGetValue(action, out v)) return v;
            return PermissionLevel.Ask;
        }

        // Persistent remember-always: future matches of (action, glob) use level.
        public void Remember(string action, string glob, PermissionLevel level)
        {
            patterns.Add(new KeyValuePair<string, KeyValuePair<string, PermissionLevel>>(
                action, new KeyValuePair<string, PermissionLevel>(glob, level)));
        }

        // Compatibility alias kept for the lightweight test harness and older callers.
        public void SetPattern(string action, string glob, PermissionLevel level)
        {
            Remember(action, glob, level);
        }

        public void RememberAllow(string action, string target)
        {
            Remember(action, target ?? "*", PermissionLevel.Allow);
        }

        public void RememberDeny(string action, string target)
        {
            Remember(action, target ?? "*", PermissionLevel.Deny);
        }

        // Session-only "once": applies only for this run, not saved.
        public void AllowOnce(string action, string target)
        {
            sessionAllow.Add(new KeyValuePair<string, string>(action, target ?? "*"));
        }

        public void DenyOnce(string action, string target)
        {
            sessionDeny.Add(new KeyValuePair<string, string>(action, target ?? "*"));
        }

        public void ClearSession()
        {
            sessionAllow.Clear();
            sessionDeny.Clear();
        }

        public PermissionDecision Check(string action, string target)
        {
            string t = target ?? "";

            // 1) explicit session deny wins.
            foreach (var d in sessionDeny)
                if (SameAction(d.Key, action) && MatchGlob(d.Value, t))
                    return PermissionDecision.Deny;

            // 2) session allow (once).
            foreach (var a in sessionAllow)
                if (SameAction(a.Key, action) && MatchGlob(a.Value, t))
                    return PermissionDecision.Allow;

            // 3) persistent remember-always patterns (stronger than base level).
            foreach (var p in patterns)
                if (SameAction(p.Key, action) && MatchGlob(p.Value.Key, t))
                    return ToDecision(p.Value.Value);

            // 4) base level for the action.
            PermissionDecision decision = ToDecision(Get(action));

            // 5) auto mode upgrades Ask -> Allow (Deny untouched).
            if (AutoMode && decision == PermissionDecision.Ask)
                decision = PermissionDecision.Allow;

            // 6) external directory: a file action writing outside allowed dirs
            //    escalates Allow -> Ask (never downgrades Deny/Ask).
            if (PermissionGate.FileActions.Contains(action) && decision == PermissionDecision.Allow && !IsWithinAllowedDirectories(t))
                decision = PermissionDecision.Ask;

            return decision;
        }

        static bool SameAction(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        static PermissionDecision ToDecision(PermissionLevel level)
        {
            if (level == PermissionLevel.Allow) return PermissionDecision.Allow;
            if (level == PermissionLevel.Deny) return PermissionDecision.Deny;
            return PermissionDecision.Ask;
        }

        static bool MatchGlob(string glob, string text)
        {
            if (string.IsNullOrEmpty(glob) || glob == "*") return true;
            string[] parts = glob.Split('*');
            int idx = 0;
            bool first = true;
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                int found = text.IndexOf(part, idx, StringComparison.OrdinalIgnoreCase);
                if (found < 0) return false;
                if (first && !glob.StartsWith("*") && found != 0) return false;
                idx = found + part.Length;
                first = false;
            }
            if (!glob.EndsWith("*") && idx != text.Length) return false;
            return true;
        }

        public bool IsWithinAllowedDirectories(string path)
        {
            if (AllowedDirectories.Count == 0) return true;
            if (string.IsNullOrWhiteSpace(path)) return false;
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("PermissionGate.IsWithinAllowedDirectories path resolve: " + _ex.Message); return false; }
            foreach (var d in AllowedDirectories)
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                string fd;
                try { fd = Path.GetFullPath(d); }
                catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("PermissionGate allowed dir resolve: " + _ex.Message); continue; }
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                fd = fd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(full, fd, StringComparison.OrdinalIgnoreCase)) return true;
                if (full.StartsWith(fd + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
                if (full.StartsWith(fd + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public string ToJson()
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
            var obj = new Dictionary<string, object>();
            var permObj = new Dictionary<string, object>();
            foreach (var kv in perms) permObj[kv.Key] = kv.Value.ToString();
            obj["permissions"] = permObj;

            var patList = new List<object>();
            foreach (var p in patterns)
            {
                patList.Add(new Dictionary<string, object> {
                    { "action", p.Key },
                    { "glob", p.Value.Key },
                    { "level", p.Value.Value.ToString() }
                });
            }
            obj["patterns"] = patList;
            obj["autoMode"] = AutoMode;

            var dirList = new List<object>();
            foreach (var d in AllowedDirectories) dirList.Add(d);
            obj["allowedDirectories"] = dirList;

            return ser.Serialize(obj);
        }

        public static PermissionGate FromJson(string json)
        {
            var gate = new PermissionGate();
            if (string.IsNullOrWhiteSpace(json)) return gate;
            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
                var obj = ser.DeserializeObject(json) as Dictionary<string, object>;
                if (obj == null) return gate;
                if (obj.ContainsKey("permissions"))
                {
                    var permObj = obj["permissions"] as Dictionary<string, object>;
                    if (permObj != null)
                        foreach (var kv in permObj)
                            gate.perms[kv.Key] = ParseLevel(Convert.ToString(kv.Value));
                }
                if (obj.ContainsKey("patterns"))
                {
                    var patList = obj["patterns"] as System.Collections.IList;
                    if (patList != null)
                        foreach (var item in patList)
                        {
                            var p = item as Dictionary<string, object>;
                            if (p == null) continue;
                            string action = Convert.ToString(p["action"]);
                            string glob = Convert.ToString(p["glob"]);
                            PermissionLevel level = ParseLevel(Convert.ToString(p["level"]));
                            gate.patterns.Add(new KeyValuePair<string, KeyValuePair<string, PermissionLevel>>(
                                action, new KeyValuePair<string, PermissionLevel>(glob, level)));
                        }
                }
                if (obj.ContainsKey("autoMode"))
                    gate.AutoMode = Convert.ToString(obj["autoMode"]).ToLowerInvariant() == "true";
                if (obj.ContainsKey("allowedDirectories"))
                {
                    var dirList = obj["allowedDirectories"] as System.Collections.IList;
                    if (dirList != null)
                        foreach (var d in dirList)
                            gate.AllowedDirectories.Add(Convert.ToString(d));
                }
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("PermissionGate.FromJson: " + _ex.Message); }
            return gate;
        }

        static PermissionLevel ParseLevel(string s)
        {
            if (string.Equals(s, "Allow", StringComparison.OrdinalIgnoreCase)) return PermissionLevel.Allow;
            if (string.Equals(s, "Deny", StringComparison.OrdinalIgnoreCase)) return PermissionLevel.Deny;
            return PermissionLevel.Ask;
        }
    }
}
