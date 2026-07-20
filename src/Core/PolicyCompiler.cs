using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp.Core
{
    // Roadmap 2.4 strategy-interpretation layer (MVP): compiles plain-language
    // permission requests into a list of IPermissionRule that can be registered
    // on PermissionGate.AddRule. This is the concrete "policy compiler" the
    // roadmap calls for -- natural language in, data-driven rules out, no code
    // change per new policy. Deterministic keyword matching; an injectable clock
    // makes time-based rules unit-testable.
    public static class PolicyCompiler
    {
        public delegate DateTime NowProvider();
        public static DateTime DefaultNow() { return DateTime.Now; }

        // Returns the compiled rules and appends human-readable notes (one per
        // recognized clause) so a UI can echo "I understood your policy as: ...".
        public static List<IPermissionRule> Compile(string text, NowProvider clock, List<string> notes)
        {
            var rules = new List<IPermissionRule>();
            if (notes == null) notes = new List<string>();
            if (clock == null) clock = DefaultNow;
            if (string.IsNullOrWhiteSpace(text)) return rules;

            string lower = text.ToLowerInvariant();

            // 1) "don't delete files larger than X MB" / "不要删除超过 X MB 的文件"
            if (lower.Contains("delete") || lower.Contains("删除") || lower.Contains("del"))
            {
                double mb = ExtractMegabytes(lower);
                if (mb > 0)
                {
                    double bytes = mb * 1024.0 * 1024.0;
                    rules.Add(new DelegatePermissionRule((action, target) =>
                    {
                        if (action != "permFileMoveDelete") return null;
                        if (TryFileSize(target) > bytes) return PermissionDecision.Deny;
                        return null;
                    }));
                    notes.Add("Deny deleting files larger than " + mb.ToString() + " MB.");
                }
            }

            // 2) "no network after 22:00" / "晚上10点后不要联网"
            if (lower.Contains("network") || lower.Contains("联网") || lower.Contains("upload")
                || lower.Contains("网络") || lower.Contains("internet"))
            {
                int hour = ExtractHour(lower);
                if (hour >= 0)
                {
                    int limit = hour;
                    rules.Add(new DelegatePermissionRule((action, target) =>
                    {
                        if (action != "permNetworkUpload") return null;
                        if (clock().Hour >= limit) return PermissionDecision.Deny;
                        return null;
                    }));
                    notes.Add("Deny network/upload after " + limit.ToString() + ":00.");
                }
            }

            // 3) "don't write to system directory" / "不要写入系统目录"
            if ((lower.Contains("system") && (lower.Contains("writ") || lower.Contains("写") || lower.Contains("目录") || lower.Contains("dir")))
                || lower.Contains("写入系统") || lower.Contains("写系统目录"))
            {
                string[] sysDirs = SystemDirs();
                rules.Add(new DelegatePermissionRule((action, target) =>
                {
                    if (action != "permFileWrite" && action != "permFileMoveDelete") return null;
                    if (string.IsNullOrWhiteSpace(target)) return null;
                    string t = Path.GetFullPath(target);
                    foreach (var d in sysDirs)
                        if (t.StartsWith(d, StringComparison.OrdinalIgnoreCase)) return PermissionDecision.Deny;
                    return null;
                }));
                notes.Add("Deny file writes/deletes under system directories.");
            }

            // 4) explicit "forbid/disable <action>" keyword clauses.
            //    e.g. "禁止运行插件" / "disable plugin runs"
            var verbMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "plugin", "permPluginRun" },
                { "插件", "permPluginRun" },
                { "remote", "permNetworkUpload" },
                { "远程", "permNetworkUpload" },
                { "ssh", "permNetworkUpload" }
            };
            foreach (var kv in verbMap)
            {
                if (lower.Contains("forbid " + kv.Key) || lower.Contains("disable " + kv.Key)
                    || lower.Contains("禁止" + kv.Key) || lower.Contains("禁止 " + kv.Key)
                    || lower.Contains("不允许" + kv.Key) || lower.Contains("deny " + kv.Key))
                {
                    string perm = kv.Value;
                    rules.Add(new DelegatePermissionRule((action, target) =>
                        action == perm ? (PermissionDecision?)PermissionDecision.Deny : null));
                    notes.Add("Deny " + kv.Key + " actions (" + perm + ").");
                }
            }

            return rules;
        }

        public static List<IPermissionRule> Compile(string text)
        {
            return Compile(text, null, new List<string>());
        }

        static string[] SystemDirs()
        {
            var list = new List<string>();
            try { list.Add(Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows))); } catch (Exception) { /* best-effort: skip unavailable special folder */ }
            try { list.Add(Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))); } catch (Exception) { /* best-effort: skip unavailable special folder */ }
            try { list.Add(Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.System))); } catch (Exception) { /* best-effort: skip unavailable special folder */ }
            return list.ToArray();
        }

        static long TryFileSize(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return 0;
            try { var fi = new FileInfo(target); return fi.Exists ? fi.Length : 0; }
            catch { return 0; }
        }

        static double ExtractMegabytes(string lower)
        {
            // "超过 10 mb", "larger than 10mb", "10兆"
            var m = Regex.Match(lower, @"(?:超过|larger than|bigger than|>)\s*(\d+(?:\.\d+)?)\s*(mb|m|兆)");
            if (m.Success) { double v; if (double.TryParse(m.Groups[1].Value, out v)) return v; }
            return 0;
        }

        static int ExtractHour(string lower)
        {
            // "晚上10点后", "10点之后", "22:00 后", "after 10pm", "after 22"
            var m = Regex.Match(lower, @"(?:晚上|晚间)?\s*(\d{1,2})\s*(?:点|時|pm)?\s*(?:之後|之后|后|after)");
            if (!m.Success) m = Regex.Match(lower, @"(?:after\s+)(\d{1,2})\s*(?:pm)?");
            if (m.Success)
            {
                int v;
                if (int.TryParse(m.Groups[1].Value, out v))
                {
                    if (lower.Contains("pm") && v < 12) v += 12;
                    if (v >= 0 && v <= 23) return v;
                }
            }
            return -1;
        }
    }
}
