using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZhuaQianDesktopApp.Core
{
    // One structured audit/action record. Tab-separated on disk for cheap appends and
    // grep-friendly inspection; also surfaced in the in-app Audit Log panel.
    public class ActionRecord
    {
        public string Timestamp;
        public string Action;
        public string Detail;
        public string Actor;
        public string TaskId;
        public string Status;
        public Dictionary<string, object> Meta = new Dictionary<string, object>();
    }

    // Append-only audit log with a buffered writer. Safe to call from any thread
    // (lock-protected). Flushes automatically past a threshold or on explicit Flush().
    public class AuditLog
    {
        readonly string path;
        readonly object sync = new object();
        readonly StringBuilder buffer = new StringBuilder();

        public AuditLog(string logPath)
        {
            path = logPath;
        }

        public void Log(string action, string detail, string actor = "user", string taskId = "", string status = "ok")
        {
            try
            {
                var rec = new ActionRecord
                {
                    Timestamp = DateTime.Now.ToString("o"),
                    Action = action ?? "",
                    Detail = detail ?? "",
                    Actor = actor ?? "user",
                    TaskId = taskId ?? "",
                    Status = status ?? "ok"
                };
                string line = rec.Timestamp + "\t" + rec.Action + "\t" + rec.Actor + "\t" + rec.TaskId + "\t" + rec.Status + "\t" + rec.Detail.Replace("\r", " ").Replace("\n", " ") + "\n";
                lock (sync)
                {
                    buffer.Append(line);
                    if (buffer.Length > 8192)
                        FlushBuffer();
                }
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("AuditLog.Log: " + _ex.Message); }
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
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("AuditLog.FlushBuffer: " + _ex.Message); }
        }

        public void Flush()
        {
            lock (sync) FlushBuffer();
        }

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
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("AuditLog.List: " + _ex.Message); }
            return result;
        }
    }
}
