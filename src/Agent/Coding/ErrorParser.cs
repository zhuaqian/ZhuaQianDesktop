using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ZhuaQianDesktopApp.Agent.Coding
{
    public enum ErrorSeverity
    {
        Error,
        Warning,
        Info
    }

    // A single structured build/test error extracted from compiler or test
    // output. Carries the file:line:col location and error code so a fix
    // strategy can locate the problem precisely.
    public sealed class BuildError
    {
        public ErrorSeverity Severity = ErrorSeverity.Error;
        public string File = "";
        public int Line;
        public int Column;
        public string Code = "";       // "CS0246", "E0309", "TS2304" ...
        public string Message = "";
        public string RawLine = "";
        public string Tool = "";       // "msbuild", "csc", "python", "go", "rust", "powershell", "jest" ...

        public bool HasLocation { get { return !string.IsNullOrEmpty(File) && Line > 0; } }

        public string Location()
        {
            if (!HasLocation) return "(no location)";
            string loc = File + ":" + Line.ToString();
            if (Column > 0) loc += ":" + Column.ToString();
            return loc;
        }

        public override string ToString()
        {
            return Severity.ToString().ToLowerInvariant() + " " + Location() + (string.IsNullOrEmpty(Code) ? "" : " [" + Code + "]") + ": " + Message;
        }
    }

    // Parses compiler / test-runner output into a list of structured BuildError
    // records. This is the "read the failure" step that feeds the fix loop:
    // without structured errors the agent cannot know which file:line to patch.
    //
    // Supports MSBuild / csc, PowerShell, Python traceback, Go, Rust, and a
    // generic "error:" / "Error:" fallback. Each format is tried per line; the
    // first match wins. Unmatched lines are kept as Info if they look relevant
    // (contain "error" case-insensitively) so context is not lost.
    public sealed class ErrorParser
    {
        // MSBuild / csc: path(line,col): error CS0246: message [project]
        static readonly Regex MsbuildRe = new Regex(
            @"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s*(?<sev>error|warning)\s+(?<code>[A-Za-z]+\d+):\s*(?<msg>.+)$",
            RegexOptions.Compiled);

        // MSBuild without column: path(line): error CS0246: message
        static readonly Regex MsbuildNoColRe = new Regex(
            @"^(?<file>.+?)\((?<line>\d+)\):\s*(?<sev>error|warning)\s+(?<code>[A-Za-z]+\d+):\s*(?<msg>.+)$",
            RegexOptions.Compiled);

        // PowerShell: At path:line char:col
        static readonly Regex PowershellAtRe = new Regex(
            @"^At\s+(?<file>.+?):(?<line>\d+)\s+char:(?<col>\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // PowerShell parse error: path:line:char
        static readonly Regex PowershellParseRe = new Regex(
            @"^(?<file>.+?):(?<line>\d+):(?<col>\d+)\s*$",
            RegexOptions.Compiled);

        // Python traceback: File "path", line N, in ...
        static readonly Regex PythonFileRe = new Regex(
            @"^\s*File\s+""(?<file>.+?)"",\s+line\s+(?<line>\d+)",
            RegexOptions.Compiled);

        // Python error line: SyntaxError: message (path, line N)
        static readonly Regex PythonErrorRe = new Regex(
            @"^(?<code>[A-Za-z]+Error):\s*(?<msg>.+?)\s*\((?<file>[^,]+),\s+line\s+(?<line>\d+)\)",
            RegexOptions.Compiled);
        static readonly Regex PythonSimpleErrorRe = new Regex(
            @"^(?<code>[A-Za-z]+Error):\s*(?<msg>.+)$",
            RegexOptions.Compiled);

        // Go: path:line:col: message
        static readonly Regex GoRe = new Regex(
            @"^(?<file>[^\s:]+?):(?<line>\d+):(?<col>\d+):\s*(?<msg>.+)$",
            RegexOptions.Compiled);

        // Go without column: path:line: message
        static readonly Regex GoNoColRe = new Regex(
            @"^(?<file>[^\s:]+?):(?<line>\d+):\s*(?<msg>.+)$",
            RegexOptions.Compiled);

        // Rust: error[E0309]: message  /  --> path:line:col
        static readonly Regex RustErrorRe = new Regex(
            @"^error(?:\[(?<code>E\d+)\])?:\s*(?<msg>.+)$",
            RegexOptions.Compiled);
        static readonly Regex RustLocRe = new Regex(
            @"^\s*-->\s*(?<file>.+?):(?<line>\d+):(?<col>\d+)",
            RegexOptions.Compiled);

        // TypeScript / tsc: path(line,col): error TS2304: message
        // (covered by MsbuildRe pattern, but tsc uses TS codes)

        // Jest / vitest: FAIL path/to/test.js
        static readonly Regex JestFailRe = new Regex(
            @"^FAIL\s+(?<file>.+)$",
            RegexOptions.Compiled);

        // Generic: error: message / Error: message
        static readonly Regex GenericErrorRe = new Regex(
            @"^(?<sev>error|Error|ERROR|warning|Warning):\s*(?<msg>.+)$",
            RegexOptions.Compiled);

        public int MaxErrors = 200;

        public List<BuildError> Parse(string output, string errorOutput, string toolHint)
        {
            var errors = new List<BuildError>();
            string combined = (output ?? "") + "\n" + (errorOutput ?? "");
            if (string.IsNullOrWhiteSpace(combined)) return errors;

            string[] lines = combined.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            BuildError pendingRust = null;   // rust error + --> location come on separate lines
            BuildError pendingPwsh = null;   // powershell "At ..." + message on following lines

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Rust: error line then --> location line.
                var mRustErr = RustErrorRe.Match(line);
                if (mRustErr.Success)
                {
                    pendingRust = new BuildError();
                    pendingRust.Tool = "rust";
                    pendingRust.Severity = ErrorSeverity.Error;
                    pendingRust.Code = mRustErr.Groups["code"].Value;
                    pendingRust.Message = mRustErr.Groups["msg"].Value.Trim();
                    pendingRust.RawLine = line;
                    Add(errors, pendingRust);
                    continue;
                }
                var mRustLoc = RustLocRe.Match(line);
                if (mRustLoc.Success && errors.Count > 0)
                {
                    var last = errors[errors.Count - 1];
                    if (last.Tool == "rust" && string.IsNullOrEmpty(last.File))
                    {
                        last.File = mRustLoc.Groups["file"].Value.Trim();
                        int.TryParse(mRustLoc.Groups["line"].Value, out last.Line);
                        int.TryParse(mRustLoc.Groups["col"].Value, out last.Column);
                    }
                    continue;
                }

                // PowerShell: "At file:line char:col" then the message follows.
                var mPwshAt = PowershellAtRe.Match(line);
                if (mPwshAt.Success)
                {
                    pendingPwsh = new BuildError();
                    pendingPwsh.Tool = "powershell";
                    pendingPwsh.Severity = ErrorSeverity.Error;
                    pendingPwsh.File = mPwshAt.Groups["file"].Value.Trim();
                    int.TryParse(mPwshAt.Groups["line"].Value, out pendingPwsh.Line);
                    int.TryParse(mPwshAt.Groups["col"].Value, out pendingPwsh.Column);
                    pendingPwsh.RawLine = line;
                    // Grab the next non-empty line as the message.
                    for (int j = i + 1; j < lines.Length && j <= i + 3; j++)
                    {
                        string next = lines[j].Trim();
                        if (next.Length > 0 && !next.StartsWith("At ", StringComparison.OrdinalIgnoreCase))
                        {
                            pendingPwsh.Message = next;
                            break;
                        }
                    }
                    if (string.IsNullOrEmpty(pendingPwsh.Message)) pendingPwsh.Message = "PowerShell error";
                    Add(errors, pendingPwsh);
                    pendingPwsh = null;
                    continue;
                }

                // MSBuild / csc / tsc with column.
                var mMsbuild = MsbuildRe.Match(line);
                if (mMsbuild.Success)
                {
                    Add(errors, BuildFromMsbuild(mMsbuild, line));
                    continue;
                }
                var mMsbuildNoCol = MsbuildNoColRe.Match(line);
                if (mMsbuildNoCol.Success)
                {
                    Add(errors, BuildFromMsbuild(mMsbuildNoCol, line));
                    continue;
                }

                // Python traceback file line.
                var mPyFile = PythonFileRe.Match(line);
                if (mPyFile.Success)
                {
                    // Defer: the actual error type comes later. Record as Info context.
                    var e = new BuildError();
                    e.Tool = "python";
                    e.Severity = ErrorSeverity.Info;
                    e.File = mPyFile.Groups["file"].Value.Trim();
                    int.TryParse(mPyFile.Groups["line"].Value, out e.Line);
                    e.RawLine = line;
                    e.Message = "traceback at " + e.File + ":" + e.Line;
                    Add(errors, e);
                    continue;
                }
                var mPyErr = PythonErrorRe.Match(line);
                if (mPyErr.Success)
                {
                    var e = new BuildError();
                    e.Tool = "python";
                    e.Severity = ErrorSeverity.Error;
                    e.Code = mPyErr.Groups["code"].Value;
                    e.Message = mPyErr.Groups["msg"].Value.Trim();
                    e.File = mPyErr.Groups["file"].Value.Trim();
                    int.TryParse(mPyErr.Groups["line"].Value, out e.Line);
                    e.RawLine = line;
                    Add(errors, e);
                    continue;
                }
                var mPySimpleErr = PythonSimpleErrorRe.Match(line);
                if (mPySimpleErr.Success)
                {
                    var e = new BuildError();
                    e.Tool = "python";
                    e.Severity = ErrorSeverity.Error;
                    e.Code = mPySimpleErr.Groups["code"].Value;
                    e.Message = mPySimpleErr.Groups["msg"].Value.Trim();
                    e.RawLine = line;
                    var loc = LastPythonLocation(errors);
                    if (loc != null)
                    {
                        e.File = loc.File;
                        e.Line = loc.Line;
                        e.Column = loc.Column;
                    }
                    Add(errors, e);
                    continue;
                }

                // Go.
                var mGo = GoRe.Match(line);
                if (mGo.Success && LooksLikeGoMessage(mGo.Groups["msg"].Value))
                {
                    Add(errors, BuildFromGo(mGo, line));
                    continue;
                }
                var mGoNoCol = GoNoColRe.Match(line);
                if (mGoNoCol.Success && LooksLikeGoMessage(mGoNoCol.Groups["msg"].Value))
                {
                    Add(errors, BuildFromGo(mGoNoCol, line));
                    continue;
                }

                // Jest FAIL.
                var mJest = JestFailRe.Match(line);
                if (mJest.Success)
                {
                    var e = new BuildError();
                    e.Tool = "jest";
                    e.Severity = ErrorSeverity.Error;
                    e.File = mJest.Groups["file"].Value.Trim();
                    e.Message = "test file failed";
                    e.RawLine = line;
                    Add(errors, e);
                    continue;
                }

                // Generic error:/Error:/warning:.
                var mGen = GenericErrorRe.Match(line);
                if (mGen.Success)
                {
                    var e = new BuildError();
                    e.Tool = string.IsNullOrEmpty(toolHint) ? "generic" : toolHint;
                    string sev = mGen.Groups["sev"].Value.ToLowerInvariant();
                    e.Severity = sev.Contains("warn") ? ErrorSeverity.Warning : ErrorSeverity.Error;
                    e.Message = mGen.Groups["msg"].Value.Trim();
                    e.RawLine = line;
                    Add(errors, e);
                    continue;
                }

                // Context line: contains "error" but did not match a structured
                // pattern. Keep as Info so the fix strategy has surrounding text.
                if (LineLooksRelevant(line) && errors.Count < MaxErrors)
                {
                    var e = new BuildError();
                    e.Tool = "context";
                    e.Severity = ErrorSeverity.Info;
                    e.Message = line.Trim();
                    e.RawLine = line;
                    Add(errors, e);
                }
            }

            return errors;
        }

        void Add(List<BuildError> errors, BuildError e)
        {
            if (errors.Count >= MaxErrors) return;
            errors.Add(e);
        }

        static bool LooksLikeGoMessage(string msg)
        {
            // Go compiler messages start with "undefined:", "cannot", "syntax error", "expected", etc.
            // Avoid false-positives on arbitrary "file:number: text" lines.
            string lower = (msg ?? "").ToLowerInvariant();
            return lower.Contains("error") || lower.Contains("undefined") || lower.Contains("cannot")
                || lower.Contains("expected") || lower.Contains("syntax") || lower.Contains("imported")
                || lower.Contains("declared") || lower.Contains("not used");
        }

        static BuildError LastPythonLocation(List<BuildError> errors)
        {
            for (int i = errors.Count - 1; i >= 0; i--)
            {
                var e = errors[i];
                if (e.Tool == "python" && e.HasLocation) return e;
            }
            return null;
        }

        static BuildError BuildFromMsbuild(Match m, string rawLine)
        {
            var e = new BuildError();
            e.Tool = "msbuild";
            e.File = m.Groups["file"].Value.Trim();
            int.TryParse(m.Groups["line"].Value, out e.Line);
            int.TryParse(m.Groups["col"].Value, out e.Column);
            string sev = m.Groups["sev"].Value.ToLowerInvariant();
            e.Severity = sev == "warning" ? ErrorSeverity.Warning : ErrorSeverity.Error;
            e.Code = m.Groups["code"].Value.Trim();
            e.Message = m.Groups["msg"].Value.Trim();
            e.RawLine = rawLine;
            return e;
        }

        static BuildError BuildFromGo(Match m, string rawLine)
        {
            var e = new BuildError();
            e.Tool = "go";
            e.File = m.Groups["file"].Value.Trim();
            int.TryParse(m.Groups["line"].Value, out e.Line);
            int.TryParse(m.Groups["col"].Value, out e.Column);
            e.Severity = ErrorSeverity.Error;
            e.Message = m.Groups["msg"].Value.Trim();
            e.RawLine = rawLine;
            return e;
        }

        static bool LineLooksRelevant(string line)
        {
            string lower = line.ToLowerInvariant();
            return lower.Contains("error") || lower.Contains("failed") || lower.Contains("exception")
                || lower.Contains("traceback") || lower.Contains("cannot find");
        }

        // Summarize the parsed errors into a compact markdown block for the
        // session report. Lists up to 20 errors grouped by severity.
        public string ToMarkdown(List<BuildError> errors)
        {
            if (errors == null || errors.Count == 0) return "No errors parsed from build/test output.";
            var sb = new StringBuilder();
            int errs = 0, warns = 0;
            foreach (var e in errors)
            {
                if (e.Severity == ErrorSeverity.Error) errs++;
                else if (e.Severity == ErrorSeverity.Warning) warns++;
            }
            sb.AppendLine("## Parsed Errors (" + errs + " errors, " + warns + " warnings)");
            sb.AppendLine();
            int shown = 0;
            foreach (var e in errors)
            {
                if (shown >= 20) { sb.AppendLine("... and " + (errors.Count - shown) + " more"); break; }
                if (e.Severity == ErrorSeverity.Info) { shown++; continue; }
                sb.AppendLine("- " + e.ToString());
                shown++;
            }
            return sb.ToString().Trim();
        }
    }
}
