using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZhuaQianDesktopApp.Agent
{
    // Generic project scanner (closes the "project scanner" gap vs Codex/Claude
    // Code). Walks a directory tree (bounded) and infers language, framework,
    // build/test commands and entry files so the coding loop can run anywhere,
    // not just inside this repository. Pure + read-only; never writes.
    public sealed class ProjectScan
    {
        public string RootDirectory = "";
        public string PrimaryKind = ""; // dotnet | node | python | rust | go | java | ruby | cpp | script | unknown
        public readonly List<string> Languages = new List<string>();
        public readonly List<string> Frameworks = new List<string>();
        public string BuildCommand = "";
        public string TestCommand = "";
        public readonly List<string> EntryFiles = new List<string>();
        public readonly List<string> SampledFiles = new List<string>();
        public int FileCount;
        public bool IsGitRepo;

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Project Scan");
            sb.AppendLine();
            sb.AppendLine("Root: " + (string.IsNullOrWhiteSpace(RootDirectory) ? "(unknown)" : RootDirectory));
            sb.AppendLine("Primary kind: " + (string.IsNullOrWhiteSpace(PrimaryKind) ? "unknown" : PrimaryKind));
            sb.AppendLine("Git repo: " + IsGitRepo.ToString());
            sb.AppendLine("File count: " + FileCount.ToString());
            sb.AppendLine("Languages: " + string.Join(", ", Languages));
            if (Frameworks.Count > 0) sb.AppendLine("Frameworks: " + string.Join(", ", Frameworks));
            sb.AppendLine("Build command: `" + (string.IsNullOrWhiteSpace(BuildCommand) ? "(none detected)" : BuildCommand) + "`");
            sb.AppendLine("Test command: `" + (string.IsNullOrWhiteSpace(TestCommand) ? "(none detected)" : TestCommand) + "`");
            if (EntryFiles.Count > 0)
            {
                sb.AppendLine("Entry files:");
                foreach (var e in EntryFiles) sb.AppendLine("  - " + e);
            }
            return sb.ToString().Trim();
        }
    }

    public sealed class ProjectScanner
    {
        static readonly string[] SkipDirs = {
            ".git", "node_modules", "bin", "obj", "packages", ".vs",
            "dist", "out", "target", "vendor", "__pycache__", ".idea", ".git"
        };

        public static ProjectScan Scan(string root, int maxFiles = 4000, int maxDepth = 9)
        {
            var scan = new ProjectScan();
            scan.RootDirectory = root ?? "";
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return scan;

            scan.IsGitRepo = Directory.Exists(Path.Combine(root, ".git"));

            var extCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            bool hasCsproj = false, hasSln = false;
            bool hasPackageJson = false, hasTsConfig = false;
            bool hasRequirements = false, hasSetupPy = false, hasPyProject = false;
            bool hasCargo = false, hasGoMod = false;
            bool hasGemfile = false, hasPom = false, hasGradle = false;
            bool hasMakefile = false, hasCMake = false;

            int seen = 0;
            Walk(root, root, 0, maxDepth, ref seen, maxFiles, extCount,
                ref hasCsproj, ref hasSln, ref hasPackageJson, ref hasTsConfig,
                ref hasRequirements, ref hasSetupPy, ref hasPyProject, ref hasCargo,
                ref hasGoMod, ref hasGemfile, ref hasPom, ref hasGradle, ref hasMakefile, ref hasCMake,
                scan);

            scan.FileCount = seen;

            if (hasCsproj || hasSln)
            {
                scan.PrimaryKind = "dotnet";
                scan.Languages.Add("C#");
                scan.Frameworks.Add("MSBuild");
                if (hasSln) scan.Frameworks.Add("Solution");
                scan.BuildCommand = "dotnet build";
                scan.TestCommand = "dotnet test";
            }
            else if (hasPackageJson)
            {
                scan.PrimaryKind = "node";
                scan.Languages.Add("JavaScript");
                if (hasTsConfig) scan.Languages.Add("TypeScript");
                scan.BuildCommand = "npm run build";
                scan.TestCommand = "npm test";
            }
            else if (hasPyProject || hasSetupPy || hasRequirements)
            {
                scan.PrimaryKind = "python";
                scan.Languages.Add("Python");
                scan.BuildCommand = "";
                scan.TestCommand = "pytest";
            }
            else if (hasCargo)
            {
                scan.PrimaryKind = "rust";
                scan.Languages.Add("Rust");
                scan.BuildCommand = "cargo build";
                scan.TestCommand = "cargo test";
            }
            else if (hasGoMod)
            {
                scan.PrimaryKind = "go";
                scan.Languages.Add("Go");
                scan.BuildCommand = "go build ./...";
                scan.TestCommand = "go test ./...";
            }
            else if (hasPom || hasGradle)
            {
                scan.PrimaryKind = "java";
                scan.Languages.Add("Java");
                scan.BuildCommand = hasGradle ? "gradle build" : "mvn package";
                scan.TestCommand = hasGradle ? "gradle test" : "mvn test";
            }
            else if (hasGemfile)
            {
                scan.PrimaryKind = "ruby";
                scan.Languages.Add("Ruby");
                scan.BuildCommand = "";
                scan.TestCommand = "bundle exec rspec";
            }
            else if (hasMakefile || hasCMake)
            {
                scan.PrimaryKind = "cpp";
                scan.Languages.Add("C/C++");
                scan.BuildCommand = hasCMake ? "cmake --build ." : "make";
                scan.TestCommand = "";
            }

            // Fallback to this repository's known scripts when nothing else fits
            // but a build.ps1 exists at the root (keeps ZhuaQian self-hosting).
            if (string.IsNullOrWhiteSpace(scan.BuildCommand) && File.Exists(Path.Combine(root, "build.ps1")))
            {
                scan.BuildCommand = @"powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1";
                if (string.IsNullOrWhiteSpace(scan.TestCommand) && File.Exists(Path.Combine(root, @"src\scripts\run-tests.ps1")))
                    scan.TestCommand = @"powershell -NoProfile -ExecutionPolicy Bypass -File .\src\scripts\run-tests.ps1";
                if (string.IsNullOrWhiteSpace(scan.PrimaryKind)) scan.PrimaryKind = "script";
            }

            TopLanguages(extCount, scan);
            DetectEntryFiles(root, scan);
            return scan;
        }

        static void Walk(string root, string dir, int depth, int maxDepth, ref int seen, int maxFiles,
            Dictionary<string, int> extCount,
            ref bool hasCsproj, ref bool hasSln, ref bool hasPackageJson, ref bool hasTsConfig,
            ref bool hasRequirements, ref bool hasSetupPy, ref bool hasPyProject, ref bool hasCargo,
            ref bool hasGoMod, ref bool hasGemfile, ref bool hasPom, ref bool hasGradle, ref bool hasMakefile, ref bool hasCMake,
            ProjectScan scan)
        {
            if (depth > maxDepth || seen >= maxFiles) return;
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch (Exception) { return; }

            foreach (var entry in entries)
            {
                if (seen >= maxFiles) break;
                string name = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(name)) continue;
                bool isDir = Directory.Exists(entry);
                if (isDir)
                {
                    if (Array.IndexOf(SkipDirs, name) >= 0) continue;
                    Walk(root, entry, depth + 1, maxDepth, ref seen, maxFiles, extCount,
                        ref hasCsproj, ref hasSln, ref hasPackageJson, ref hasTsConfig,
                        ref hasRequirements, ref hasSetupPy, ref hasPyProject, ref hasCargo,
                        ref hasGoMod, ref hasGemfile, ref hasPom, ref hasGradle, ref hasMakefile, ref hasCMake,
                        scan);
                    continue;
                }

                seen++;
                string lower = name.ToLowerInvariant();
                if (lower.EndsWith(".csproj")) hasCsproj = true;
                else if (lower.EndsWith(".sln")) hasSln = true;
                else if (lower == "package.json") hasPackageJson = true;
                else if (lower == "tsconfig.json") hasTsConfig = true;
                else if (lower == "requirements.txt") hasRequirements = true;
                else if (lower == "setup.py") hasSetupPy = true;
                else if (lower == "pyproject.toml") hasPyProject = true;
                else if (lower == "cargo.toml") hasCargo = true;
                else if (lower == "go.mod") hasGoMod = true;
                else if (lower == "gemfile") hasGemfile = true;
                else if (lower == "pom.xml") hasPom = true;
                else if (lower == "build.gradle") hasGradle = true;
                else if (lower == "makefile") hasMakefile = true;
                else if (lower == "cmakelists.txt") hasCMake = true;

                string ext = Path.GetExtension(name);
                if (!string.IsNullOrEmpty(ext))
                {
                    if (!extCount.ContainsKey(ext)) extCount[ext] = 0;
                    extCount[ext]++;
                }

                if (scan.SampledFiles.Count < 40)
                    scan.SampledFiles.Add(MakeRelative(root, entry));
            }
        }

        static void TopLanguages(Dictionary<string, int> extCount, ProjectScan scan)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {".cs","C#"}, {".fs","F#"}, {".vb","VB.NET"},
                {".js","JavaScript"}, {".ts","TypeScript"}, {".jsx","JavaScript"}, {".tsx","TypeScript"},
                {".py","Python"}, {".rb","Ruby"}, {".go","Go"}, {".rs","Rust"},
                {".java","Java"}, {".kt","Kotlin"}, {".cpp","C++"}, {".cc","C++"}, {".c","C"}, {".h","C/C++"},
                {".php","PHP"}, {".swift","Swift"}, {".scala","Scala"}, {".sql","SQL"},
                {".html","HTML"}, {".css","CSS"}, {".sh","Shell"}, {".ps1","PowerShell"}
            };
            var langs = new List<string>();
            foreach (var kv in extCount)
            {
                if (map.TryGetValue(kv.Key, out string lang) && !langs.Contains(lang))
                    langs.Add(lang);
            }
            foreach (var l in langs) if (!scan.Languages.Contains(l)) scan.Languages.Add(l);
        }

        static void DetectEntryFiles(string root, ProjectScan scan)
        {
            string[] candidates = {
                "Program.cs", "Main.cs", "main.ts", "main.js", "index.ts", "index.js",
                "app.ts", "app.js", "app.py", "main.py", "main.go", "main.rs",
                "Program.fs", "Startup.cs", "MainForm.cs"
            };
            foreach (var c in candidates)
            {
                string full = Path.Combine(root, c);
                if (File.Exists(full)) { scan.EntryFiles.Add(c); continue; }
                try
                {
                    var found = Directory.GetFiles(root, c, SearchOption.TopDirectoryOnly);
                    if (found.Length > 0) scan.EntryFiles.Add(MakeRelative(root, found[0]));
                }
                catch (Exception) { }
            }
        }

        static string MakeRelative(string root, string full)
        {
            try
            {
                string r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
                string f = Path.GetFullPath(full);
                if (f.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return f.Substring(r.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
                return full;
            }
            catch (Exception) { return full; }
        }
    }
}
