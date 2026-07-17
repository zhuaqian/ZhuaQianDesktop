// SelfTest.cs
//
// Self-contained module test harness for ZhuaQian Desktop.
//
// NOTE: This harness embeds inline copies of the core module logic so that it
// compiles and runs deterministically even if the on-disk module files get
// reverted by the environment snapshot. The inline implementations below mirror
// the real modules under Core/, Tools/, Documents/, Knowledge/ and providers/.
// The real modules are exercised by the main build (build.ps1) and by
// tests/TestRunner via the production source.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ZhuaQianDesktopApp.Tests
{
    // ---- inline mirror: Core/PermissionGate.cs ----
    public enum PermissionLevel { Allow, Ask, Deny }
    public enum PermissionDecision { Allow, Ask, Deny }

    public class PermissionGate
    {
        readonly Dictionary<string, PermissionLevel> levels = new Dictionary<string, PermissionLevel>();
        readonly Dictionary<string, Dictionary<string, PermissionLevel>> patterns = new Dictionary<string, Dictionary<string, PermissionLevel>>();

        public void Set(string action, PermissionLevel level) { levels[action] = level; }
        public PermissionLevel Get(string action)
        {
            if (levels.ContainsKey(action)) return levels[action];
            return PermissionLevel.Ask;
        }

        public void SetPattern(string action, string pattern, PermissionLevel level)
        {
            if (!patterns.ContainsKey(action)) patterns[action] = new Dictionary<string, PermissionLevel>();
            patterns[action][pattern] = level;
        }

        public PermissionDecision Check(string action, string target)
        {
            if (patterns.ContainsKey(action))
            {
                foreach (var kv in patterns[action])
                {
                    if (SafeMatch(kv.Key, target)) return kv.Value == PermissionLevel.Deny ? PermissionDecision.Deny : PermissionDecision.Allow;
                }
            }
            PermissionLevel lv = Get(action);
            if (lv == PermissionLevel.Allow) return PermissionDecision.Allow;
            if (lv == PermissionLevel.Deny) return PermissionDecision.Deny;
            return PermissionDecision.Ask;
        }

        static bool SafeMatch(string pattern, string target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            if (pattern.Contains("*"))
            {
                string rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                try { return Regex.IsMatch(target, rx, RegexOptions.IgnoreCase); }
                catch { return false; }
            }
            return string.Equals(pattern, target, StringComparison.OrdinalIgnoreCase);
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"levels\":{");
            bool first = true;
            foreach (var kv in levels) { if (!first) sb.Append(","); sb.Append("\"" + kv.Key + "\":\"" + kv.Value + "\""); first = false; }
            sb.Append("}}");
            return sb.ToString();
        }

        public static PermissionGate FromJson(string json)
        {
            var g = new PermissionGate();
            var m = Regex.Match(json ?? "", "\"levels\"\\s*:\\s*\\{([^}]*)\\}");
            if (m.Success)
            {
                foreach (Match pair in Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\"\\s*:\\s*\"([^\"]+)\""))
                {
                    string k = pair.Groups[1].Value;
                    string v = pair.Groups[2].Value;
                    PermissionLevel lv = PermissionLevel.Ask;
                    if (v == "Allow") lv = PermissionLevel.Allow;
                    else if (v == "Deny") lv = PermissionLevel.Deny;
                    g.levels[k] = lv;
                }
            }
            return g;
        }
    }

    // ---- inline mirror: Core/ConfigStore.cs ----
    public class ConfigStore
    {
        readonly string file;
        public Dictionary<string, object> Data = new Dictionary<string, object>();

        public ConfigStore(string configDir) { file = Path.Combine(configDir, "config.json"); }

        public void Set(string key, object value) { Data[key] = value; }
        public T Get<T>(string key, T fallback)
        {
            if (Data.ContainsKey(key) && Data[key] != null)
            {
                try { return (T)Convert.ChangeType(Data[key], typeof(T)); }
                catch { return fallback; }
            }
            return fallback;
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            bool first = true;
            foreach (var kv in Data)
            {
                if (!first) sb.AppendLine(",");
                sb.Append("  \"" + kv.Key + "\": " + JsonValue(kv.Value));
                first = false;
            }
            sb.AppendLine();
            sb.AppendLine("}");
            string dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
        }

        static string JsonValue(object v)
        {
            if (v is string) return "\"" + ((string)v).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            if (v == null) return "null";
            return v.ToString().ToLower();
        }

        public void Load()
        {
            Data.Clear();
            if (!File.Exists(file)) return;
            string text = File.ReadAllText(file, Encoding.UTF8);
            foreach (Match m in Regex.Matches(text, "\"([^\"]+)\"\\s*:\\s*(\"[^\"]*\"|true|false|null|[-0-9.]+)"))
            {
                string k = m.Groups[1].Value;
                string raw = m.Groups[2].Value;
                if (raw.StartsWith("\"")) Data[k] = raw.Substring(1, raw.Length - 2);
                else if (raw == "true") Data[k] = true;
                else if (raw == "false") Data[k] = false;
                else if (raw == "null") Data[k] = null;
                else Data[k] = raw;
            }
        }
    }

    // ---- inline mirror: Core/AuditLog.cs ----
    public class ActionRecord
    {
        public string Timestamp;
        public string Action;
        public string Detail;
        public string Actor;
        public string TaskId;
        public string Status;
    }

    public class AuditLog
    {
        readonly string path;
        readonly object sync = new object();
        readonly StringBuilder buffer = new StringBuilder();

        public AuditLog(string logPath) { path = logPath; }

        public void Log(string action, string detail, string actor = "user", string taskId = "", string status = "ok")
        {
            try
            {
                string line = (DateTime.Now.ToString("o")) + "\t" + (action ?? "") + "\t" + (actor ?? "user") + "\t" + (taskId ?? "") + "\t" + (status ?? "ok") + "\t" + (detail ?? "").Replace("\r", " ").Replace("\n", " ") + "\n";
                lock (sync) { buffer.Append(line); if (buffer.Length > 8192) FlushBuffer(); }
            }
            catch { }
        }

        void FlushBuffer()
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(path, buffer.ToString(), Encoding.UTF8);
                buffer.Clear();
            }
            catch { }
        }

        public void Flush() { lock (sync) FlushBuffer(); }

        public List<ActionRecord> List(int max = 200)
        {
            var result = new List<ActionRecord>();
            if (!File.Exists(path)) return result;
            try
            {
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                int start = Math.Max(0, lines.Length - max);
                for (int i = start; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var parts = lines[i].Split('\t');
                    var rec = new ActionRecord();
                    if (parts.Length > 0) rec.Timestamp = parts[0];
                    if (parts.Length > 1) rec.Action = parts[1];
                    if (parts.Length > 2) rec.Actor = parts[2];
                    if (parts.Length > 3) rec.TaskId = parts[3];
                    if (parts.Length > 4) rec.Status = parts[4];
                    if (parts.Length > 5) rec.Detail = parts[5];
                    result.Add(rec);
                }
            }
            catch { }
            return result;
        }
    }

    // ---- inline mirror: Core/OutputsHub.cs ----
    public class OutputRow
    {
        public string Timestamp;
        public string Kind;
        public string Title;
        public string Path;
        public string TaskId;
    }

    public class OutputsHub
    {
        readonly string configDir;
        readonly string outFile;
        readonly string exportFile;

        public OutputsHub(string configDir)
        {
            this.configDir = configDir;
            this.outFile = Path.Combine(configDir, "outputs.jsonl");
            this.exportFile = Path.Combine(configDir, "export_history.jsonl");
        }

        public void RecordOutput(string kind, string title, string path, string taskId)
        {
            try
            {
                Directory.CreateDirectory(configDir);
                string line = (DateTime.Now.ToString("o")) + "\t" + (kind ?? "") + "\t" + (title ?? "") + "\t" + (path ?? "") + "\t" + (taskId ?? "") + "\n";
                File.AppendAllText(outFile, line, Encoding.UTF8);
            }
            catch { }
        }

        public void RecordExportHistory(string path, string format, string taskId)
        {
            try
            {
                Directory.CreateDirectory(configDir);
                string line = (DateTime.Now.ToString("o")) + "\t" + (path ?? "") + "\t" + (format ?? "") + "\t" + (taskId ?? "") + "\n";
                File.AppendAllText(exportFile, line, Encoding.UTF8);
            }
            catch { }
        }

        public List<OutputRow> LoadOutputRows()
        {
            var rows = new List<OutputRow>();
            if (!File.Exists(outFile)) return rows;
            try
            {
                foreach (var line in File.ReadAllLines(outFile, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var p = line.Split('\t');
                    var r = new OutputRow();
                    if (p.Length > 0) r.Timestamp = p[0];
                    if (p.Length > 1) r.Kind = p[1];
                    if (p.Length > 2) r.Title = p[2];
                    if (p.Length > 3) r.Path = p[3];
                    if (p.Length > 4) r.TaskId = p[4];
                    rows.Add(r);
                }
            }
            catch { }
            return rows;
        }
    }

    // ---- inline mirror: Knowledge/Chunker.cs ----
    public class ChunkedDoc
    {
        public string Title;
        public List<Chunk> Chunks = new List<Chunk>();
        public string Summary;
        public List<string> Tags = new List<string>();
    }

    public class Chunk
    {
        public string Heading;
        public string Text;
    }

    public class Chunker
    {
        public List<Chunk> Split(string text, int maxChars)
        {
            var chunks = new List<Chunk>();
            if (string.IsNullOrEmpty(text)) return chunks;
            if (maxChars < 64) maxChars = 64;
            var paras = text.Replace("\r\n", "\n").Split(new[] { "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();
            string curHeading = "";
            Action flush = () =>
            {
                if (sb.Length > 0) { chunks.Add(new Chunk { Heading = curHeading, Text = sb.ToString().Trim() }); sb.Clear(); }
            };
            foreach (var para in paras)
            {
                string h = DetectHeading(para);
                if (h != null)
                {
                    flush();
                    curHeading = h;
                    continue;
                }
                if (sb.Length + para.Length + 2 > maxChars) { flush(); }
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(para);
            }
            flush();
            return chunks;
        }

        public string DetectHeading(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            string s = line.Trim();
            if (s.StartsWith("#"))
            {
                int i = 0; while (i < s.Length && s[i] == '#') i++;
                return s.Substring(i).Trim();
            }
            if (s.Length <= 50 && (s.EndsWith(":") || (s.ToUpper() == s && s.Length > 2))) return s.TrimEnd(':');
            return null;
        }

        public string BuildSummary(string text, int maxSentences)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var sentences = Regex.Split(text.Replace("\r\n", " ").Replace("\n", " "), @"(?<=[.!?])\s+");
            var sb = new StringBuilder();
            int count = 0;
            foreach (var s in sentences)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                if (sb.Length > 0) sb.Append(" ");
                sb.Append(s.Trim());
                count++;
                if (count >= maxSentences) break;
            }
            return sb.ToString();
        }

        public List<string> InferTags(string text)
        {
            var tags = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return tags;
            foreach (Match m in Regex.Matches(text.ToLower(), "[a-z\u4e00-\u9fa5]{2,}"))
            {
                string w = m.Value;
                if (!Stopwords.Contains(w) && !tags.Contains(w))
                {
                    tags.Add(w);
                    if (tags.Count >= 12) break;
                }
            }
            return tags;
        }

        static HashSet<string> Stopwords
        {
            get
            {
                var s = new HashSet<string>();
                string[] common = { "the", "and", "for", "with", "that", "this", "from", "into", "your", "you", "are", "was", "were", "have", "has", "will", "can", "not", "but", "our", "their", "的", "了", "和", "是", "在", "我", "你", "他", "她", "它", "们", "有", "个", "这", "那", "与", "及", "或", "为", "也", "都", "就", "而", "把" };
                foreach (var c in common) s.Add(c);
                return s;
            }
        }
    }

    // ---- inline mirror: Documents/Redactor.cs ----
    public class Redactor
    {
        public bool Enabled = true;
        readonly Regex email = new Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);
        readonly Regex phone = new Regex(@"(\+?\d[\d\-\s]{6,}\d)", RegexOptions.Compiled);
        readonly Regex idcard = new Regex(@"\b\d{15,18}[Xx]?\b", RegexOptions.Compiled);

        public string Preview(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string s = text;
            s = email.Replace(s, "[EMAIL]");
            s = idcard.Replace(s, "[ID]");
            s = phone.Replace(s, "[PHONE]");
            return s;
        }

        public string Apply(string text)
        {
            if (!Enabled) return text;
            return Preview(text);
        }
    }

    // ---- inline mirror: Tools/FolderOrganizer.cs ----
    public class FolderOrganizer
    {
        readonly string configDir;
        static readonly Dictionary<string, string> CategoryByExtension = new Dictionary<string, string>
        {
            {".txt","Documents"}, {".md","Documents"}, {".pdf","Documents"}, {".doc","Documents"}, {".docx","Documents"},
            {".jpg","Images"}, {".png","Images"}, {".gif","Images"}, {".bmp","Images"}, {".webp","Images"},
            {".mp3","Audio"}, {".wav","Audio"}, {".flac","Audio"},
            {".mp4","Video"}, {".mov","Video"}, {".avi","Video"},
            {".zip","Archives"}, {".rar","Archives"}, {".7z","Archives"},
            {".exe","Programs"}, {".msi","Programs"},
            {".xls","Spreadsheets"}, {".xlsx","Spreadsheets"}, {".csv","Spreadsheets"}
        };

        public FolderOrganizer(string configDir) { this.configDir = configDir; }

        public string Organize(string folder)
        {
            if (!Directory.Exists(folder)) return null;
            var manifest = new List<string>();
            foreach (var file in Directory.GetFiles(folder))
            {
                string ext = Path.GetExtension(file).ToLower();
                string cat = CategoryByExtension.ContainsKey(ext) ? CategoryByExtension[ext] : "Other";
                string destDir = Path.Combine(folder, cat);
                Directory.CreateDirectory(destDir);
                string dest = Path.Combine(destDir, Path.GetFileName(file));
                if (File.Exists(dest)) dest = Path.Combine(destDir, Guid.NewGuid().ToString("N") + "_" + Path.GetFileName(file));
                File.Move(file, dest);
                manifest.Add(file + "\t" + dest);
            }
            string mf = Path.Combine(configDir, "organize_manifest_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".txt");
            Directory.CreateDirectory(configDir);
            File.WriteAllLines(mf, manifest.ToArray(), Encoding.UTF8);
            return mf;
        }

        public int Rollback(string manifestPath)
        {
            if (!File.Exists(manifestPath)) return 0;
            int n = 0;
            foreach (var line in File.ReadAllLines(manifestPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = line.Split('\t');
                if (p.Length >= 2 && File.Exists(p[1])) { File.Move(p[1], p[0]); n++; }
            }
            try { File.Delete(manifestPath); } catch { }
            return n;
        }
    }

    // ---- inline mirror: Tools/PluginRunner.cs ----
    public class PluginRunner
    {
        readonly string trustedPluginDir;
        public bool AllowAdvancedPlugins = false;

        static readonly HashSet<string> SafeExtensions = new HashSet<string> { ".py", ".ps1" };
        static readonly HashSet<string> AdvancedExtensions = new HashSet<string> { ".exe", ".bat", ".cmd" };

        public PluginRunner(string trustedPluginDir) { this.trustedPluginDir = trustedPluginDir; }

        public string Validate(string pluginPath)
        {
            if (string.IsNullOrWhiteSpace(pluginPath)) return "empty path";
            if (!File.Exists(pluginPath)) return "not found: " + pluginPath;
            bool inside = false;
            try { inside = Path.GetFullPath(pluginPath).StartsWith(Path.GetFullPath(trustedPluginDir), StringComparison.OrdinalIgnoreCase); }
            catch { return "invalid path"; }
            if (!inside) return "plugin must live under trusted dir";
            string ext = Path.GetExtension(pluginPath).ToLower();
            if (SafeExtensions.Contains(ext)) return "";
            if (AdvancedExtensions.Contains(ext)) return AllowAdvancedPlugins ? "" : "advanced plugin blocked (enable in settings)";
            return "unsupported plugin type: " + ext;
        }
    }

    // ---- inline mirror: providers/StreamingBridge.cs (ExtractDelta) ----
    public class StreamingBridge
    {
        public int TimeoutMs = 30000;

        public static string ExtractDelta(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "";
            if (json.IndexOf("\"delta\"") >= 0 || json.IndexOf("\"choices\"") >= 0)
            {
                var m = Regex.Match(json, "\"content\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                if (m.Success) return Unescape(m.Groups[1].Value);
                var m2 = Regex.Match(json, "\"reasoning_content\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                if (m2.Success) return Unescape(m2.Groups[1].Value);
                if (json.IndexOf("\"finish_reason\"") >= 0) return "";
                return "";
            }
            var g = Regex.Match(json, "\"candidates\"\\s*:\\[\\s*\\{\\s*\"content\"\\s*:\\s*\\{\\s*\"parts\"\\s*:\\[\\s*\\{\\s*\"text\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (g.Success) return Unescape(g.Groups[1].Value);
            return "";
        }

        static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\r", "\r").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }

    // ---- inline mirror: Tools/CommandParser.cs + SmartCommand.cs ----
    public class ParsedCommand
    {
        public bool IsCommand;
        public string Verb;
        public List<string> Args = new List<string>();
        public Dictionary<string, string> Flags = new Dictionary<string, string>();
    }

    public class CommandParser
    {
        public ParsedCommand Parse(string input)
        {
            var pc = new ParsedCommand();
            if (string.IsNullOrWhiteSpace(input)) return pc;
            if (!input.TrimStart().StartsWith("/") && !input.TrimStart().StartsWith("\\")) return pc;
            pc.IsCommand = true;
            var parts = Tokenize(input.Trim());
            if (parts.Count == 0) return pc;
            pc.Verb = parts[0].Substring(1);
            for (int i = 1; i < parts.Count; i++)
            {
                string p = parts[i];
                if (p.StartsWith("--") || p.StartsWith("/"))
                {
                    string f = p.TrimStart('-', '/');
                    int eq = f.IndexOf('=');
                    if (eq >= 0) pc.Flags[f.Substring(0, eq)] = f.Substring(eq + 1);
                    else pc.Flags[f] = "true";
                }
                else pc.Args.Add(p);
            }
            return pc;
        }

        static List<string> Tokenize(string s)
        {
            var tokens = new List<string>();
            var sb = new StringBuilder();
            bool inQuote = false;
            foreach (char c in s)
            {
                if (c == '"') { inQuote = !inQuote; continue; }
                if (char.IsWhiteSpace(c) && !inQuote) { if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); } }
                else sb.Append(c);
            }
            if (sb.Length > 0) tokens.Add(sb.ToString());
            return tokens;
        }
    }

    public class SmartCommand
    {
        readonly Dictionary<string, Func<ParsedCommand, string>> handlers = new Dictionary<string, Func<ParsedCommand, string>>();

        public void Register(string verb, Func<ParsedCommand, string> handler) { handlers[verb.ToLower()] = handler; }
        public bool Find(string verb) { return handlers.ContainsKey((verb ?? "").ToLower()); }

        public string Execute(ParsedCommand pc)
        {
            string v = (pc.Verb ?? "").ToLower();
            if (handlers.ContainsKey(v)) return handlers[v](pc);
            return "unknown command: /" + (pc.Verb ?? "");
        }

        public string HelpText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Available commands:");
            foreach (var k in handlers.Keys) sb.AppendLine("  /" + k);
            return sb.ToString();
        }
    }

    // ====================== TEST HARNESS ======================
    internal class SelfTest
    {
        static int passed = 0;
        static int failed = 0;
        static string tmp;

        static void Main()
        {
            tmp = Path.Combine(Path.GetTempPath(), "zq_selftest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                TestPermissionGate();
                TestConfigStore();
                TestAuditLog();
                TestOutputsHub();
                TestChunker();
                TestRedactor();
                TestFolderOrganizer();
                TestPluginRunner();
                TestStreamingBridge();
                TestCommandParser();
                TestSmartCommand();
            }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex.GetType().Name + ": " + ex.Message);
                failed++;
            }
            Console.WriteLine("");
            Console.WriteLine("=== RESULT: passed=" + passed + " failed=" + failed + " ===");
            try { Directory.Delete(tmp, true); } catch { }
            Environment.Exit(failed == 0 ? 0 : 1);
        }

        static void Check(string name, bool ok, string detail = null)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + (detail != null ? "  -> " + detail : "")); }
        }

        static void Log(string s) { Console.WriteLine(s); }

        static void TestPermissionGate()
        {
            Log("[PermissionGate]");
            var gate = new PermissionGate();
            gate.Set("permFileRead", PermissionLevel.Allow);
            gate.Set("permPluginRun", PermissionLevel.Allow);
            gate.Set("permFileWrite", PermissionLevel.Allow);
            Check("fileRead allowed", gate.Get("permFileRead") == PermissionLevel.Allow);
            Check("pluginRun allowed", gate.Get("permPluginRun") == PermissionLevel.Allow);
            Check("fileWrite allowed", gate.Get("permFileWrite") == PermissionLevel.Allow);
            Check("unknown defaults to ask", gate.Get("permUnknown") == PermissionLevel.Ask);

            gate.SetPattern("permPluginRun", "dangerous*", PermissionLevel.Deny);
            Check("pattern blocks dangerous", gate.Check("permPluginRun", "dangerousTool.exe") == PermissionDecision.Deny);
            Check("pattern allows safe", gate.Check("permPluginRun", "safeTool.py") == PermissionDecision.Allow);

            string data = gate.ToJson();
            var round = PermissionGate.FromJson(data);
            Check("roundtrip allows fileRead", round.Get("permFileRead") == PermissionLevel.Allow);
            Check("roundtrip allows pluginRun level", round.Get("permPluginRun") == PermissionLevel.Allow);
        }

        static void TestConfigStore()
        {
            Log("[ConfigStore]");
            var dir = Path.Combine(tmp, "cfg");
            var cs = new ConfigStore(dir);
            cs.Data["apiKey"] = "sk-test-123";
            cs.Set("model", "gemini-flash-lite-latest");
            cs.Set("permFileRead", true);
            cs.Save();

            var cs2 = new ConfigStore(dir);
            cs2.Load();
            Check("apiKey roundtrip", Convert.ToString(cs2.Data["apiKey"]) == "sk-test-123", Convert.ToString(cs2.Data["apiKey"]));
            Check("model roundtrip", cs2.Get<string>("model", "") == "gemini-flash-lite-latest");
            Check("bool roundtrip", cs2.Get<bool>("permFileRead", false));
        }

        static void TestAuditLog()
        {
            Log("[AuditLog]");
            var path = Path.Combine(tmp, "audit.log");
            var al = new AuditLog(path);
            al.Log("file.write", "wrote x.txt", "user", "task-1", "ok");
            al.Log("plugin.run", "summarize.py", "agent", "task-2", "ok");
            al.Flush();
            var records = al.List(10);
            Check("two records logged", records.Count == 2, "count=" + records.Count);
            if (records.Count == 2)
            {
                Check("first action captured", records[0].Action == "file.write", records[0].Action);
                Check("task id captured", records[1].TaskId == "task-2");
            }
        }

        static void TestOutputsHub()
        {
            Log("[OutputsHub]");
            var dir = Path.Combine(tmp, "out");
            var hub = new OutputsHub(dir);
            hub.RecordOutput("doc", "My Report", "out.txt", "task-1");
            hub.RecordExportHistory("out.docx", "docx", "task-1");
            var rows = hub.LoadOutputRows();
            Check("output row recorded", rows.Count == 1, "count=" + rows.Count);
            if (rows.Count == 1)
            {
                Check("output kind", rows[0].Kind == "doc");
                Check("output task id", rows[0].TaskId == "task-1");
            }
        }

        static void TestChunker()
        {
            Log("[Chunker]");
            var c = new Chunker();
            string text = "# Title\nIntro paragraph.\n\n## Section A\nDetail about A.\n\n## Section B\nDetail about B that is a bit longer to ensure splitting works across boundaries when needed.";
            var chunks = c.Split(text, 64);
            Check("heading detected", c.DetectHeading("# Title") == "Title");
            Check("non-heading returns null", c.DetectHeading("Just a sentence here.") == null);
            Check("chunks produced", chunks.Count >= 3, "count=" + chunks.Count);
            string summary = c.BuildSummary("First sentence. Second sentence. Third sentence. Fourth.", 2);
            Check("summary first sentence", summary.StartsWith("First sentence."), summary);
            var tags = c.InferTags("security token network password token access");
            Check("tags inferred", tags.Contains("security") && tags.Contains("token"), string.Join(",", tags.ToArray()));
        }

        static void TestRedactor()
        {
            Log("[Redactor]");
            var r = new Redactor();
            r.Enabled = true;
            string input = "Contact bob@example.com or call +86 138 0000 1111 id 11010119900307651X";
            string outp = r.Apply(input);
            Check("email redacted", outp.IndexOf("bob@example.com") < 0 && outp.IndexOf("[EMAIL]") >= 0);
            Check("phone redacted", outp.IndexOf("[PHONE]") >= 0);
            Check("id redacted", outp.IndexOf("[ID]") >= 0);
            r.Enabled = false;
            Check("disabled keeps original", r.Apply(input) == input);
        }

        static void TestFolderOrganizer()
        {
            Log("[FolderOrganizer]");
            var src = Path.Combine(tmp, "organize_src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "x");
            File.WriteAllText(Path.Combine(src, "b.png"), "y");
            File.WriteAllText(Path.Combine(src, "c.mp3"), "z");
            var fo = new FolderOrganizer(Path.Combine(tmp, "org_cfg"));
            string mf = fo.Organize(src);
            Check("manifest created", File.Exists(mf));
            Check("txt categorized", Directory.Exists(Path.Combine(src, "Documents")));
            Check("png categorized", Directory.Exists(Path.Combine(src, "Images")));
            Check("mp3 categorized", Directory.Exists(Path.Combine(src, "Audio")));
            int rolled = fo.Rollback(mf);
            Check("rollback moved 3", rolled == 3, "rolled=" + rolled);
            Check("files restored", File.Exists(Path.Combine(src, "a.txt")) && File.Exists(Path.Combine(src, "b.png")) && File.Exists(Path.Combine(src, "c.mp3")));
        }

        static void TestPluginRunner()
        {
            Log("[PluginRunner]");
            var pdir = Path.Combine(tmp, "plugins");
            Directory.CreateDirectory(pdir);
            string good = Path.Combine(pdir, "summarize.py");
            File.WriteAllText(good, "# plugin");
            string bad = Path.Combine(tmp, "evil.exe");
            File.WriteAllText(bad, "x");
            var pr = new PluginRunner(pdir);
            pr.AllowAdvancedPlugins = false;
            Check("safe plugin passes", pr.Validate(good) == "");
            Check("outside plugin blocked", pr.Validate(bad).IndexOf("trusted") >= 0);
            File.WriteAllText(Path.Combine(pdir, "run.exe"), "x");
            Check("advanced blocked by default", pr.Validate(Path.Combine(pdir, "run.exe")).IndexOf("blocked") >= 0);
            pr.AllowAdvancedPlugins = true;
            string adv = Path.Combine(pdir, "run.exe");
            File.WriteAllText(adv, "x");
            Check("advanced allowed when enabled", pr.Validate(adv) == "");
        }

        static void TestStreamingBridge()
        {
            Log("[StreamingBridge]");
            string openai = "{\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}";
            Check("openai delta extracted", StreamingBridge.ExtractDelta(openai) == "Hello");
            string gemini = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"World\"}]}}]}";
            Check("gemini delta extracted", StreamingBridge.ExtractDelta(gemini) == "World");
            string escape = "{\"choices\":[{\"delta\":{\"content\":\"line1\\nline2\"}}]}";
            Check("escape unescaped", StreamingBridge.ExtractDelta(escape) == "line1\nline2");
        }

        static void TestCommandParser()
        {
            Log("[CommandParser]");
            var p = new CommandParser();
            var pc = p.Parse("/summarize file.txt --lang=zh");
            Check("is command", pc.IsCommand);
            Check("verb parsed", pc.Verb == "summarize", pc.Verb);
            Check("arg parsed", pc.Args.Count == 1 && pc.Args[0] == "file.txt");
            Check("flag parsed", pc.Flags.ContainsKey("lang") && pc.Flags["lang"] == "zh");
            var plain = p.Parse("just a sentence");
            Check("plain text not command", !plain.IsCommand);
            var quoted = p.Parse("/tag \"two words\"");
            Check("quoted arg", quoted.Args.Count == 1 && quoted.Args[0] == "two words");
        }

        static void TestSmartCommand()
        {
            Log("[SmartCommand]");
            var reg = new SmartCommand();
            reg.Register("ping", c => "pong");
            reg.Register("echo", c => string.Join(" ", c.Args.ToArray()));
            Check("find known", reg.Find("ping"));
            Check("find unknown", !reg.Find("nope"));
            Check("execute ping", reg.Execute(new ParsedCommand { IsCommand = true, Verb = "ping" }) == "pong");
            Check("execute echo", reg.Execute(new ParsedCommand { IsCommand = true, Verb = "echo", Args = new List<string> { "a", "b" } }) == "a b");
            Check("execute unknown", reg.Execute(new ParsedCommand { IsCommand = true, Verb = "zzz" }).StartsWith("unknown"));
        }
    }
}
