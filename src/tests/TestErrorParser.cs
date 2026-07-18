using System;
using System.IO;
using ZhuaQianDesktopApp.Agent.Coding;

namespace ZhuaQianDesktopApp.Tests
{
    public static class TestErrorParser
    {
        public static int RunAll()
        {
            int failures = 0;
            try
            {
                failures += TestMsbuildFormat();
                failures += TestMsbuildNoCol();
                failures += TestPowershellFormat();
                failures += TestPythonFormat();
                failures += TestGoFormat();
                failures += TestRustFormat();
                failures += TestGenericError();
                failures += TestEmpty();
                failures += TestToMarkdown();
            }
            catch (Exception ex)
            {
                Console.WriteLine("TestErrorParser CRASH: " + ex.Message);
                failures++;
            }
            return failures;
        }

        static int TestMsbuildFormat()
        {
            var parser = new ErrorParser();
            var errors = parser.Parse("", "Program.cs(10,5): error CS0246: The type or namespace name 'Foo' could not be found", "msbuild");
            int f = 0;
            Assert(ref f, errors.Count >= 1, "one error parsed, got " + errors.Count);
            if (errors.Count > 0)
            {
                var e = errors[0];
                Assert(ref f, e.File.Contains("Program.cs"), "file is Program.cs, got: " + e.File);
                Assert(ref f, e.Line == 10, "line is 10, got: " + e.Line);
                Assert(ref f, e.Column == 5, "column is 5, got: " + e.Column);
                Assert(ref f, e.Code == "CS0246", "code is CS0246, got: " + e.Code);
                Assert(ref f, e.Severity == ErrorSeverity.Error, "severity is Error");
                Assert(ref f, e.Message.Contains("Foo"), "message contains Foo");
                Assert(ref f, e.Tool == "msbuild", "tool is msbuild, got: " + e.Tool);
            }
            return f;
        }

        static int TestMsbuildNoCol()
        {
            var parser = new ErrorParser();
            var errors = parser.Parse("", "App.cs(42): warning CS0168: The variable 'x' is declared but never used", "msbuild");
            int f = 0;
            Assert(ref f, errors.Count >= 1, "one warning parsed, got " + errors.Count);
            if (errors.Count > 0)
            {
                var e = errors[0];
                Assert(ref f, e.Line == 42, "line is 42");
                Assert(ref f, e.Severity == ErrorSeverity.Warning, "severity is Warning");
                Assert(ref f, e.Code == "CS0168", "code is CS0168");
            }
            return f;
        }

        static int TestPowershellFormat()
        {
            var parser = new ErrorParser();
            string err = "At C:\\build.ps1:15 char:5\n+     Write-Hosst 'hi'\n+     ~~~~~~~~~~~~\n    The term 'Write-Hosst' is not recognized";
            var errors = parser.Parse("", err, "powershell");
            int f = 0;
            Assert(ref f, errors.Count >= 1, "powershell error parsed, got " + errors.Count);
            if (errors.Count > 0)
            {
                var e = errors[0];
                Assert(ref f, e.Tool == "powershell", "tool is powershell");
                Assert(ref f, e.File.Contains("build.ps1"), "file contains build.ps1, got: " + e.File);
                Assert(ref f, e.Line == 15, "line is 15, got: " + e.Line);
                Assert(ref f, e.Message.Contains("Write-Hosst") || e.Message.Contains("not recognized"), "message captured: " + e.Message);
            }
            return f;
        }

        static int TestPythonFormat()
        {
            var parser = new ErrorParser();
            string err = "Traceback (most recent call last):\n  File \"app.py\", line 10, in <module>\n    foo()\nNameError: name 'foo' is not defined";
            var errors = parser.Parse("", err, "python");
            int f = 0;
            Assert(ref f, errors.Count >= 1, "python errors parsed, got " + errors.Count);
            // Should have at least one error with a python tool tag.
            bool hasPythonError = false;
            foreach (var e in errors)
            {
                if (e.Tool == "python" && e.Severity == ErrorSeverity.Error)
                {
                    hasPythonError = true;
                    Assert(ref f, e.Code.Contains("Error"), "python code contains Error, got: " + e.Code);
                    Assert(ref f, e.Message.Contains("foo") || e.Message.Contains("defined"), "python message captured: " + e.Message);
                }
            }
            Assert(ref f, hasPythonError, "found a python error record");
            return f;
        }

        static int TestGoFormat()
        {
            var parser = new ErrorParser();
            var errors = parser.Parse("", "main.go:10:5: undefined: Foo", "go");
            int f = 0;
            Assert(ref f, errors.Count >= 1, "go error parsed, got " + errors.Count);
            if (errors.Count > 0)
            {
                var e = errors[0];
                var goErr = FindTool(errors, "go");
                if (goErr != null)
                {
                    Assert(ref f, goErr.File.Contains("main.go"), "go file is main.go, got: " + goErr.File);
                    Assert(ref f, goErr.Line == 10, "go line is 10");
                    Assert(ref f, goErr.Message.Contains("undefined"), "go message contains undefined: " + goErr.Message);
                }
            }
            return f;
        }

        static int TestRustFormat()
        {
            var parser = new ErrorParser();
            string err = "error[E0309]: types in types cannot be nested\n  --> src/main.rs:10:5\n   |\n10 |     let x: Vec<Vec> = vec![];";
            var errors = parser.Parse("", err, "rust");
            int f = 0;
            Assert(ref f, errors.Count >= 1, "rust error parsed, got " + errors.Count);
            var rustErr = FindTool(errors, "rust");
            if (rustErr != null)
            {
                Assert(ref f, rustErr.Code == "E0309", "rust code is E0309, got: " + rustErr.Code);
                Assert(ref f, rustErr.File.Contains("main.rs"), "rust file is main.rs, got: " + rustErr.File);
                Assert(ref f, rustErr.Line == 10, "rust line is 10, got: " + rustErr.Line);
            }
            return f;
        }

        static int TestGenericError()
        {
            var parser = new ErrorParser();
            var errors = parser.Parse("", "error: something went wrong here", "generic");
            int f = 0;
            Assert(ref f, errors.Count >= 1, "generic error parsed, got " + errors.Count);
            if (errors.Count > 0)
            {
                var e = errors[0];
                Assert(ref f, e.Severity == ErrorSeverity.Error, "severity is Error");
                Assert(ref f, e.Message.Contains("something went wrong"), "message captured: " + e.Message);
            }
            return f;
        }

        static int TestEmpty()
        {
            var parser = new ErrorParser();
            var errors = parser.Parse("", "", "");
            int f = 0;
            Assert(ref f, errors.Count == 0, "empty input => 0 errors, got " + errors.Count);
            return f;
        }

        static int TestToMarkdown()
        {
            var parser = new ErrorParser();
            var errors = parser.Parse("", "Program.cs(1,1): error CS1001: test error\nProgram.cs(2,1): warning CS1002: test warn", "msbuild");
            string md = parser.ToMarkdown(errors);
            int f = 0;
            Assert(ref f, md.Contains("1 errors") || md.Contains("1 error"), "markdown shows error count: " + md);
            Assert(ref f, md.Contains("CS1001"), "markdown lists CS1001: " + md);
            return f;
        }

        static BuildError FindTool(System.Collections.Generic.List<BuildError> errors, string tool)
        {
            foreach (var e in errors) if (e.Tool == tool) return e;
            return null;
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
