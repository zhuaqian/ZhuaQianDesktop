using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZhuaQianDesktopApp.Agent
{
    public enum ChangeKind { Add, Modify, Delete }

    // A single-file patch in unified-diff form. PatchText is the standard
    // `@@ ... @@` unified diff; it is both human-readable (shown in the UI) and
    // machine-applicable (PatchApplier).
    public sealed class UnifiedPatch
    {
        public string FilePath = "";
        public ChangeKind Kind = ChangeKind.Modify;
        public string PatchText = "";
        public string Rationale = "";
    }

    public static class DiffEngine
    {
        // Produce a unified diff between two texts. Labels are the `---`/`+++`
        // headers (typically relative paths). Round-trips via PatchApplier.
        public static string GenerateUnified(string oldLabel, string oldText, string newLabel, string newText, int context = 3)
        {
            var oldLines = SplitLines(oldText);
            var newLines = SplitLines(newText);
            return GenerateUnifiedLines(oldLines, newLines, oldLabel, newLabel, context);
        }

        public static string GenerateUnifiedLines(IList<string> oldLines, IList<string> newLines, string oldLabel, string newLabel, int context = 3)
        {
            var ops = ComputeLcsOps(oldLines, newLines);
            var sb = new StringBuilder();
            sb.AppendLine("--- " + (oldLabel ?? "a"));
            sb.AppendLine("+++ " + (newLabel ?? "b"));

            int n = ops.Count;
            int cursor = 0;
            while (cursor < n)
            {
                while (cursor < n && ops[cursor].Type == 0) cursor++;
                if (cursor >= n) break;
                int changeStart = cursor;
                while (cursor < n && ops[cursor].Type != 0) cursor++;
                int changeEnd = cursor; // exclusive

                int bodyStart = Math.Max(0, changeStart - context);
                int bodyEnd = Math.Min(n, changeEnd + context);

                int oldStartLine = Math.Max(1, ops[bodyStart].OldLine <= 0 ? 1 : ops[bodyStart].OldLine);
                int newStartLine = Math.Max(1, ops[bodyStart].NewLine <= 0 ? 1 : ops[bodyStart].NewLine);

                var hunkOld = new List<string>();
                var hunkNew = new List<string>();
                int oldCount = 0, newCount = 0;
                for (int k = bodyStart; k < bodyEnd; k++)
                {
                    var op = ops[k];
                    if (op.Type == 1) { hunkOld.Add("-" + op.OldText); oldCount++; }
                    else if (op.Type == 2) { hunkNew.Add("+" + op.NewText); newCount++; }
                    else { hunkOld.Add(" " + op.OldText); hunkNew.Add(" " + op.NewText); oldCount++; newCount++; }
                }
                sb.AppendLine(string.Format("@@ -{0},{1} +{2},{3} @@", oldStartLine, oldCount, newStartLine, newCount));
                foreach (var l in hunkOld) sb.AppendLine(l);
                foreach (var l in hunkNew) sb.AppendLine(l);
            }

            return sb.ToString().TrimEnd('\n', '\r');
        }

        struct Op
        {
            public int Type; // 0 equal, 1 del, 2 add
            public string OldText;
            public string NewText;
            public int OldLine; // 1-based line in old (0 if none)
            public int NewLine; // 1-based line in new (0 if none)
        }

        static List<Op> ComputeLcsOps(IList<string> oldLines, IList<string> newLines)
        {
            int m = oldLines.Count, n = newLines.Count;
            var dp = new int[m + 1, n + 1];
            for (int i = m - 1; i >= 0; i--)
                for (int j = n - 1; j >= 0; j--)
                    dp[i, j] = oldLines[i] == newLines[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

            var ops = new List<Op>();
            int x = 0, y = 0, oldLineNo = 1, newLineNo = 1;
            while (x < m && y < n)
            {
                if (oldLines[x] == newLines[y])
                {
                    ops.Add(new Op { Type = 0, OldText = oldLines[x], NewText = newLines[y], OldLine = oldLineNo, NewLine = newLineNo });
                    x++; y++; oldLineNo++; newLineNo++;
                }
                else if (dp[x + 1, y] >= dp[x, y + 1])
                {
                    ops.Add(new Op { Type = 1, OldText = oldLines[x], NewText = oldLines[x], OldLine = oldLineNo, NewLine = 0 });
                    x++; oldLineNo++;
                }
                else
                {
                    ops.Add(new Op { Type = 2, OldText = newLines[y], NewText = newLines[y], OldLine = 0, NewLine = newLineNo });
                    y++; newLineNo++;
                }
            }
            while (x < m)
            {
                ops.Add(new Op { Type = 1, OldText = oldLines[x], NewText = oldLines[x], OldLine = oldLineNo, NewLine = 0 });
                x++; oldLineNo++;
            }
            while (y < n)
            {
                ops.Add(new Op { Type = 2, OldText = newLines[y], NewText = newLines[y], OldLine = 0, NewLine = newLineNo });
                y++; newLineNo++;
            }
            return ops;
        }

        static IList<string> SplitLines(string text)
        {
            if (text == null || text.Length == 0) return new List<string>();
            var parts = text.Replace("\r\n", "\n").Split('\n');
            var list = new List<string>(parts);
            if (list.Count > 0 && list[list.Count - 1].Length == 0) list.RemoveAt(list.Count - 1);
            return list;
        }
    }

    public static class PatchApplier
    {
        // Apply a unified diff to the original text and return the new text.
        // Strict line-number based (no fuzzy). Reliable when the patch was
        // generated against the current file content (our normal case: read
        // file -> diff -> apply).
        public static string Apply(string originalText, string patchText)
        {
            var orig = ToLines(originalText);
            var patchLines = (patchText ?? "").Replace("\r\n", "\n").Split('\n');

            // New-file case: original empty and patch is just additions.
            if (orig.Count == 0 && !HasHunkHeader(patchLines))
            {
                var result = new List<string>();
                foreach (var line in patchLines)
                {
                    if (line.StartsWith("+++") || line.StartsWith("---") || line.StartsWith("@@")) continue;
                    if (line.StartsWith("+")) result.Add(line.Substring(1));
                    else if (line.StartsWith("\\")) continue;
                }
                return JoinLines(result);
            }

            var outLines = new List<string>();
            int origIdx = 0; // 0-based
            bool inHunk = false;
            int i = 0;
            while (i < patchLines.Length)
            {
                var line = patchLines[i];
                if (line.StartsWith("---") || line.StartsWith("+++")) { i++; continue; }
                if (line.StartsWith("@@"))
                {
                    int oldStart = ParseHunkRange(line).Item1; // 1-based
                    while (origIdx < oldStart - 1)
                    {
                        outLines.Add(orig[origIdx]);
                        origIdx++;
                    }
                    i++;
                    inHunk = true;
                    continue;
                }
                if (inHunk)
                {
                    if (line.StartsWith("+")) outLines.Add(line.Substring(1));
                    else if (line.StartsWith("-")) origIdx++;
                    else if (line.StartsWith(" ")) { outLines.Add(orig[origIdx]); origIdx++; }
                    else if (line.StartsWith("\\")) { /* no newline marker */ }
                    else inHunk = false;
                    i++;
                    continue;
                }
                i++;
            }
            while (origIdx < orig.Count)
            {
                outLines.Add(orig[origIdx]);
                origIdx++;
            }
            return JoinLines(outLines);
        }

        // Apply a patch to a file in the workspace. Creates/modifies/deletes the
        // file. Returns a short status string for the audit log.
        public static string ApplyToWorkspace(string rootDir, UnifiedPatch patch)
        {
            if (patch == null || string.IsNullOrWhiteSpace(patch.FilePath))
                return "skip: empty patch path";
            string full = Path.Combine(rootDir, patch.FilePath.Replace('/', Path.DirectorySeparatorChar));
            if (patch.Kind == ChangeKind.Delete)
            {
                if (File.Exists(full)) { File.Delete(full); return "deleted " + patch.FilePath; }
                return "skip delete (missing) " + patch.FilePath;
            }
            if (patch.Kind == ChangeKind.Add)
            {
                string dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(full, ExtractNewFileText(patch.PatchText));
                return "created " + patch.FilePath;
            }
            string original = File.Exists(full) ? File.ReadAllText(full) : "";
            string updated = Apply(original, patch.PatchText);
            string dir2 = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir2) && !Directory.Exists(dir2)) Directory.CreateDirectory(dir2);
            File.WriteAllText(full, updated);
            return "patched " + patch.FilePath;
        }

        static string ExtractNewFileText(string patchText)
        {
            var lines = (patchText ?? "").Replace("\r\n", "\n").Split('\n');
            var sb = new List<string>();
            foreach (var line in lines)
            {
                if (line.StartsWith("+++") || line.StartsWith("---") || line.StartsWith("@@")) continue;
                if (line.StartsWith("+")) sb.Add(line.Substring(1));
                else if (line.StartsWith("\\")) continue;
                else if (line.StartsWith("-")) continue;
            }
            return JoinLines(sb);
        }

        static bool HasHunkHeader(string[] lines)
        {
            foreach (var l in lines) if (l.StartsWith("@@")) return true;
            return false;
        }

        static Tuple<int, int> ParseHunkRange(string header)
        {
            int a = header.IndexOf('-');
            int plus = header.IndexOf('+');
            int at2 = header.IndexOf('@', 2);
            string oldPart = header.Substring(a + 1, plus - (a + 1)).Trim();
            string newPart = header.Substring(plus + 1, at2 - (plus + 1)).Trim();
            return Tuple.Create(ParseFirst(oldPart), ParseFirst(newPart));
        }

        static int ParseFirst(string part)
        {
            int comma = part.IndexOf(',');
            string num = comma >= 0 ? part.Substring(0, comma) : part;
            int v;
            return int.TryParse(num, out v) ? v : 1;
        }

        static IList<string> ToLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();
            var parts = text.Replace("\r\n", "\n").Split('\n');
            var list = new List<string>(parts);
            if (list.Count > 0 && list[list.Count - 1].Length == 0) list.RemoveAt(list.Count - 1);
            return list;
        }

        static string JoinLines(IList<string> lines)
        {
            return string.Join("\n", lines);
        }
    }
}
