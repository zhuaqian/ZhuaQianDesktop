using System;
using System.Collections.Generic;
using System.IO;
using ZhuaQianDesktopApp.Agent.Coding;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;
using CodingGitWorkflow = ZhuaQianDesktopApp.Agent.Coding.GitWorkflow;

namespace ZhuaQianDesktopApp.Tests
{
    public static class TestGitWorkflow
    {
        public static int RunAll()
        {
            int failures = 0;
            string tmp = null;
            try
            {
                tmp = Path.Combine(Path.GetTempPath(), "zqgit_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);

                failures += TestStatusParse(tmp);
                failures += TestSuggestCommitMessage(tmp);
                failures += TestSuggestCommitMessageDocs(tmp);
                failures += TestDiff(tmp);
                failures += TestCreateBranch(tmp);
                failures += TestPermissionDenied(tmp);
                failures += TestExportPatch(tmp);
                failures += TestInvalidBranchName(tmp);
            }
            catch (Exception ex)
            {
                Console.WriteLine("TestGitWorkflow CRASH: " + ex.Message);
                failures++;
            }
            finally
            {
                try { if (tmp != null && Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
            return failures;
        }

        sealed class FakeRecorder : ICommandRecorder
        {
            public Queue<AgentPlanStepResult> Results = new Queue<AgentPlanStepResult>();
            public List<string> Calls = new List<string>();
            public AgentPlanStepResult Run(string fileName, string arguments, string workingDirectory = "")
            {
                Calls.Add(fileName + " " + arguments);
                if (Results.Count == 0) return new AgentPlanStepResult { Success = true, ExitCode = 0, OutputSummary = "" };
                return Results.Dequeue();
            }
        }

        static AgentPlanStepResult Ok(string stdout) { return new AgentPlanStepResult { Success = true, ExitCode = 0, OutputSummary = stdout }; }
        static AgentPlanStepResult Fail(string stderr) { return new AgentPlanStepResult { Success = false, ExitCode = 1, ErrorSummary = stderr }; }

        static PermissionGate AllowGate()
        {
            var g = new PermissionGate();
            g.Set("permCommandRun", PermissionLevel.Allow);
            g.AutoMode = true;
            return g;
        }

        static int TestStatusParse(string root)
        {
            var rec = new FakeRecorder();
            rec.Results.Enqueue(Ok(" M src/Program.cs\n?? src/new.cs\nA  installer/Install.ps1\n"));
            var git = new CodingGitWorkflow(root, AllowGate(), rec);
            var entries = git.Status();
            int f = 0;
            Assert(ref f, entries.Count == 3, "3 status entries, got " + entries.Count);
            if (entries.Count == 3)
            {
                Assert(ref f, entries[0].IsModified, "entry 0 is modified");
                Assert(ref f, entries[0].Path == "src/Program.cs", "entry 0 path, got: " + entries[0].Path);
                Assert(ref f, entries[1].IsNew, "entry 1 is new");
                Assert(ref f, entries[2].Path.Contains("Install.ps1"), "entry 2 path");
            }
            return f;
        }

        static int TestSuggestCommitMessage(string root)
        {
            var rec = new FakeRecorder();
            rec.Results.Enqueue(Ok(" M src/Program.cs\n M src/Agent/Coding/CodePatcher.cs\n"));
            var git = new CodingGitWorkflow(root, AllowGate(), rec);
            string msg = git.SuggestCommitMessage();
            int f = 0;
            Assert(ref f, msg.Contains("fix"), "message type is fix for source changes, got: " + msg);
            Assert(ref f, msg.Contains("(src)"), "message scope is src, got: " + msg);
            Assert(ref f, msg.Contains("update"), "message has subject, got: " + msg);
            return f;
        }

        static int TestSuggestCommitMessageDocs(string root)
        {
            var rec = new FakeRecorder();
            rec.Results.Enqueue(Ok(" M docs/README.md\n M docs/ARCH.md\n"));
            var git = new CodingGitWorkflow(root, AllowGate(), rec);
            string msg = git.SuggestCommitMessage();
            int f = 0;
            Assert(ref f, msg.StartsWith("docs"), "message type is docs for doc-only changes, got: " + msg);
            Assert(ref f, msg.Contains("(docs)"), "message scope is docs, got: " + msg);
            return f;
        }

        static int TestDiff(string root)
        {
            var rec = new FakeRecorder();
            rec.Results.Enqueue(Ok("diff --git a/file b/file\n@@ -1,1 +1,1 @@\n-old\n+new\n"));
            var git = new CodingGitWorkflow(root, AllowGate(), rec);
            string diff = git.Diff();
            int f = 0;
            Assert(ref f, diff.Contains("-old"), "diff has -old: " + diff);
            Assert(ref f, diff.Contains("+new"), "diff has +new: " + diff);
            return f;
        }

        static int TestCreateBranch(string root)
        {
            var rec = new FakeRecorder();
            rec.Results.Enqueue(Ok("Switched to a new branch 'fix/typo'"));
            var git = new CodingGitWorkflow(root, AllowGate(), rec);
            var result = git.CreateBranch("fix/typo");
            int f = 0;
            Assert(ref f, result.Ok, "create branch succeeds");
            Assert(ref f, rec.Calls.Count > 0 && rec.Calls[0].Contains("checkout -b fix/typo"), "called git checkout -b: " + (rec.Calls.Count > 0 ? rec.Calls[0] : "(none)"));
            return f;
        }

        static int TestPermissionDenied(string root)
        {
            var gate = new PermissionGate();
            gate.Set("permCommandRun", PermissionLevel.Deny);
            var rec = new FakeRecorder();
            var git = new CodingGitWorkflow(root, gate, rec);
            var entries = git.Status();
            int f = 0;
            Assert(ref f, entries.Count == 0, "denied => empty status");
            Assert(ref f, rec.Calls.Count == 0, "recorder never called when denied");
            return f;
        }

        static int TestExportPatch(string root)
        {
            var rec = new FakeRecorder();
            rec.Results.Enqueue(Ok("diff --git a/f b/f\n-old\n+new\n"));
            var git = new CodingGitWorkflow(root, AllowGate(), rec);
            string patchPath = Path.Combine(root, "out.patch");
            var result = git.ExportPatch(patchPath);
            int f = 0;
            Assert(ref f, result.Ok, "export patch succeeds");
            Assert(ref f, File.Exists(patchPath), "patch file written");
            Assert(ref f, File.ReadAllText(patchPath).Contains("-old"), "patch file has diff content");
            return f;
        }

        static int TestInvalidBranchName(string root)
        {
            var rec = new FakeRecorder();
            var git = new CodingGitWorkflow(root, AllowGate(), rec);
            var result = git.CreateBranch("bad; name");
            int f = 0;
            Assert(ref f, !result.Ok, "invalid branch name rejected");
            Assert(ref f, rec.Calls.Count == 0, "recorder not called for invalid name");
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
