using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ZhuaQianDesktopApp.Agent
{
    // The missing "code editor kernel" (Epic D3). Applies controlled patch
    // operations (add / modify / delete a file) through the single
    // Command -> PermissionGate -> Executor -> AuditLog pipeline, and records
    // a unified diff (plus a rollback manifest) so the change is reviewable.
    //
    // This is what turns CodingAgentSession from a one-shot reporter into a
    // real "modify code" actor that Codex / Claude Code expose natively.
    public sealed class PatchExecutor : ICommandExecutor
    {
        public string CommandType { get { return "PatchFile"; } }

        readonly string rootDirectory;

        public PatchExecutor(string rootDirectory)
        {
            this.rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(rootDirectory);
        }

        public CommandResult Execute(IAgentCommand command)
        {
            string op = GetString(command.Parameters, "op");   // add | edit | delete
            string rel = command.Target;
            if (string.IsNullOrWhiteSpace(rel))
                return CommandResult.Failed("PatchFile requires a target path");
            string abs = Path.GetFullPath(Path.Combine(rootDirectory, rel));
            if (!IsWithinRoot(abs))
                return CommandResult.Failed("target escapes root: " + rel);

            string oldText = GetString(command.Parameters, "oldText");
            string newText = GetString(command.Parameters, "newText");

            string before = File.Exists(abs) ? File.ReadAllText(abs, Encoding.UTF8) : "";
            string after = before;
            string diff;

            if (op == "add")
            {
                if (File.Exists(abs)) return CommandResult.Failed("add target already exists: " + rel);
                after = newText ?? "";
                WriteAllText(abs, after);
                diff = MakeDiff(rel, before, after);
            }
            else if (op == "edit")
            {
                if (!File.Exists(abs)) return CommandResult.Failed("edit target missing: " + rel);
                if (string.IsNullOrEmpty(oldText) || !before.Contains(oldText))
                    return CommandResult.Failed("edit oldText not found in " + rel);
                after = before.Replace(oldText, newText ?? "", 1);
                WriteAllText(abs, after);
                diff = MakeDiff(rel, before, after);
            }
            else if (op == "delete")
            {
                if (!File.Exists(abs)) return CommandResult.Failed("delete target missing: " + rel);
                File.Delete(abs);
                diff = MakeDiff(rel, before, "");
            }
            else
            {
                return CommandResult.Failed("unknown patch op: " + op);
            }

            string diffPath = Path.Combine(Path.GetTempPath(), "zq-patch-" + Guid.NewGuid().ToString("N") + ".diff");
            File.WriteAllText(diffPath, diff, Encoding.UTF8);
            string manifest = WriteRollbackManifest(rel, op, before);

            return CommandResult.Ok(diffPath, true, manifest, "diff",
                Encoding.UTF8.GetByteCount(diff), diff);
        }

        bool IsWithinRoot(string path)
        {
            try
            {
                string full = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string root = rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ||
                       full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                       full.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static void WriteAllText(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, text, Encoding.UTF8);
        }

        // Minimal unified-diff builder (no external diff dependency). Mirrors
        // the Python reference's difflib.unified_diff output closely enough for
        // human review inside the UI.
        static string MakeDiff(string rel, string before, string after)
        {
            var bLines = before.Length == 0 ? new List<string>() : new List<string>(before.Split('\n'));
            var aLines = after.Length == 0 ? new List<string>() : new List<string>(after.Split('\n'));
            var sb = new StringBuilder();
            sb.AppendLine("--- " + rel);
            sb.AppendLine("+++ " + rel);
            int n = Math.Max(bLines.Count, aLines.Count);
            int bIdx = 0, aIdx = 0;
            while (bIdx < bLines.Count || aIdx < aLines.Count)
            {
                bool same = bIdx < bLines.Count && aIdx < aLines.Count &&
                            bLines[bIdx] == aLines[aIdx];
                if (same) { bIdx++; aIdx++; continue; }
                sb.AppendLine("@@");
                while (bIdx < bLines.Count &&
                       !(aIdx < aLines.Count && bLines[bIdx] == aLines[aIdx]))
                {
                    if (bLines[bIdx].EndsWith("\r")) sb.Append("- " + bLines[bIdx].TrimEnd('\r'));
                    else sb.AppendLine("- " + bLines[bIdx]);
                    bIdx++;
                }
                while (aIdx < aLines.Count &&
                       !(bIdx < bLines.Count && bLines[bIdx] == aLines[aIdx]))
                {
                    if (aLines[aIdx].EndsWith("\r")) sb.Append("+ " + aLines[aIdx].TrimEnd('\r'));
                    else sb.AppendLine("+ " + aLines[aIdx]);
                    aIdx++;
                }
                break;
            }
            return sb.ToString().TrimEnd('\n') + "\n";
        }

        static string WriteRollbackManifest(string rel, string op, string before)
        {
            var sb = new StringBuilder();
            sb.AppendLine("op=" + op);
            sb.AppendLine("target=" + rel);
            string dataPath = Path.Combine(Path.GetTempPath(), "zq-rollback-" + Guid.NewGuid().ToString("N") + ".bak");
            File.WriteAllText(dataPath, before, Encoding.UTF8);
            sb.AppendLine("backup=" + dataPath);
            string manifest = Path.Combine(Path.GetTempPath(), "zq-manifest-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(manifest, sb.ToString(), Encoding.UTF8);
            return manifest;
        }

        static string GetString(IReadOnlyDictionary<string, object> values, string key)
        {
            object value;
            if (values != null && values.TryGetValue(key, out value) && value != null)
                return Convert.ToString(value);
            return "";
        }
    }
}
