using System;
using System.Collections.Generic;
using System.IO;
using ZhuaQianDesktopApp.Agent.Coding;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp.Tests
{
    public static class TestBuildFixLoop
    {
        public static int RunAll()
        {
            int failures = 0;
            string tmp = null;
            try
            {
                tmp = Path.Combine(Path.GetTempPath(), "zqbfl_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);

                failures += TestPassOnFirstTry(tmp);
                failures += TestFixThenPass(tmp);
                failures += TestCannotFix(tmp);
                failures += TestNonConvergent(tmp);
                failures += TestExhausted(tmp);
                failures += TestRuleBasedMissingSemicolon(tmp);
                failures += TestRuleBasedMissingUsing(tmp);
            }
            catch (Exception ex)
            {
                Console.WriteLine("TestBuildFixLoop CRASH: " + ex.Message);
                failures++;
            }
            finally
            {
                try { if (tmp != null && Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
            return failures;
        }

        // Fake recorder: returns preset results in order, one per call.
        sealed class ScriptedRecorder : ICommandRecorder
        {
            readonly Queue<AgentPlanStepResult> queue = new Queue<AgentPlanStepResult>();
            public int CallCount;
            public void Enqueue(AgentPlanStepResult r) { queue.Enqueue(r); }
            public AgentPlanStepResult Run(string fileName, string arguments, string workingDirectory = "")
            {
                CallCount++;
                if (queue.Count == 0) return Pass();
                return queue.Dequeue();
            }
        }

        // Fake strategy: returns a fixed list of patches regardless of errors.
        sealed class ScriptedStrategy : IFixStrategy
        {
            public List<CodePatch> Patches;
            public string Name { get { return "scripted"; } }
            public List<CodePatch> SuggestFixes(List<BuildError> errors, ProjectProfile profile) { return Patches ?? new List<CodePatch>(); }
        }

        static AgentPlanStepResult Pass()
        {
            var r = new AgentPlanStepResult();
            r.Success = true; r.ExitCode = 0; r.OutputSummary = "OK"; r.StartedAt = DateTime.Now; r.FinishedAt = DateTime.Now;
            return r;
        }

        static AgentPlanStepResult Fail(string errorOutput, int exitCode = 1)
        {
            var r = new AgentPlanStepResult();
            r.Success = false; r.ExitCode = exitCode; r.ErrorSummary = errorOutput; r.StartedAt = DateTime.Now; r.FinishedAt = DateTime.Now;
            return r;
        }

        static PermissionGate AllowGate()
        {
            var g = new PermissionGate();
            g.Set("permFileWrite", PermissionLevel.Allow);
            g.AutoMode = true;
            return g;
        }

        static void SetupCsharpProject(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "src", "scripts"));
            File.WriteAllText(Path.Combine(root, "build.ps1"), "echo build");
            File.WriteAllText(Path.Combine(root, "src", "scripts", "run-tests.ps1"), "echo test");
        }

        static int TestPassOnFirstTry(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "p1");
            SetupCsharpProject(root);
            var recorder = new ScriptedRecorder();
            recorder.Enqueue(Pass()); // build
            recorder.Enqueue(Pass()); // test
            var loop = new BuildFixLoop(root, AllowGate(), recorder, new ScriptedStrategy());
            var report = loop.Run(new BuildFixLoopOptions { RootDirectory = root, MaxIterations = 3 });
            int f = 0;
            Assert(ref f, report.Status == BuildFixLoopStatus.Passed, "pass on first try => Passed, got " + report.Status);
            Assert(ref f, report.IterationsRun == 1, "1 iteration, got " + report.IterationsRun);
            Assert(ref f, report.AppliedPatches.Count == 0, "no patches applied");
            Assert(ref f, report.BuildResults.Count == 1, "1 build result");
            return f;
        }

        static int TestFixThenPass(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "p2");
            SetupCsharpProject(root);
            File.WriteAllText(Path.Combine(root, "src", "dummy.txt"), "x");
            var recorder = new ScriptedRecorder();
            recorder.Enqueue(Fail("Program.cs(3,1): error CS1002: ; expected")); // iter1 build fail
            recorder.Enqueue(Pass()); // iter2 build pass
            recorder.Enqueue(Pass()); // iter2 test pass
            var strategy = new ScriptedStrategy();
            strategy.Patches = new List<CodePatch> {
                new CodePatch { Operation = PatchOperation.Modify, FilePath = "src/dummy.txt", NewContent = "fixed\n", OldContent = "x" }
            };
            var loop = new BuildFixLoop(root, AllowGate(), recorder, strategy);
            var report = loop.Run(new BuildFixLoopOptions { RootDirectory = root, MaxIterations = 3 });
            int f = 0;
            Assert(ref f, report.Status == BuildFixLoopStatus.Passed, "fix then pass => Passed, got " + report.Status);
            Assert(ref f, report.IterationsRun == 2, "2 iterations, got " + report.IterationsRun);
            Assert(ref f, report.AppliedPatches.Count == 1, "1 patch applied");
            Assert(ref f, report.AppliedPatches[0].Success, "patch succeeded");
            Assert(ref f, File.ReadAllText(Path.Combine(root, "src", "dummy.txt")) == "fixed\n", "file content updated by patch");
            return f;
        }

        static int TestCannotFix(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "p3");
            SetupCsharpProject(root);
            var recorder = new ScriptedRecorder();
            recorder.Enqueue(Fail("Program.cs(1,1): error CS9999: mysterious unknown error"));
            var loop = new BuildFixLoop(root, AllowGate(), recorder, new ScriptedStrategy()); // no patches
            var report = loop.Run(new BuildFixLoopOptions { RootDirectory = root, MaxIterations = 3 });
            int f = 0;
            Assert(ref f, report.Status == BuildFixLoopStatus.CannotFix, "no fix => CannotFix, got " + report.Status);
            Assert(ref f, report.StopReason.Contains("no patch") || report.StopReason.Contains("no fix"), "stop reason mentions no patch: " + report.StopReason);
            return f;
        }

        static int TestNonConvergent(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "p4");
            SetupCsharpProject(root);
            File.WriteAllText(Path.Combine(root, "src", "stub.txt"), "v1");
            var recorder = new ScriptedRecorder();
            // Two identical CS1002 errors => same signature => non-convergent stop.
            recorder.Enqueue(Fail("Program.cs(3,1): error CS1002: ; expected"));
            recorder.Enqueue(Fail("Program.cs(3,1): error CS1002: ; expected"));
            // Strategy returns a patch each time (but the fake recorder keeps failing identically).
            var strategy = new ScriptedStrategy();
            strategy.Patches = new List<CodePatch> {
                new CodePatch { Operation = PatchOperation.Modify, FilePath = "src/stub.txt", NewContent = "v2\n", OldContent = "v1" }
            };
            var loop = new BuildFixLoop(root, AllowGate(), recorder, strategy);
            var report = loop.Run(new BuildFixLoopOptions { RootDirectory = root, MaxIterations = 5 });
            int f = 0;
            Assert(ref f, report.Status == BuildFixLoopStatus.CannotFix, "non-convergent => CannotFix, got " + report.Status);
            Assert(ref f, report.StopReason.Contains("non-convergent"), "stop reason mentions non-convergent: " + report.StopReason);
            Assert(ref f, report.IterationsRun == 2, "stopped at iteration 2, got " + report.IterationsRun);
            return f;
        }

        static int TestExhausted(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "p5");
            SetupCsharpProject(root);
            // Recorder keeps returning DIFFERENT errors each call so signatures differ
            // and convergence check never triggers, but strategy keeps returning patches
            // that change the file. After MaxIterations it should be Exhausted.
            File.WriteAllText(Path.Combine(root, "src", "counter.txt"), "0");
            int counter = 0;
            var recorder = new ScriptedRecorder();
            for (int i = 0; i < 10; i++)
            {
                counter++;
                recorder.Enqueue(Fail("Program.cs(" + counter + ",1): error CS1002: ; expected"));
            }
            // Strategy: each call rewrites counter.txt with a new value (different OldContent => different content).
            int patchCounter = 0;
            var strategy = new DynamicStrategy(() =>
            {
                patchCounter++;
                return new List<CodePatch> {
                    new CodePatch { Operation = PatchOperation.Modify, FilePath = "src/counter.txt",
                        NewContent = patchCounter.ToString() + "\n" }
                };
            });
            var loop = new BuildFixLoop(root, AllowGate(), recorder, strategy);
            var report = loop.Run(new BuildFixLoopOptions { RootDirectory = root, MaxIterations = 3 });
            int f = 0;
            Assert(ref f, report.Status == BuildFixLoopStatus.Exhausted, "exhausted => Exhausted, got " + report.Status);
            Assert(ref f, report.IterationsRun == 3, "ran 3 iterations (MaxIterations), got " + report.IterationsRun);
            return f;
        }

        sealed class DynamicStrategy : IFixStrategy
        {
            readonly Func<List<CodePatch>> factory;
            public DynamicStrategy(Func<List<CodePatch>> factory) { this.factory = factory; }
            public string Name { get { return "dynamic"; } }
            public List<CodePatch> SuggestFixes(List<BuildError> errors, ProjectProfile profile) { return factory(); }
        }

        // ---- RuleBasedFixStrategy: real file fixes ----

        static int TestRuleBasedMissingSemicolon(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "rb1");
            SetupCsharpProject(root);
            string csFile = Path.Combine(root, "Program.cs");
            File.WriteAllText(csFile, "using System;\nclass Program\n{\n    static void Main()\n    {\n        Console.WriteLine(\"hi\")\n    }\n}\n");
            // The missing semicolon is on line 6.
            var strategy = new RuleBasedFixStrategy();
            var errors = new List<BuildError> {
                new BuildError { Severity = ErrorSeverity.Error, File = "Program.cs", Line = 6, Column = 9, Code = "CS1002", Message = "; expected" }
            };
            var profile = new ProjectProfile { RootDirectory = root, Language = ProjectLanguage.CSharp };
            var patches = strategy.SuggestFixes(errors, profile);
            int f = 0;
            Assert(ref f, patches.Count == 1, "one patch suggested, got " + patches.Count);
            if (patches.Count > 0)
            {
                // Apply the patch.
                var patcher = new CodePatcher(root, AllowGate());
                var result = patcher.Apply(patches[0]);
                Assert(ref f, result.Success, "patch applied");
                string content = File.ReadAllText(csFile);
                Assert(ref f, content.Contains("Console.WriteLine(\"hi\");"), "semicolon added: " + content);
            }
            return f;
        }

        static int TestRuleBasedMissingUsing(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "rb2");
            SetupCsharpProject(root);
            string csFile = Path.Combine(root, "Program.cs");
            File.WriteAllText(csFile, "class Program\n{\n    static void Main()\n    {\n        var f = File.ReadAllText(\"x\");\n    }\n}\n");
            var strategy = new RuleBasedFixStrategy();
            var errors = new List<BuildError> {
                new BuildError { Severity = ErrorSeverity.Error, File = "Program.cs", Line = 5, Column = 13, Code = "CS0246", Message = "The type or namespace name 'File' could not be found" }
            };
            var profile = new ProjectProfile { RootDirectory = root, Language = ProjectLanguage.CSharp };
            var patches = strategy.SuggestFixes(errors, profile);
            int f = 0;
            Assert(ref f, patches.Count == 1, "one using patch suggested, got " + patches.Count);
            if (patches.Count > 0)
            {
                var patcher = new CodePatcher(root, AllowGate());
                var result = patcher.Apply(patches[0]);
                Assert(ref f, result.Success, "using patch applied");
                string content = File.ReadAllText(csFile);
                Assert(ref f, content.Contains("using System.IO;"), "using System.IO added: " + content);
            }
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
