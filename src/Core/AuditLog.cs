using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
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

    // Result of a tamper-evident chain verification.
    public class AuditChainResult
    {
        public bool Ok;
        public int FirstBrokenLine = -1; // 0-based line index, -1 when Ok
        public string Message = "";
    }

    // Append-only audit log with a buffered writer. Safe to call from any thread
    // (lock-protected). Flushes automatically past a threshold or on explicit Flush().
    //
    // TAMPER-EVIDENT CHAIN (2026-07-20): every record carries a SHA-256 hash of
    // (previous record's hash + its own canonical line). Re-walking the file and
    // recomputing each hash lets VerifyChain() detect if any record was altered,
    // inserted, or deleted after the fact -- without any external store or blockchain.
    // Legacy lines written before this feature existed (no trailing hash field) are
    // treated as a trusted seed and skipped by the verifier; new lines form the chain.
    public class AuditLog
    {
        const string ChainSeed = "ZhuaQianDesktop-AuditChain-Genesis-v1";
        readonly string path;
        readonly object sync = new object();
        readonly StringBuilder buffer = new StringBuilder();
        string lastHash = ChainSeed;

        public AuditLog(string logPath)
        {
            path = logPath;
            LoadLastHash();
        }

        // Resume the chain across process restarts: the trailing hash of the last
        // hashed line becomes the seed for the next appended record.
        void LoadLastHash()
        {
            try
            {
                if (!File.Exists(path)) return;
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('\t');
                    if (parts.Length >= 7 && parts[6].Length == 64)
                    {
                        lastHash = parts[6];
                        return;
                    }
                    // first non-empty line from the end is legacy (no hash) -> keep seed
                    return;
                }
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("AuditLog.LoadLastHash: " + _ex.Message); }
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
                // collapse whitespace so a single record never spans multiple lines and
                // the canonical line is unambiguous for hashing/verification.
                string cleaned = rec.Detail.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
                string core = rec.Timestamp + "\t" + rec.Action + "\t" + rec.Actor + "\t" + rec.TaskId + "\t" + rec.Status + "\t" + cleaned;
                string h;
                lock (sync)
                {
                    h = Hash(lastHash + "|" + core);
                    lastHash = h;
                    buffer.Append(core + "\t" + h + "\n");
                    if (buffer.Length > 8192)
                        FlushBuffer();
                }
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("AuditLog.Log: " + _ex.Message); }
        }

        static string Hash(string input)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                var digest = sha.ComputeHash(bytes);
                var sb = new StringBuilder(digest.Length * 2);
                foreach (var b in digest) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
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

        // Re-walks the file and verifies the hash chain. Returns Ok=true when every
        // hashed record is internally consistent. FirstBrokenLine is the 0-based index
        // of the first record whose hash does not match (or -1 when intact).
        public AuditChainResult VerifyChain()
        {
            var result = new AuditChainResult();
            if (!File.Exists(path))
            {
                result.Ok = true;
                result.Message = "no log file";
                return result;
            }
            try
            {
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                string expectedPrev = ChainSeed;
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 7)
                    {
                        // legacy line (written before chain feature) -> trusted seed
                        expectedPrev = ChainSeed;
                        continue;
                    }
                    string core = parts[0] + "\t" + parts[1] + "\t" + parts[2] + "\t" + parts[3] + "\t" + parts[4] + "\t" + parts[5];
                    string computed = Hash(expectedPrev + "|" + core);
                    if (computed != parts[6])
                    {
                        result.Ok = false;
                        result.FirstBrokenLine = i;
                        result.Message = "hash mismatch at line " + (i + 1);
                        return result;
                    }
                    expectedPrev = parts[6];
                }
                result.Ok = true;
                result.Message = "chain intact";
                return result;
            }
            catch (Exception _ex)
            {
                result.Ok = false;
                result.Message = "verify error: " + _ex.Message;
                return result;
            }
        }

        public bool IsIntact()
        {
            return VerifyChain().Ok;
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
