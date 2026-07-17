using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ZhuaQianDesktopApp.Tools
{
    public class SystemDiagnostics
    {
        public string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Local Computer Diagnostics");
            sb.AppendLine("Captured: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            AppendSystem(sb);
            AppendDrives(sb);
            AppendTopProcesses(sb);
            AppendAdvice(sb);
            return sb.ToString().Trim();
        }

        void AppendSystem(StringBuilder sb)
        {
            sb.AppendLine("## System");
            sb.AppendLine("- OS: " + Environment.OSVersion);
            sb.AppendLine("- 64-bit OS: " + Environment.Is64BitOperatingSystem);
            sb.AppendLine("- 64-bit process: " + Environment.Is64BitProcess);
            sb.AppendLine("- CPU cores: " + Environment.ProcessorCount);
            sb.AppendLine("- App memory: " + FormatBytes(Process.GetCurrentProcess().WorkingSet64));
            sb.AppendLine("- Uptime: " + TimeSpan.FromMilliseconds(Environment.TickCount & int.MaxValue));
            sb.AppendLine();
        }

        void AppendDrives(StringBuilder sb)
        {
            sb.AppendLine("## Drives");
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (!d.IsReady)
                    {
                        sb.AppendLine("- " + d.Name + " not ready");
                        continue;
                    }
                    double pct = d.TotalSize > 0 ? (double)d.AvailableFreeSpace / d.TotalSize * 100.0 : 0;
                    sb.AppendLine("- " + d.Name + " free " + FormatBytes(d.AvailableFreeSpace) + " / " + FormatBytes(d.TotalSize) + " (" + pct.ToString("0.0") + "%)");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("- Drive scan failed: " + ex.Message);
            }
            sb.AppendLine();
        }

        void AppendTopProcesses(StringBuilder sb)
        {
            sb.AppendLine("## Top Memory Processes");
            try
            {
                var rows = new List<ProcessRow>();
                foreach (var p in Process.GetProcesses())
                {
                    var row = SafeProcessRow(p);
                    if (row != null) rows.Add(row);
                }
                rows.Sort((a, b) => b.WorkingSetBytes.CompareTo(a.WorkingSetBytes));
                int count = Math.Min(12, rows.Count);
                for (int i = 0; i < count; i++)
                {
                    var r = rows[i];
                    sb.AppendLine("- " + r.Name + " pid=" + r.Pid + " memory=" + FormatBytes(r.WorkingSetBytes));
                }
                sb.AppendLine("- Window titles are omitted for privacy.");
            }
            catch (Exception ex)
            {
                sb.AppendLine("- Process scan failed: " + ex.Message);
            }
            sb.AppendLine();
        }

        void AppendAdvice(StringBuilder sb)
        {
            sb.AppendLine("## What To Check First");
            sb.AppendLine("- If the computer is slow, inspect the top memory processes and close apps with unsaved work only after saving.");
            sb.AppendLine("- If disk free space is under 10%, clean downloads, temp files, or large generated outputs.");
            sb.AppendLine("- If a specific app is stuck, identify its process name/PID before ending it.");
            sb.AppendLine("- For code problems, provide the folder path, error text, and the command you ran so the assistant can propose a minimal fix.");
        }

        ProcessRow SafeProcessRow(Process p)
        {
            try
            {
                return new ProcessRow
                {
                    Pid = p.Id,
                    Name = p.ProcessName ?? "",
                    WorkingSetBytes = p.WorkingSet64
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SystemDiagnostics process row: " + ex.Message);
                return null;
            }
            finally
            {
                try { p.Dispose(); } catch (Exception ex) { Debug.WriteLine("SystemDiagnostics dispose: " + ex.Message); }
            }
        }

        string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double value = bytes;
            string[] units = { "KB", "MB", "GB", "TB" };
            int idx = -1;
            do
            {
                value /= 1024.0;
                idx++;
            }
            while (value >= 1024.0 && idx < units.Length - 1);
            return value.ToString("0.0") + " " + units[idx];
        }

        class ProcessRow
        {
            public int Pid;
            public string Name = "";
            public long WorkingSetBytes;
        }
    }
}
