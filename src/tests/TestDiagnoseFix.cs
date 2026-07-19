using System;
using System.Collections.Generic;
using System.IO;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Agent.Coding;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Documents;
using ZhuaQianDesktopApp.Tools;

namespace ZhuaQianDesktopApp.Tests
{
    // Tests for the DiagnoseFix closed-loop command (Epic D4 wiring):
    //  - the executor is registered in the single pipeline (no auto-allow bypass),
    //  - the safe fix strategy maps parsed build errors onto patch operations,
    //  - a manual unified diff is applied THROUGH the pipeline (PatchFile),
    //    honoring the single side-effect pipeline convention.
    public static class TestDiagnoseFix
    {
        public static int RunAll()
        {
            int failures = 0;
            string tmp = null;
            try
            {
                tmp = Path.Combine(Path.GetTempPath(), "zqdf_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);

                failures += TestFactoryRegistersDiagnoseFix(tmp);
                failures += TestSafeFixStrategyMapsError(tmp);
                failures += TestManualDiffAppliedViaPipeline(tmp);
            }
            catch (Exception ex)
            {
                Console.WriteLine("TestDiagnoseFix CRASH: " + ex.Message);
                failures++;
            }
            finally
            {
                try { if (tmp != null && Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
            return failures;
        }

        static AgentPipelineFactory NewFactory(string baseTmp)
        {
            return new AgentPipelineFactory(
                Path.Combine(baseTmp, "audit.log"),
                baseTmp,
                new OutputsHub(baseTmp),
                new OfficeExporter(),
                new WebSearchClient());
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

        static int TestFactoryRegistersDiagnoseFix(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "reg");
            SetupCsharpProject(root);
            var pipeline = NewFactory(baseTmp).Create(AllowGate(), null, false, root);
            int f = 0;
            Assert(ref f, pipeline.HasExecutor("DiagnoseFix"), "factory registers DiagnoseFix executor");
            Assert(ref f, pipeline.HasExecutor("PatchFile"), "factory registers PatchFile executor (loop applies fixes via it)");
            Assert(ref f, pipeline.HasExecutor("GitWorkflow"), "factory registers GitWorkflow executor");
            return f;
        }

        static int TestSafeFixStrategyMapsError(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "strat");
            SetupCsharpProject(root);
            string csFile = Path.Combine(root, "Program.cs");
            File.WriteAllText(csFile,
                "using System;\nclass Program\n{\n    static void Main()\n    {\n        Console.WriteLine(\"hi\")\n    }\n}\n");

            var profile = new ProjectProfile { RootDirectory = root, Language = ProjectLanguage.CSharp };
            var strategy = new DiagnoseFixExecutor.DiagnoseFixFixStrategy(profile);

            var errors = new List<FixLoopRunner.BuildError>
            {
                new FixLoopRunner.BuildError { File = "Program.cs", Line = 6, Message = "CS1002: ; expected" }
            };
            var ops = strategy.Propose(root, null, errors);

            int f = 0;
            Assert(ref f, ops != null && ops.Count == 1, "CS1002 maps to exactly one patch op, got " + (ops == null ? "null" : ops.Count.ToString()));
            if (ops != null && ops.Count > 0)
            {
                Assert(ref f, ops[0].Op == "edit", "op is edit, got " + ops[0].Op);
                Assert(ref f, string.Equals(ops[0].Target, "Program.cs", StringComparison.OrdinalIgnoreCase), "target is Program.cs, got " + ops[0].Target);
                Assert(ref f, ops[0].OldText.IndexOf("Console.WriteLine(\"hi\")") >= 0, "old text contains unterminated line");
                Assert(ref f, ops[0].NewText.IndexOf("Console.WriteLine(\"hi\");") >= 0, "new text contains semicolon");
            }
            return f;
        }

        static int TestManualDiffAppliedViaPipeline(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "manual");
            SetupCsharpProject(root);
            string csFile = Path.Combine(root, "Program.cs");
            File.WriteAllText(csFile,
                "using System;\nclass Program\n{\n    static void Main()\n    {\n        Console.WriteLine(\"hi\")\n    }\n}\n");

            var pipeline = NewFactory(baseTmp).Create(AllowGate(), null, false, root);
            var executor = new DiagnoseFixExecutor(pipeline, root);

            // Minimal unified diff (the format produced by this app's report):
            // add the missing semicolon to line 6 of Program.cs.
            string diff =
                "--- Program.cs\n" +
                "+++ Program.cs\n" +
                "@@\n" +
                "-        Console.WriteLine(\"hi\")\n" +
                "+        Console.WriteLine(\"hi\");\n";

            var parameters = new Dictionary<string, object>
            {
                { "root", root },
                { "patch", diff }
            };
            var command = new AgentCommand("DiagnoseFix", "permFileWrite", "test", root, "manual patch", parameters);
            var result = executor.Execute(command);

            int f = 0;
            Assert(ref f, result != null && result.Status == CommandStatus.Success, "manual patch command succeeds, got " + (result == null ? "null" : result.Status.ToString()));
            string content = File.ReadAllText(csFile);
            Assert(ref f, content.Contains("Console.WriteLine(\"hi\");"), "manual diff applied through pipeline (semicolon present): " + content);
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
