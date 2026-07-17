using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;

namespace ZhuaQianDesktopApp.Tools
{
    // Read-only, non-blocking Windows process snapshot used by the monitoring layer.
    // It never terminates or suspends processes; it only records evidence for later review.
    public class ProcessSnapshot
    {
        public int Pid;
        public string Name = "";
        public string MainWindowTitle = "";
        public string MainModulePath = "";
        public long WorkingSetBytes;
        public string StartTimeIso = "";
        public int SessionId;
        public string RiskHint = "";
    }

    public class MonitoringEvent
    {
        public string eventId;
        public string at;
        public string type;        // snapshot | heartbeat | anomaly | case_opened | case_closed
        public string detail;
        public string caseId;
    }

    public class MonitoringCase
    {
        public string caseId;
        public string openedAt;
        public string title;
        public string severity;    // low | medium | high
        public string status;       // open | reviewing | closed
        public string summary;
        public List<string> relatedEvents = new List<string>();
    }

    public class ProcessSnapshotCollector
    {
        static readonly string[] SuspiciousHints = new string[]
        {
            "cheat", "inject", "trainer", "wpe", "olly", "x64dbg", "x32dbg",
            "cheatengine", "ce.exe", "speed", "aimbot", "hook", "debugger"
        };

        readonly string eventsPath;
        readonly string casesPath;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };

        // In-memory baseline of the previous snapshot so we can diff on the next run.
        HashSet<int> previousPids = new HashSet<int>();
        Dictionary<int, string> previousNames = new Dictionary<int, string>();

        public Func<string> GetActiveCaseId;

        public ProcessSnapshotCollector(string configDir, string eventsFile, string casesFile)
        {
            Directory.CreateDirectory(configDir);
            eventsPath = eventsFile;
            casesPath = casesFile;
        }

