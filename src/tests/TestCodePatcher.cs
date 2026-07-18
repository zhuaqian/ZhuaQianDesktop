using System;
using System.IO;
using ZhuaQianDesktopApp.Agent.Coding;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp.Tests
{
    public static class TestCodePatcher
    {
        public static int RunAll()
        {
            int failures = 0;
            string tmp = null;
            try
            {
                tmp = Path.Combine(Path.GetTempPath(), "zqcp_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);

                failures += TestCreate(tmp);
                failures += TestModifyWithDiff(tmp);
                failures += TestDelete(tmp);
                failures += TestPathTraversalRejected(tmp);
                failures += TestPermissionDenied(tmp);
                failures += TestDryRunNoWrite(tmp);
                failures += TestRollback(tmp);
                failures += TestPreconditionMismatch(tmp);
                failures += TestGenerateDiff();
            }
            catch (Exception ex)
            {
                Console.WriteLine("TestCodePatcher CRASH: " + ex.Message);
                failures++;
            }
            finally
            {
                try { if (tmp != null && Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
            return failures;
        }

        static PermissionGate AllowGate()
        {
            var g = new PermissionGate();
            g.Set("permFileWrite", PermissionLevel.Allow);
            g.AutoMode = true;
            return g;
        }

        static int TestCreate(string root)
        {
            var patcher = new CodePatcher(root, AllowGate());
            var patch = new CodePatch { Operation = PatchOperation.Create, FilePath = "newfile.txt", NewContent = "hello\nworld\n" };
            var result = patcher.Apply(patch);
            int f = 0;
            Assert(ref f, result.Success, "create succeeds");
            Assert(ref f, File.Exists(Path.Combine(root, "newfile.txt")), "file written to disk");
            Assert(ref f, result.DiffText.Contains("+++ b/newfile.txt"), "diff has +++ header: " + result.DiffText);
            Assert(ref f, result.DiffText.Contains("+hello"), "diff has +hello line: " + result.DiffText);
            return f;
        }

        static int TestModifyWithDiff(string root)
        {
            string file = "modify.cs";
            File.WriteAllText(Path.Combine(root, file), "line1\nold\nline3\n");
            var patcher = new CodePatcher(root, AllowGate());
            var patch = new CodePatch { Operation = PatchOperation.Modify, FilePath = file, NewContent = "line1\nnew\nline3\n", Reason = "fix typo" };
            var result = patcher.Apply(patch);
            int f = 0;
            Assert(ref f, result.Success, "modify succeeds");
            string content = File.ReadAllText(Path.Combine(root, file));
            Assert(ref f, content.Contains("new") && !content.Contains("old"), "file content updated");
            Assert(ref f, result.DiffText.Contains("-old"), "diff has -old: " + result.DiffText);
            Assert(ref f, result.DiffText.Contains("+new"), "diff has +new: " + result.DiffText);
            Assert(ref f, result.CanRollback, "backup created for rollback");
            return f;
        }

        static int TestDelete(string root)
        {
            string file = "todelete.txt";
            File.WriteAllText(Path.Combine(root, file), "bye\n");
            var patcher = new CodePatcher(root, AllowGate());
            var patch = new CodePatch { Operation = PatchOperation.Delete, FilePath = file };
            var result = patcher.Apply(patch);
            int f = 0;
            Assert(ref f, result.Success, "delete succeeds");
            Assert(ref f, !File.Exists(Path.Combine(root, file)), "file removed from disk");
            Assert(ref f, result.CanRollback, "backup kept for delete rollback");
            return f;
        }

        static int TestPathTraversalRejected(string root)
        {
            var patcher = new CodePatcher(root, AllowGate());
            var patch = new CodePatch { Operation = PatchOperation.Create, FilePath = "../escape.txt", NewContent = "x" };
            var result = patcher.Apply(patch);
            int f = 0;
            Assert(ref f, !result.Success, "path traversal rejected");
            Assert(ref f, result.ErrorMessage.Contains("escapes") || result.ErrorMessage.Contains("absolute"), "error message mentions escape: " + result.ErrorMessage);
            return f;
        }

        static int TestPermissionDenied(string root)
        {
            var gate = new PermissionGate();
            gate.Set("permFileWrite", PermissionLevel.Deny);
            var patcher = new CodePatcher(root, gate);
            var patch = new CodePatch { Operation = PatchOperation.Create, FilePath = "denied.txt", NewContent = "x" };
            var result = patcher.Apply(patch);
            int f = 0;
            Assert(ref f, !result.Success, "permission denied blocks write");
            Assert(ref f, result.ErrorMessage.Contains("permission denied"), "error mentions permission: " + result.ErrorMessage);
            Assert(ref f, !File.Exists(Path.Combine(root, "denied.txt")), "file NOT written");
            return f;
        }

        static int TestDryRunNoWrite(string root)
        {
            var patcher = new CodePatcher(root, AllowGate());
            var patch = new CodePatch { Operation = PatchOperation.Create, FilePath = "dryrun.txt", NewContent = "preview\n" };
            var result = patcher.DryRun(patch);
            int f = 0;
            Assert(ref f, result.Success, "dry-run succeeds");
            Assert(ref f, result.DryRun, "result marked as dry-run");
            Assert(ref f, !File.Exists(Path.Combine(root, "dryrun.txt")), "dry-run does NOT write to disk");
            Assert(ref f, result.DiffText.Contains("+preview"), "dry-run still produces diff: " + result.DiffText);
            return f;
        }

        static int TestRollback(string root)
        {
            string file = "rollback.cs";
            File.WriteAllText(Path.Combine(root, file), "original\n");
            var patcher = new CodePatcher(root, AllowGate());
            var patch = new CodePatch { Operation = PatchOperation.Modify, FilePath = file, NewContent = "changed\n" };
            var result = patcher.Apply(patch);
            int f = 0;
            Assert(ref f, result.Success, "modify before rollback");
            Assert(ref f, File.ReadAllText(Path.Combine(root, file)).Contains("changed"), "content changed");
            bool ok = patcher.Rollback(result);
            Assert(ref f, ok, "rollback returns true");
            Assert(ref f, File.ReadAllText(Path.Combine(root, file)).Contains("original"), "content restored after rollback");
            return f;
        }

        static int TestPreconditionMismatch(string root)
        {
            string file = "precond.txt";
            File.WriteAllText(Path.Combine(root, file), "actual\n");
            var patcher = new CodePatcher(root, AllowGate());
            var patch = new CodePatch { Operation = PatchOperation.Modify, FilePath = file, NewContent = "new\n", OldContent = "expected_but_different\n" };
            var result = patcher.Apply(patch);
            int f = 0;
            Assert(ref f, !result.Success, "precondition mismatch blocks write");
            Assert(ref f, result.ErrorMessage.Contains("precondition"), "error mentions precondition: " + result.ErrorMessage);
            Assert(ref f, File.ReadAllText(Path.Combine(root, file)) == "actual\n", "file unchanged");
            return f;
        }

        static int TestGenerateDiff()
        {
            var patcher = new CodePatcher(Directory.GetCurrentDirectory(), new PermissionGate());
            string diff = patcher.GenerateDiff("a\nb\nc\n", "a\nB\nc\n", "test.txt");
            int f = 0;
            Assert(ref f, diff.Contains("-b"), "diff has -b: " + diff);
            Assert(ref f, diff.Contains("+B"), "diff has +B: " + diff);
            Assert(ref f, diff.Contains("@@"), "diff has hunk header: " + diff);

            // Identical content => empty diff.
            string same = patcher.GenerateDiff("x\ny\n", "x\ny\n", "same.txt");
            Assert(ref f, string.IsNullOrEmpty(same), "identical content => empty diff, got: " + same);
            return f;
        }

        static void Assert(ref int failures, bool condition, string message)
        {
            if (condition)
                Console.WriteLine("  PASS: " + message);
            else
            {
                Console.WriteLine("  FAIL: " + message);
                failures++;
            }
        }
    }
}
