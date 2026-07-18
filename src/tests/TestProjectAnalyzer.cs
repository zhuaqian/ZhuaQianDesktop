using System;
using System.IO;
using ZhuaQianDesktopApp.Agent.Coding;

namespace ZhuaQianDesktopApp.Tests
{
    public static class TestProjectAnalyzer
    {
        public static int RunAll()
        {
            int failures = 0;
            string tmp = null;
            try
            {
                tmp = Path.Combine(Path.GetTempPath(), "zqpa_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);

                failures += TestCSharpProject(tmp);
                failures += TestNodeProject(tmp);
                failures += TestPythonProject(tmp);
                failures += TestEmptyDir(tmp);
                failures += TestNonExistent();
            }
            catch (Exception ex)
            {
                Console.WriteLine("TestProjectAnalyzer CRASH: " + ex.Message);
                failures++;
            }
            finally
            {
                try { if (tmp != null && Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
            return failures;
        }

        static int TestCSharpProject(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "csharp");
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(Path.Combine(root, "build.ps1"), "echo build");
            File.WriteAllText(Path.Combine(root, "src", "scripts", "run-tests.ps1"), "echo test");
            File.WriteAllText(Path.Combine(root, "src", "Program.cs"), "class Program { static void Main() {} }");
            File.WriteAllText(Path.Combine(root, "ZhuaQianDesktop.csproj"),
                "<Project><PropertyGroup><TargetFrameworkVersion>v4.8</TargetFrameworkVersion></PropertyGroup></Project>");

            var analyzer = new ProjectAnalyzer();
            var profile = analyzer.Analyze(root);

            int f = 0;
            Assert(ref f, profile.Language == ProjectLanguage.CSharp, "CSharp language detected, got " + profile.Language);
            Assert(ref f, profile.HasBuild, "build tool detected (build.ps1)");
            Assert(ref f, profile.BuildCommand.Contains("build.ps1"), "build command references build.ps1, got: " + profile.BuildCommand);
            Assert(ref f, profile.HasTest, "test tool detected (run-tests.ps1)");
            Assert(ref f, profile.TestCommand.Contains("run-tests.ps1"), "test command references run-tests.ps1, got: " + profile.TestCommand);
            Assert(ref f, profile.Framework.Contains("4.8") || profile.Framework.Contains(".NET"), "framework detected, got: " + profile.Framework);
            Assert(ref f, profile.EntryFile.Contains("Program.cs"), "entry file is Program.cs, got: " + profile.EntryFile);
            Assert(ref f, profile.PackageFile.Contains(".csproj"), "package file is .csproj, got: " + profile.PackageFile);
            return f;
        }

        static int TestNodeProject(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "node");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "package.json"),
                "{\"name\":\"demo\",\"scripts\":{\"build\":\"tsc\",\"test\":\"jest\"},\"dependencies\":{\"react\":\"^18\"}}");
            File.WriteAllText(Path.Combine(root, "index.js"), "console.log('hi')");

            var analyzer = new ProjectAnalyzer();
            var profile = analyzer.Analyze(root);

            int f = 0;
            Assert(ref f, profile.Language == ProjectLanguage.JavaScript || profile.Language == ProjectLanguage.Mixed,
                "JS language detected, got " + profile.Language);
            Assert(ref f, profile.HasBuild, "build tool detected (npm run build)");
            Assert(ref f, profile.BuildCommand.Contains("npm run build") || profile.BuildCommand.Contains("npm"),
                "build command is npm-based, got: " + profile.BuildCommand);
            Assert(ref f, profile.HasTest, "test tool detected (npm test)");
            Assert(ref f, profile.TestCommand.Contains("npm test"), "test command is npm test, got: " + profile.TestCommand);
            Assert(ref f, profile.Framework.Contains("React") || profile.Framework.Contains("Node"), "framework detected, got: " + profile.Framework);
            return f;
        }

        static int TestPythonProject(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "py");
            Directory.CreateDirectory(Path.Combine(root, "tests"));
            File.WriteAllText(Path.Combine(root, "main.py"), "print('hi')");
            File.WriteAllText(Path.Combine(root, "pyproject.toml"), "[project]\nname='demo'");

            var analyzer = new ProjectAnalyzer();
            var profile = analyzer.Analyze(root);

            int f = 0;
            Assert(ref f, profile.Language == ProjectLanguage.Python, "Python language detected, got " + profile.Language);
            Assert(ref f, profile.EntryFile.Contains("main.py"), "entry is main.py, got: " + profile.EntryFile);
            Assert(ref f, profile.PackageFile.Contains("pyproject"), "package is pyproject.toml, got: " + profile.PackageFile);
            return f;
        }

        static int TestEmptyDir(string baseTmp)
        {
            string root = Path.Combine(baseTmp, "empty");
            Directory.CreateDirectory(root);

            var analyzer = new ProjectAnalyzer();
            var profile = analyzer.Analyze(root);

            int f = 0;
            Assert(ref f, profile.Language == ProjectLanguage.Unknown, "empty dir => Unknown language, got " + profile.Language);
            Assert(ref f, !profile.HasBuild, "empty dir has no build tool");
            Assert(ref f, !profile.HasTest, "empty dir has no test tool");
            Assert(ref f, profile.Notes.Count > 0, "empty dir has notes");
            return f;
        }

        static int TestNonExistent()
        {
            var analyzer = new ProjectAnalyzer();
            var profile = analyzer.Analyze(Path.Combine(Path.GetTempPath(), "zqpa_nonexistent_" + Guid.NewGuid().ToString("N")));

            int f = 0;
            Assert(ref f, profile.Language == ProjectLanguage.Unknown, "nonexistent dir => Unknown");
            Assert(ref f, profile.Notes.Count > 0 && profile.Notes[0].Contains("does not exist"), "nonexistent dir has note");
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