        public List<ProcessSnapshot> Collect()
        {
            var result = new List<ProcessSnapshot>();
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    var snap = new ProcessSnapshot();
                    try
                    {
                        snap.Pid = p.Id;
                        snap.Name = p.ProcessName ?? "";
                        snap.MainWindowTitle = p.MainWindowTitle ?? "";
                        snap.WorkingSetBytes = SafeWorkingSet(p);
                        snap.SessionId = SafeSessionId(p);
                        snap.StartTimeIso = SafeStartTime(p);
                        snap.MainModulePath = SafeMainModule(p);
                        snap.RiskHint = ClassifyRisk(snap.Name, snap.MainModulePath);
                    }
                    catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("ProcessSnapshotCollector.Collect inner: " + _ex.Message); }
                    finally
                    {
                        try { p.Dispose(); } catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("ProcessSnapshot dispose: " + _ex.Message); }
                    }
                    result.Add(snap);
                }
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("ProcessSnapshotCollector.Collect: " + _ex.Message); }
            return result;
        }

        // Collects a snapshot, writes a monitoring event, and opens a case for any
        // newly-appeared suspicious process. Returns the number of events written.
        public int RecordSnapshot()
        {
            var snaps = Collect();
            int written = 0;
            var currentPids = new HashSet<int>();
            var currentNames = new Dictionary<int, string>();

            foreach (var s in snaps)
            {
                currentPids.Add(s.Pid);
                currentNames[s.Pid] = s.Name;

                bool isNew = !previousPids.Contains(s.Pid);
                bool suspicious = !string.IsNullOrEmpty(s.RiskHint);

                if (isNew && suspicious)
                {
                    string detail = "New suspicious process detected: " + s.Name +
                        " (pid=" + s.Pid + ", hint=" + s.RiskHint + ")";
                    string caseId = OpenCase("Suspicious process: " + s.Name, "medium", detail);
                    WriteEvent("anomaly", detail, caseId);
                    written++;
                }
            }

            // Heartbeat event for the snapshot itself (read-only evidence record).
            WriteEvent("snapshot", "Collected " + snaps.Count + " process snapshots.", "");
            written++;

            previousPids = currentPids;
            previousNames = currentNames;
            return written;
        }

        public string OpenCase(string title, string severity, string summary)
        {
            var cs = new MonitoringCase
            {
                caseId = "case-" + Guid.NewGuid().ToString("N").Substring(0, 12),
                openedAt = DateTime.Now.ToString("o"),
                title = title ?? "",
                severity = severity ?? "low",
                status = "open",
                summary = summary ?? ""
            };
            try
            {
                File.AppendAllText(casesPath, json.Serialize(cs) + "\r\n", System.Text.Encoding.UTF8);
                WriteEvent("case_opened", "Case opened: " + title, cs.caseId);
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("ProcessSnapshotCollector.OpenCase: " + _ex.Message); }
            return cs.caseId;
        }

        public void CloseCase(string caseId)
        {
            if (string.IsNullOrWhiteSpace(caseId)) return;
            try
            {
                var cases = LoadCases(int.MaxValue);
                for (int i = 0; i < cases.Count; i++)
                {
                    if (string.Equals(cases[i].caseId, caseId, StringComparison.OrdinalIgnoreCase))
                    {
                        cases[i].status = "closed";
                        break;
                    }
                }
                cases.Reverse();
                SaveCases(cases);
                WriteEvent("case_closed", "Case closed: " + caseId, caseId);
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("ProcessSnapshotCollector.CloseCase: " + _ex.Message); }
        }

        public List<MonitoringCase> LoadCases(int max)
        {
            var rows = new List<MonitoringCase>();
            if (!File.Exists(casesPath)) return rows;
            try
            {
                var lines = File.ReadAllLines(casesPath, System.Text.Encoding.UTF8);
                for (int i = lines.Length - 1; i >= 0 && rows.Count < max; i--)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var c = json.DeserializeObject(lines[i]) as Dictionary<string, object>;
                    if (c == null) continue;
                    rows.Add(new MonitoringCase
                    {
                        caseId = Str(c, "caseId"),
                        openedAt = Str(c, "openedAt"),
                        title = Str(c, "title"),
                        severity = Str(c, "severity"),
                        status = Str(c, "status"),
                        summary = Str(c, "summary")
                    });
                }
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("ProcessSnapshotCollector.LoadCases: " + _ex.Message); }
            return rows;
        }

        public List<MonitoringEvent> LoadEvents(int max)
        {
            var rows = new List<MonitoringEvent>();
            if (!File.Exists(eventsPath)) return rows;
            try
            {
                var lines = File.ReadAllLines(eventsPath, System.Text.Encoding.UTF8);
                for (int i = lines.Length - 1; i >= 0 && rows.Count < max; i--)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var e = json.DeserializeObject(lines[i]) as Dictionary<string, object>;
                    if (e == null) continue;
                    rows.Add(new MonitoringEvent
                    {
                        eventId = Str(e, "eventId"),
                        at = Str(e, "at"),
                        type = Str(e, "type"),
                        detail = Str(e, "detail"),
                        caseId = Str(e, "caseId")
                    });
                }
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("ProcessSnapshotCollector.LoadEvents: " + _ex.Message); }
            return rows;
        }

        public void ClearRecords()
        {
            try
            {
                if (File.Exists(eventsPath)) File.Delete(eventsPath);
                if (File.Exists(casesPath)) File.Delete(casesPath);
                previousPids.Clear();
                previousNames.Clear();
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("ProcessSnapshotCollector.ClearRecords: " + _ex.Message); }
        }

        void WriteEvent(string type, string detail, string caseId)
        {
            try
            {
                var ev = new MonitoringEvent
                {
                    eventId = "evt-" + Guid.NewGuid().ToString("N").Substring(0, 12),
                    at = DateTime.Now.ToString("o"),
                    type = type ?? "",
                    detail = (detail ?? "").Replace("\r", " ").Replace("\n", " "),
                    caseId = caseId ?? ""
                };
                File.AppendAllText(eventsPath, json.Serialize(ev) + "\r\n", System.Text.Encoding.UTF8);
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("ProcessSnapshotCollector.WriteEvent: " + _ex.Message); }
        }

        void SaveCases(List<MonitoringCase> cases)
        {
            var lines = new List<string>();
            foreach (var c in cases)
                lines.Add(json.Serialize(c));
            File.WriteAllLines(casesPath, lines.ToArray(), System.Text.Encoding.UTF8);
        }

        static string ClassifyRisk(string name, string path)
        {
            string hay = ((name ?? "") + " " + (path ?? "")).ToLowerInvariant();
            foreach (var hint in SuspiciousHints)
            {
                if (hay.Contains(hint))
                    return hint;
            }
            return "";
        }

        static long SafeWorkingSet(Process p)
        {
            try { return p.WorkingSet64; } catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("SafeWorkingSet pid=" + p.Id + ": " + _ex.Message); return 0; }
        }

        static int SafeSessionId(Process p)
        {
            try { return p.SessionId; } catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("SafeSessionId pid=" + p.Id + ": " + _ex.Message); return -1; }
        }

        static string SafeStartTime(Process p)
        {
            try { return p.StartTime.ToString("o"); } catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("SafeStartTime pid=" + p.Id + ": " + _ex.Message); return ""; }
        }

        static string SafeMainModule(Process p)
        {
            try
            {
                var m = p.MainModule;
                return m != null ? (m.FileName ?? "") : "";
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("SafeMainModule pid=" + p.Id + ": " + _ex.Message); return ""; }
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            return d.ContainsKey(key) ? Convert.ToString(d[key]) : "";
        }
    }
}
