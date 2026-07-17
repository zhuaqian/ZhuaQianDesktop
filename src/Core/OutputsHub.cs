using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ZhuaQianDesktopApp.Core
{
    public class OutputsHub
    {
        readonly string configDir;
        readonly string outputsPath;
        readonly string exportHistoryPath;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };

        public OutputsHub(string configDir)
        {
            this.configDir = configDir;
            outputsPath = Path.Combine(configDir, "outputs.jsonl");
            exportHistoryPath = Path.Combine(configDir, "export-history.jsonl");
        }

        public void RecordOutput(string sourceAction, string type, string path, string taskId, string taskTitle, string sourceActionId, int sizeBytes)
        {
            try
            {
                Directory.CreateDirectory(configDir);
                bool exists = File.Exists(path);
                long actualSize = exists ? new FileInfo(path).Length : 0;
                var row = new Dictionary<string, object>
                {
                    { "outputId", Guid.NewGuid().ToString("N") },
                    { "taskId", taskId ?? "" },
                    { "taskTitle", taskTitle ?? "" },
                    { "displayName", taskTitle ?? "" },
                    { "recordSource", "primary" },
                    { "type", type ?? "txt" },
                    { "path", path ?? "" },
                    { "createdAt", DateTime.Now.ToString("o") },
                    { "sourceAction", sourceAction ?? "" },
                    { "sourceActionId", sourceActionId ?? "" },
                    { "exists", exists },
                    { "sizeBytes", actualSize > 0 ? actualSize : (long)sizeBytes },
                    { "metadata", new Dictionary<string, object>() }
                };
                File.AppendAllText(outputsPath, json.Serialize(row) + "\r\n", Encoding.UTF8);
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("OutputsHub.RecordOutput: " + _ex.Message); }
        }

        public void RecordExportHistory(string format, string path, int chars, string taskId, string taskTitle)
        {
            try
            {
                Directory.CreateDirectory(configDir);
                var row = new Dictionary<string, object> {
                    { "at", DateTime.Now.ToString("o") },
                    { "taskId", taskId ?? "" },
                    { "taskTitle", taskTitle ?? "" },
                    { "format", format ?? "" },
                    { "path", path ?? "" },
                    { "chars", chars }
                };
                File.AppendAllText(exportHistoryPath, json.Serialize(row) + "\r\n", Encoding.UTF8);
                // NOTE: do not also call RecordOutput here. LoadOutputRows() already
                // merges export-history rows into the unified outputs view, so a second
                // RecordOutput would create a duplicate entry.
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("OutputsHub.RecordExportHistory: " + _ex.Message); }
        }

        public List<Dictionary<string, object>> LoadOutputRows(int max)
        {
            var rows = LoadPrimaryOutputRows(max);
            int remaining = max - rows.Count;
            if (remaining > 0)
            {
                foreach (var legacy in LoadExportHistoryRows(remaining))
                {
                    string path = legacy.ContainsKey("path") ? Convert.ToString(legacy["path"]) : "";
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    bool exists = File.Exists(path);
                    long size = exists ? new FileInfo(path).Length : 0;
                    var converted = new Dictionary<string, object>
                    {
                    { "outputId", StableLegacyOutputId(legacy) },
                    { "taskId", legacy.ContainsKey("taskId") ? Convert.ToString(legacy["taskId"]) : "" },
                    { "taskTitle", legacy.ContainsKey("taskTitle") ? Convert.ToString(legacy["taskTitle"]) : "" },
                    { "displayName", legacy.ContainsKey("taskTitle") ? Convert.ToString(legacy["taskTitle"]) : "" },
                    { "recordSource", "legacy-export-history" },
                    { "type", legacy.ContainsKey("format") ? Convert.ToString(legacy["format"]) : "txt" },
                    { "path", path },
                    { "createdAt", legacy.ContainsKey("at") ? Convert.ToString(legacy["at"]) : "" },
                    { "sourceAction", "export" },
                        { "sourceActionId", "" },
                        { "exists", exists },
                        { "sizeBytes", size }
                    };
                    rows.Add(converted);
                }
            }
            return rows;
        }

        public void SaveOutputRows(List<Dictionary<string, object>> rows)
        {
            try
            {
                Directory.CreateDirectory(configDir);
                var sb = new StringBuilder();
                foreach (var row in rows)
                    sb.AppendLine(json.Serialize(row));
                File.WriteAllText(outputsPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("OutputsHub.SaveOutputRows: " + _ex.Message); }
        }

        public void RemoveLegacyExportEntry(string path)
        {
            try
            {
                if (!File.Exists(exportHistoryPath)) return;
                var lines = new List<string>(File.ReadAllLines(exportHistoryPath, Encoding.UTF8));
                int removed = lines.RemoveAll(line =>
                {
                    if (string.IsNullOrWhiteSpace(line)) return false;
                    try
                    {
                        var row = json.DeserializeObject(line) as Dictionary<string, object>;
                        return row != null && Convert.ToString(row["path"]) == path;
                    }
                    catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("OutputsHub.RemoveLegacyExportEntry parse: " + _ex.Message); return false; }
                });
                if (removed > 0)
                    File.WriteAllText(exportHistoryPath, string.Join("\r\n", lines) + "\r\n", Encoding.UTF8);
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("RemoveLegacyExportEntry: " + _ex.Message); }
        }

        public bool Rename(string outputId, string newTaskTitle)
        {
            if (string.IsNullOrWhiteSpace(outputId)) return false;
            var rows = LoadPrimaryOutputRows(int.MaxValue);
            bool changed = false;
            foreach (var row in rows)
            {
                if (!RowIdMatches(row, outputId)) continue;
                row["taskTitle"] = newTaskTitle ?? "";
                row["displayName"] = newTaskTitle ?? "";
                changed = true;
                break;
            }
            if (changed) SaveOutputRows(rows);
            return changed;
        }

        public bool AddToKnowledge(string outputId)
        {
            if (string.IsNullOrWhiteSpace(outputId)) return false;
            var rows = LoadPrimaryOutputRows(int.MaxValue);
            bool changed = false;
            foreach (var row in rows)
            {
                if (!RowIdMatches(row, outputId)) continue;
                row["addedToKnowledge"] = true;
                changed = true;
                break;
            }
            if (changed) SaveOutputRows(rows);
            return changed;
        }

        public bool Delete(string outputId)
        {
            if (string.IsNullOrWhiteSpace(outputId)) return false;
            var rows = LoadPrimaryOutputRows(int.MaxValue);
            int removed = rows.RemoveAll(row => RowIdMatches(row, outputId));
            if (removed > 0) SaveOutputRows(rows);
            return removed > 0;
        }

        public List<Dictionary<string, object>> LoadExportHistoryRows(int max)
        {
            var rows = new List<Dictionary<string, object>>();
            if (!File.Exists(exportHistoryPath)) return rows;
            try
            {
                var lines = File.ReadAllLines(exportHistoryPath, Encoding.UTF8);
                for (int i = lines.Length - 1; i >= 0 && rows.Count < max; i--)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var row = json.DeserializeObject(lines[i]) as Dictionary<string, object>;
                    if (row != null) rows.Add(row);
                }
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("OutputsHub.LoadExportHistoryRows: " + _ex.Message); }
            return rows;
        }

        static bool RowIdMatches(Dictionary<string, object> row, string outputId)
        {
            if (row == null || !row.ContainsKey("outputId")) return false;
            return string.Equals(Convert.ToString(row["outputId"]), outputId, StringComparison.OrdinalIgnoreCase);
        }

        List<Dictionary<string, object>> LoadPrimaryOutputRows(int max)
        {
            var rows = new List<Dictionary<string, object>>();
            if (!File.Exists(outputsPath)) return rows;
            try
            {
                var lines = File.ReadAllLines(outputsPath, Encoding.UTF8);
                for (int i = lines.Length - 1; i >= 0 && rows.Count < max; i--)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var row = json.DeserializeObject(lines[i]) as Dictionary<string, object>;
                    if (row != null)
                    {
                        if (!row.ContainsKey("recordSource")) row["recordSource"] = "primary";
                        if (!row.ContainsKey("displayName")) row["displayName"] = row.ContainsKey("taskTitle") ? Convert.ToString(row["taskTitle"]) : "";
                        rows.Add(row);
                    }
                }
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("OutputsHub.LoadPrimaryOutputRows: " + _ex.Message); }
            return rows;
        }

        static string StableLegacyOutputId(Dictionary<string, object> legacy)
        {
            string source = "";
            if (legacy != null)
            {
                source = (legacy.ContainsKey("path") ? Convert.ToString(legacy["path"]) : "") + "|" +
                         (legacy.ContainsKey("at") ? Convert.ToString(legacy["at"]) : "") + "|" +
                         (legacy.ContainsKey("taskId") ? Convert.ToString(legacy["taskId"]) : "") + "|" +
                         (legacy.ContainsKey("format") ? Convert.ToString(legacy["format"]) : "");
            }
            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
                var sb = new StringBuilder();
                for (int i = 0; i < 8 && i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return "legacy-" + sb.ToString();
            }
        }
    }
}
