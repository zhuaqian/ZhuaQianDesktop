using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp.Agent.Coding
{
    // Kind of file edit a CodePatch performs. This is the controlled-edit
    // primitive that turns "generate text" into "apply a reviewable patch",
    // matching how Codex / Claude Code edit repositories.
    public enum PatchOperation
    {
        Create,
        Modify,
        Delete
    }

    // A single controlled file edit. FilePath is relative to the project root.
    // NewContent is the full post-patch file content (Create / Modify); for
    // Delete it is ignored. OldContent is optional and, when present, is used
    // as a precondition check: the patch refuses to apply if the on-disk
    // content does not match, preventing blind overwrites of a file that
    // changed underneath the agent.
    public sealed class CodePatch
    {
        public PatchOperation Operation = PatchOperation.Modify;
        public string FilePath = "";
        public string NewContent = "";
        public string OldContent = "";
        public string Reason = "";
        public string SourceError = "";   // which BuildError triggered this patch, for audit

        public string Summary()
        {
            string op = Operation == PatchOperation.Create ? "create"
                       : Operation == PatchOperation.Delete ? "delete" : "modify";
            return op + " " + FilePath + (string.IsNullOrEmpty(Reason) ? "" : " (" + Reason + ")");
        }
    }

    // Result of applying one CodePatch. Carries the unified diff text (for UI
    // review / audit log), the backup path (for rollback), and success state.
    public sealed class PatchResult
    {
        public bool Success;
        public PatchOperation Operation;
        public string FilePath = "";
        public string DiffText = "";
        public string BackupPath = "";
        public string ErrorMessage = "";
        public int BytesChanged;
        public bool DryRun;

        public bool CanRollback { get { return !DryRun && !string.IsNullOrEmpty(BackupPath) && File.Exists(BackupPath); } }
    }

    // Controlled-edit kernel. Applies CodePatch operations to disk, generates
    // unified diffs, keeps .bak backups for rollback, and routes every write
    // through PermissionGate (permFileWrite). A dry-run mode produces the diff
    // and precondition checks without touching disk, so the UI can preview a
    // fix before the user approves it.
    //
    // Path safety: FilePath must be relative and must not escape the project
    // root (no .., no absolute paths), matching PluginManifestParser's guard.
    public sealed class CodePatcher
    {
        readonly PermissionGate permissionGate;
        readonly string rootDirectory;

        public CodePatcher(string rootDirectory, PermissionGate permissionGate)
        {
            this.rootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? "" : Path.GetFullPath(rootDirectory);
            this.permissionGate = permissionGate ?? new PermissionGate();
        }

        // Apply a single patch to disk. Returns a PatchResult with the diff.
        public PatchResult Apply(CodePatch patch)
        {
            return Apply(patch, false);
        }

        // Produce the diff and precondition checks without writing to disk.
        public PatchResult DryRun(CodePatch patch)
        {
            return Apply(patch, true);
        }

        public PatchResult Apply(CodePatch patch, bool dryRun)
        {
            var result = new PatchResult();
            result.DryRun = dryRun;
            if (patch == null)
            {
                result.ErrorMessage = "null patch";
                return result;
            }
            result.Operation = patch.Operation;
            result.FilePath = patch.FilePath ?? "";

            string pathError = ValidatePath(patch.FilePath);
            if (pathError != null)
            {
                result.ErrorMessage = pathError;
                return result;
            }

            string fullPath = Path.Combine(rootDirectory, patch.FilePath.Replace('/', Path.DirectorySeparatorChar));

            // Permission gate: every patch write goes through permFileWrite.
            if (!dryRun)
            {
                var decision = permissionGate.Check("permFileWrite", fullPath);
                if (decision != PermissionDecision.Allow)
                {
                    result.ErrorMessage = "permission denied for permFileWrite on " + patch.FilePath;
                    return result;
                }
            }

            string oldContent = "";
            bool exists = File.Exists(fullPath);
            if (exists)
            {
                try { oldContent = File.ReadAllText(fullPath); }
                catch (Exception ex) { result.ErrorMessage = "read failed: " + ex.Message; return result; }
            }

            // Precondition check: if OldContent is provided, the on-disk content
            // must match, otherwise the file changed underneath the agent.
            if (!string.IsNullOrEmpty(patch.OldContent) && patch.Operation == PatchOperation.Modify)
            {
                if (NormalizeLineEndings(oldContent) != NormalizeLineEndings(patch.OldContent))
                {
                    result.ErrorMessage = "precondition mismatch: file changed since scan (" + patch.FilePath + ")";
                    return result;
                }
            }

            // Operation validation.
            if (patch.Operation == PatchOperation.Create && exists && !dryRun)
            {
                // Create over an existing file degrades to Modify (still allowed,
                // but noted). This is common when a fix rewrites an existing file.
                patch.Operation = PatchOperation.Modify;
                result.Operation = PatchOperation.Modify;
            }
            if (patch.Operation == PatchOperation.Delete && !exists)
            {
                result.ErrorMessage = "cannot delete a file that does not exist: " + patch.FilePath;
                return result;
            }
            if (patch.Operation != PatchOperation.Delete && string.IsNullOrEmpty(patch.NewContent) && patch.Operation == PatchOperation.Create)
            {
                result.ErrorMessage = "create patch has empty NewContent";
                return result;
            }

            // Generate the diff before writing.
            if (patch.Operation == PatchOperation.Delete)
            {
                result.DiffText = GenerateDiff(oldContent, "", patch.FilePath);
            }
            else
            {
                result.DiffText = GenerateDiff(oldContent, patch.NewContent ?? "", patch.FilePath);
            }
            result.BytesChanged = Math.Abs((patch.NewContent ?? "").Length - oldContent.Length);

            if (dryRun)
            {
                result.Success = true;
                return result;
            }

            // Backup before writing (for Modify / Delete).
            if ((patch.Operation == PatchOperation.Modify || patch.Operation == PatchOperation.Delete) && exists)
            {
                try
                {
                    string backupPath = fullPath + ".bak";
                    int backupIndex = 0;
                    while (File.Exists(backupPath)) { backupIndex++; backupPath = fullPath + ".bak" + backupIndex; }
                    File.Copy(fullPath, backupPath, true);
                    result.BackupPath = backupPath;
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = "backup failed: " + ex.Message;
                    return result;
                }
            }

            // Apply.
            try
            {
                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                if (patch.Operation == PatchOperation.Delete)
                {
                    File.Delete(fullPath);
                }
                else
                {
                    File.WriteAllText(fullPath, patch.NewContent ?? "");
                }
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "write failed: " + ex.Message;
            }
            return result;
        }

        public List<PatchResult> ApplyAll(List<CodePatch> patches)
        {
            var results = new List<PatchResult>();
            if (patches == null) return results;
            foreach (var p in patches)
            {
                var r = Apply(p, false);
                results.Add(r);
                if (!r.Success) break; // stop on first failure (atomic-ish)
            }
            return results;
        }

        // Roll back a single patch using its .bak backup. Returns true on success.
        public bool Rollback(PatchResult result)
        {
            if (result == null || !result.CanRollback) return false;
            try
            {
                string fullPath = Path.Combine(rootDirectory, result.FilePath.Replace('/', Path.DirectorySeparatorChar));
                if (result.Operation == PatchOperation.Delete)
                {
                    // Restore the deleted file from backup.
                    File.Copy(result.BackupPath, fullPath, true);
                }
                else
                {
                    // Restore the pre-modify content.
                    File.Copy(result.BackupPath, fullPath, true);
                }
                File.Delete(result.BackupPath);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("CodePatcher rollback: " + ex.Message);
                return false;
            }
        }

        // Generate a unified diff between two text snapshots. Uses a
        // common-prefix / common-suffix hunk strategy: good for the small,
        // localized edits a coding agent makes, and deterministic to test.
        public string GenerateDiff(string oldText, string newText, string filePath)
        {
            string[] oldLines = SplitLines(oldText);
            string[] newLines = SplitLines(newText);

            // Find common prefix.
            int prefix = 0;
            int minLen = Math.Min(oldLines.Length, newLines.Length);
            while (prefix < minLen && oldLines[prefix] == newLines[prefix]) prefix++;

            // If identical, no diff.
            if (prefix == oldLines.Length && prefix == newLines.Length)
                return "";

            // Find common suffix.
            int suffix = 0;
            while (suffix < (minLen - prefix) &&
                   oldLines[oldLines.Length - 1 - suffix] == newLines[newLines.Length - 1 - suffix])
                suffix++;

            int oldStart = prefix;
            int oldEnd = oldLines.Length - suffix;
            int newStart = prefix;
            int newEnd = newLines.Length - suffix;

            var sb = new StringBuilder();
            sb.AppendLine("--- a/" + filePath);
            sb.AppendLine("+++ b/" + filePath);

            int oldHunkLen = oldEnd - oldStart;
            int newHunkLen = newEnd - newStart;
            if (oldHunkLen == 0 && newHunkLen == 0) return sb.ToString().Trim();

            // Hunk header: @@ -oldStart,oldLen +newStart,newLen @@
            int oldDisplay = oldLines.Length == 0 ? 0 : oldStart + 1;
            int newDisplay = newLines.Length == 0 ? 0 : newStart + 1;
            sb.AppendLine("@@ -" + oldDisplay + "," + oldHunkLen + " +" + newDisplay + "," + newHunkLen + " @@");

            for (int i = oldStart; i < oldEnd; i++)
                sb.AppendLine("-" + oldLines[i]);
            for (int i = newStart; i < newEnd; i++)
                sb.AppendLine("+" + newLines[i]);

            return sb.ToString().TrimEnd('\r', '\n');
        }

        static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return new string[0];
            // Normalize line endings then split, keeping content without terminators.
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return normalized.Split('\n');
        }

        static string NormalizeLineEndings(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        string ValidatePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return "empty file path";
            if (Path.IsPathRooted(filePath))
                return "file path must be relative to the project root, got absolute path: " + filePath;
            var parts = filePath.Split(new[] { '\\', '/' }, StringSplitOptions.None);
            foreach (var p in parts)
            {
                if (p == "..") return "file path escapes project root (..): " + filePath;
                if (p == ".") return "file path contains . component: " + filePath;
            }
            return null;
        }
    }
}
