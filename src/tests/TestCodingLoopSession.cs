using System;
using System.Collections.Generic;
using System.IO;
using ZhuaQianDesktopApp.Agent.Coding;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp.Tests
{
    public static class TestCodingLoopSession
    {
        public static int RunAll()
        {
            int failures = 0;
            string tmp = null;
            try
            {
                tmp = Path.Combine(Path.GetTempPath(), "zqcls_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);

                failures += TestFullLoopFixMissingSemicolon(tmp);
                failures += TestCannotFixReportsHonestly(tmp);
                failures += TestToMarkdown(tmp);
            }
            catch (Exception ex)
            {
                Console.WriteLine("TestCodingLoopSession CRASH: " + ex.Message);
                failures++;
            }
            finally
            {
                try { if (tmp != null && Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
            return failures;
        }

        sealed class ScriptedRecorder : ICommandRecorder
        {
            readonly Queue<AgentPlanStepResult> queue = new Queue<AgentPlanStepResult>();
            public void Enqueue(AgentPlanStepResult r) { queue.Enqueue(r); }
            public AgentPlanStepResult Run(string fileName, string arguments, string workingDirectory = "")
            {
                if (queue.Count == 0)
                    return new AgentPlanStepResult { Success = true, ExitCode = 0, OutputSummary = "", StartedAt = DateTime.Now, FinishedAt = DateTime.Now };
                return queue.Dequeue();
            }
        }

        static AgentPlanStepResult Pass() { return new AgentPlanStepResult { Success = true, ExitCode = 0, OutputSummary = "OK", StartedAt = DateTime.Now, FinishedAt = DateTime.Now }; }
        static AgentPlanStepResult Fail(string errorOutput) { return new AgentPlanStepResult { Success = false, ExitCode = 1, ErrorSummary = errorOutput, StartedAt = DateTime.Now, FinishedAt = DateTime.Now }; }

        static PermissionGate AllowGate()
        {
            var g = new PermissionGate();
            g.Set("permFileWrite", PermissionLevel.Allow);
            g.Set("permCommandRun", PermissionLevel.Allow);
            g.AutoMode = true;
            return g;
        }

        static void SetupProject(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "src", "scripts"));
            File.WriteAllText(Path.Combine(root, "build.ps1"), "echo build");
            File.WriteAllText(Path.Combine(root, "src", "scripts", "run-tests.ps1"), "echo test");
        }

        // End-to-end: a C# file with a missing semicolon is reported by the
        // fake build, the rule-based strategy fixes it, and the second build
        // passes. This is the "check why this fails and fix it" demo loop.
        static int TestFullLoopFixMissingSemicolon(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "e2e1");
            SetupProject(root);
            string csFile = Path.Combine(root, "Program.cs");
            File.WriteAllText(csFile,
                "using System;\n" +
                "class Program\n" +
                "{\n" +
                "    static void Main()\n" +
                "    {\n" +
                "        Console.WriteLine(\"hello\")\n" +   // line 6: missing semicolon
                "    }\n" +
                "}\n");

            var recorder = new ScriptedRecorder();
            recorder.Enqueue(Fail("Program.cs(6,34): error CS1002: ; expected"));  // iter1 build
            recorder.Enqueue(Pass());  // iter2 build
            recorder.Enqueue(Pass());  // iter2 test

            var session = new CodingLoopSession(root, AllowGate(), recorder, new RuleBasedFixStrategy());
            var report = session.Run("fix the missing semicolon build error");

            int f = 0;
            Assert(ref f, report.Status == CodingLoopStatus.Passed, "loop status Passed, got " + report.Status);
            Assert(ref f, report.Phase == CodingLoopPhase.Done, "phase Done, got " + report.Phase);
            Assert(ref f, report.Profile != null && report.Profile.Language == ProjectLanguage.CSharp, "profile detected CSharp");
            Assert(ref f, report.BuildFixReport != null, "build-fix report present");
            Assert(ref f, report.BuildFixReport.IterationsRun == 2, "2 iterations, got " + (report.BuildFixReport != null ? report.BuildFixReport.IterationsRun : -1));
            Assert(ref f, report.BuildFixReport.AppliedPatches.Count >= 1, "at least 1 patch applied");
            // The file should now contain the semicolon.
            string content = File.ReadAllText(csFile);
            Assert(ref f, content.Contains("Console.WriteLine(\"hello\");"), "semicolon added to source: " + content);
            Assert(ref f, !string.IsNullOrEmpty(report.SuggestedCommitMessage), "commit message suggested");
            Assert(ref f, !string.IsNullOrEmpty(report.GitSummary), "git summary present");
            return f;
        }

        // When the strategy cannot fix the error, the loop reports CannotFix
        // honestly instead of silently looping or applying junk.
        static int TestCannotFixReportsHonestly(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "e2e2");
            SetupProject(root);
            var recorder = new ScriptedRecorder();
            recorder.Enqueue(Fail("Program.cs(1,1): error CS9999: completely unknown error"));

            var session = new CodingLoopSession(root, AllowGate(), recorder, new RuleBasedFixStrategy());
            var report = session.Run("fix unknown error");

            int f = 0;
            Assert(ref f, report.Status == CodingLoopStatus.CannotFix, "status CannotFix, got " + report.Status);
            Assert(ref f, report.Phase == CodingLoopPhase.Done, "phase Done");
            Assert(ref f, report.BuildFixReport.AppliedPatches.Count == 0, "no patches applied");
            Assert(ref f, report.ReviewNotes.Contains("could not fix") || report.ReviewNotes.Contains("Manual review"), "review notes mention manual review: " + report.ReviewNotes);
            return f;
        }

        static int TestToMarkdown(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "e2e3");
            SetupProject(root);
            var recorder = new ScriptedRecorder();
            recorder.Enqueue(Pass()); // build
            recorder.Enqueue(Pass()); // test
            var session = new CodingLoopSession(root, AllowGate(), recorder, new RuleBasedFixStrategy());
            var report = session.Run("verify clean build");
            string md = report.ToMarkdown();

            int f = 0;
            Assert(ref f, md.Contains("Coding Loop Report"), "markdown has title");
            Assert(ref f, md.Contains("Goal"), "markdown has goal");
            Assert(ref f, md.Contains("Status"), "markdown has status");
            Assert(ref f, md.Contains("Project"), "markdown has project section");
            Assert(ref f, md.Contains("Build"), "markdown has build info");
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
