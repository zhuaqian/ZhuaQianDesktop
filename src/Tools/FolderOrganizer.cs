using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace ZhuaQianDesktopApp.Tools
{
    public class OrganizeResult
    {
        public string ManifestPath;
        public int Moved;
        public int Errors;
        public List<Dictionary<string, object>> MovedPairs;
        public List<Dictionary<string, object>> ErrorList;
    }

    // Organizes files in a folder into categorized subfolders and produces a
    // rollback manifest so the move can be undone. Read-only until the caller
    // confirms through the permission gate / Approval Card.
    // Spec: docs/CURRENT_GAPS_ASSESSMENT.md (P0 rollback) and NEXT_STEP_EXECUTION_PLAN.md.
    public class FolderOrganizer
    {
        readonly string rollbackDir;

        public FolderOrganizer(string configDir)
        {
            rollbackDir = Path.Combine(configDir, "rollback");
            Directory.CreateDirectory(rollbackDir);
        }

        static readonly HashSet<string> ImageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
        static readonly HashSet<string> TextExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".doc", ".docx", ".pdf", ".rtf", ".csv", ".xls", ".xlsx", ".ppt", ".pptx"
        };
        static readonly HashSet<string> CodeExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".py", ".js", ".ts", ".java", ".cpp", ".c", ".h", ".ps1", ".json", ".xml", ".html", ".css"
        };
        static readonly HashSet<string> MediaExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".mp4", ".mov", ".avi"
        };
        static readonly HashSet<string> ArchiveExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".gz", ".tar"
        };

        static string FileTypeBucket(string ext)
        {
            if (ImageExts.Contains(ext)) return "Images";
            if (TextExts.Contains(ext)) return "Documents";
            if (CodeExts.Contains(ext)) return "Code";
            if (MediaExts.Contains(ext)) return "Media";
            if (ArchiveExts.Contains(ext)) return "Archives";
            return "Other";
        }

        static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; i < 10000; i++)
            {
                string candidate = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(candidate)) return candidate;
            }
            return Path.Combine(dir, name + " (" + Guid.NewGuid().ToString("N").Substring(0, 8) + ")" + ext);
        }

        public List<KeyValuePair<string, string>> BuildPlan(string rootDir)
        {
            var plan = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(rootDir) || !Directory.Exists(rootDir))
                return plan;
            foreach (var info in new DirectoryInfo(rootDir).GetFiles())
            {
                string month = info.LastWriteTime.ToString("yyyy-MM");
                string type = FileTypeBucket(info.Extension);
                string targetDir = Path.Combine(rootDir, "_ZhuaQian_Organized", month, type);
                string target = UniquePath(Path.Combine(targetDir, info.Name));
                plan.Add(new KeyValuePair<string, string>(info.FullName, target));
            }
            return plan;
        }

        public OrganizeResult Execute(string rootDir, List<KeyValuePair<string, string>> plan)
        {
            var result = new OrganizeResult
            {
                Moved = 0,
                Errors = 0,
                MovedPairs = new List<Dictionary<string, object>>(),
                ErrorList = new List<Dictionary<string, object>>()
            };
            if (plan == null) plan = new List<KeyValuePair<string, string>>();
            string manifestPath = Path.Combine(rollbackDir, "organize-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
            try
            {
                foreach (var kv in plan)
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(kv.Value);
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        File.Move(kv.Key, kv.Value);
                        result.Moved++;
                        result.MovedPairs.Add(new Dictionary<string, object>
                        {
                            { "from", kv.Key },
                            { "to", kv.Value }
                        });
                    }
                    catch (Exception ex)
                    {
                        result.Errors++;
                        result.ErrorList.Add(new Dictionary<string, object>
                        {
                            { "from", kv.Key },
                            { "error", ex.Message }
                        });
                    }
                }
                var manifest = new Dictionary<string, object>
                {
                    { "manifest", manifestPath },
                    { "moved", result.MovedPairs },
                    { "errors", result.ErrorList }
                };
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
                File.WriteAllText(manifestPath, ser.Serialize(manifest), System.Text.Encoding.UTF8);
                result.ManifestPath = manifestPath;
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorList.Add(new Dictionary<string, object> { { "error", ex.Message } });
            }
            return result;
        }

        public int Rollback(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
                return 0;
            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
                var manifest = ser.DeserializeObject(File.ReadAllText(manifestPath, System.Text.Encoding.UTF8)) as Dictionary<string, object>;
                if (manifest == null) return 0;
                var moved = ToList(manifest["moved"]);
                if (moved == null) return 0;
                int restored = 0;
                for (int i = moved.Count - 1; i >= 0; i--)
                {
                    var row = moved[i] as Dictionary<string, object>;
                    if (row == null) continue;
                    string original = row.ContainsKey("from") ? Convert.ToString(row["from"]) : "";
                    string current = row.ContainsKey("to") ? Convert.ToString(row["to"]) : "";
                    if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(current)) continue;
                    try
                    {
                        if (File.Exists(current) && !File.Exists(original))
                        {
                            string dir = Path.GetDirectoryName(original);
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                            File.Move(current, original);
                            restored++;
                        }
                    }
                    catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("FolderOrganizer.Rollback item: " + _ex.Message); }
                }
                return restored;
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("FolderOrganizer.Rollback: " + _ex.Message); return 0; }
        }

        public string BuildPreview(string manifestPath)
        {
            if (!File.Exists(manifestPath)) return "";
            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
                var manifest = ser.DeserializeObject(File.ReadAllText(manifestPath, System.Text.Encoding.UTF8)) as Dictionary<string, object>;
                if (manifest == null) return "";
                var moved = ToList(manifest["moved"]);
                if (moved == null) return "";
                var sb = new System.Text.StringBuilder();
                int count = 0;
                foreach (var item in moved)
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null) continue;
                    count++;
                    sb.AppendLine(count + ". " + Convert.ToString(row["from"]) + " -> " + Convert.ToString(row["to"]));
                }
                return sb.ToString();
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("FolderOrganizer.BuildPreview: " + _ex.Message); return ""; }
        }

        public static string CategoryFor(string filePath)
        {
            string ext = Path.GetExtension(filePath ?? "");
            if (ImageExts.Contains(ext)) return "Images";
            if (TextExts.Contains(ext)) return "Documents";
            if (CodeExts.Contains(ext)) return "Code";
            if (MediaExts.Contains(ext)) return "Media";
            if (ArchiveExts.Contains(ext)) return "Archives";
            if (string.IsNullOrEmpty(ext)) return "Misc";
            return "Other";
        }

        static ArrayList ToList(object raw)
        {
            var list = raw as ArrayList;
            if (list != null) return list;
            var arr = raw as object[];
            if (arr != null) return new ArrayList(arr);
            return null;
        }
    }
}
