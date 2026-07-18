using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZhuaQianDesktopApp.Agent.Coding
{
    // Detected programming language of a project root.
    public enum ProjectLanguage
    {
        Unknown,
        CSharp,
        JavaScript,
        TypeScript,
        Python,
        Go,
        Rust,
        Java,
        Kotlin,
        Swift,
        Ruby,
        Php,
        Cpp,
        Mixed
    }

    // A detected build/test/lint tool with the command that invokes it.
    public sealed class DetectedTool
    {
        public string Name = "";        // "dotnet", "npm", "cargo", "make", "powershell" ...
        public string Command = "";     // full command string to run
        public string Source = "";      // why detected: "build.ps1 found", "package.json scripts.build"
        public int Confidence;          // 0-100 heuristic confidence
    }

    // Immutable snapshot of a project's structure: language, framework, build /
    // test / lint commands, entry / package files, and source directories. This
    // is the "read the project" step that closes the Codex/Claude Code gap
    // where ZhuaQian previously only looked at git status and line counts.
    public sealed class ProjectProfile
    {
        public string RootDirectory = "";
        public ProjectLanguage Language = ProjectLanguage.Unknown;
        public string Framework = "";          // ".NET Framework 4.8", "Node 22", "Python 3.12" ...
        public DetectedTool BuildTool;
        public DetectedTool TestTool;
        public DetectedTool LintTool;
        public string EntryFile = "";          // best-guess main entry
        public string PackageFile = "";        // package manifest
        public readonly List<string> SourceDirectories = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public DateTime AnalyzedAt = DateTime.Now;

        public string BuildCommand { get { return BuildTool != null ? BuildTool.Command : ""; } }
        public string TestCommand { get { return TestTool != null ? TestTool.Command : ""; } }
        public string LintCommand { get { return LintTool != null ? LintTool.Command : ""; } }

        public bool HasBuild { get { return BuildTool != null && !string.IsNullOrWhiteSpace(BuildTool.Command); } }
        public bool HasTest { get { return TestTool != null && !string.IsNullOrWhiteSpace(TestTool.Command); } }
    }

    // Scans a project root directory to produce a ProjectProfile. Pure reads:
    // never writes files. It walks the top two directory levels (skipping
    // node_modules / bin / obj / .git / vendor) to detect language markers,
    // build scripts, test scripts, and entry files. This is the Analyze step
    // of the Analyze -> Plan -> ApplyPatch -> RunTests -> Fix -> Review -> Done
    // coding loop.
    public sealed class ProjectAnalyzer
    {
        static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", "bin", "obj", ".git", ".svn", ".hg", "vendor",
            "dist", "build", "out", "target", "__pycache__", ".venv", "venv",
            ".idea", ".vscode", ".vs", "packages"
        };

        public int MaxDepth = 2;

        public ProjectProfile Analyze(string rootDirectory)
        {
            var profile = new ProjectProfile();
            profile.RootDirectory = rootDirectory ?? "";
            if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
            {
                profile.Notes.Add("root directory does not exist");
                return profile;
            }

            var langCounts = new Dictionary<ProjectLanguage, int>();
            var files = new List<string>();
            Walk(rootDirectory, 0, files, langCounts);

            profile.Language = PickLanguage(langCounts);
            profile.Framework = DetectFramework(rootDirectory, profile.Language);
            profile.BuildTool = DetectBuildTool(rootDirectory, profile.Language);
            profile.TestTool = DetectTestTool(rootDirectory, profile.Language, profile.BuildTool);
            profile.LintTool = DetectLintTool(rootDirectory, profile.Language);
            profile.EntryFile = DetectEntryFile(rootDirectory, profile.Language, files);
            profile.PackageFile = DetectPackageFile(rootDirectory, profile.Language);
            CollectSourceDirectories(profile, rootDirectory, files);

            if (profile.BuildTool == null)
                profile.Notes.Add("no build tool detected");
            if (profile.TestTool == null)
                profile.Notes.Add("no test tool detected");
            if (string.IsNullOrEmpty(profile.EntryFile))
                profile.Notes.Add("no entry file detected");

            return profile;
        }

        void Walk(string dir, int depth, List<string> files, Dictionary<ProjectLanguage, int> langCounts)
        {
            if (depth > MaxDepth) return;
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("ProjectAnalyzer walk: " + ex.Message); return; }

            foreach (var entry in entries)
            {
                string name = Path.GetFileName(entry);
                if (SkipDirs.Contains(name)) continue;

                if (Directory.Exists(entry))
                {
                    Walk(entry, depth + 1, files, langCounts);
                }
                else
                {
                    files.Add(entry);
                    var lang = ClassifyExtension(Path.GetExtension(entry));
                    if (lang != ProjectLanguage.Unknown)
                    {
                        int n;
                        langCounts.TryGetValue(lang, out n);
                        langCounts[lang] = n + 1;
                    }
                }
            }
        }

        static ProjectLanguage ClassifyExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return ProjectLanguage.Unknown;
            switch (ext.ToLowerInvariant())
            {
                case ".cs": return ProjectLanguage.CSharp;
                case ".js": case ".mjs": case ".cjs": case ".jsx": return ProjectLanguage.JavaScript;
                case ".ts": case ".tsx": return ProjectLanguage.TypeScript;
                case ".py": return ProjectLanguage.Python;
                case ".go": return ProjectLanguage.Go;
                case ".rs": return ProjectLanguage.Rust;
                case ".java": return ProjectLanguage.Java;
                case ".kt": case ".kts": return ProjectLanguage.Kotlin;
                case ".swift": return ProjectLanguage.Swift;
                case ".rb": return ProjectLanguage.Ruby;
                case ".php": return ProjectLanguage.Php;
                case ".cpp": case ".cc": case ".cxx": case ".c": case ".h": case ".hpp": return ProjectLanguage.Cpp;
                default: return ProjectLanguage.Unknown;
            }
        }

        static ProjectLanguage PickLanguage(Dictionary<ProjectLanguage, int> counts)
        {
            if (counts.Count == 0) return ProjectLanguage.Unknown;
            ProjectLanguage best = ProjectLanguage.Unknown;
            int bestCount = 0;
            int topTwo = 0;
            foreach (var kv in counts)
            {
                if (kv.Value > bestCount) { bestCount = kv.Value; best = kv.Key; }
                topTwo = Math.Max(topTwo, kv.Value);
            }
            // If two languages are within 30% of each other and both significant, mark Mixed.
            int second = 0;
            foreach (var kv in counts)
                if (kv.Key != best && kv.Value > second) second = kv.Value;
            if (best != ProjectLanguage.Unknown && second > 0 && topTwo > 5 && second >= topTwo * 0.7)
                return ProjectLanguage.Mixed;
            return best;
        }

        static string DetectFramework(string root, ProjectLanguage lang)
        {
            // .NET: read .csproj or .sln for TargetFramework
            string csproj = FindFirst(root, "*.csproj", 1);
            if (!string.IsNullOrEmpty(csproj))
            {
                string tf = GrepFirst(csproj, "<TargetFrameworkVersion>", "</TargetFrameworkVersion>");
                if (!string.IsNullOrEmpty(tf)) return ".NET " + tf.Trim();
                tf = GrepFirst(csproj, "<TargetFramework>", "</TargetFramework>");
                if (!string.IsNullOrEmpty(tf)) return ".NET " + tf.Trim();
                return ".NET (project file present)";
            }
            if (File.Exists(Path.Combine(root, "package.json")))
            {
                var deps = ReadPackageJsonDeps(Path.Combine(root, "package.json"));
                if (deps.Contains("react")) return "Node + React";
                if (deps.Contains("vue")) return "Node + Vue";
                if (deps.Contains("next")) return "Node + Next.js";
                return "Node.js";
            }
            if (File.Exists(Path.Combine(root, "Cargo.toml"))) return "Rust (Cargo)";
            if (File.Exists(Path.Combine(root, "go.mod"))) return "Go module";
            if (File.Exists(Path.Combine(root, "pom.xml"))) return "Java (Maven)";
            if (File.Exists(Path.Combine(root, "build.gradle")) || File.Exists(Path.Combine(root, "build.gradle.kts")))
                return "Java/Android (Gradle)";
            return lang == ProjectLanguage.Unknown ? "" : lang.ToString();
        }

        static DetectedTool DetectBuildTool(string root, ProjectLanguage lang)
        {
            // Highest priority: project-local build script (matches ZhuaQian's own pattern).
            if (File.Exists(Path.Combine(root, "build.ps1")))
                return Tool("powershell", @"powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1", "build.ps1 found", 95);
            if (File.Exists(Path.Combine(root, "Makefile")) || File.Exists(Path.Combine(root, "makefile")))
                return Tool("make", "make", "Makefile found", 80);

            switch (lang)
            {
                case ProjectLanguage.CSharp:
                    if (File.Exists(Path.Combine(root, "build.ps1")))
                        return Tool("powershell", @"powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1", "build.ps1", 95);
                    if (FindFirst(root, "*.csproj", 1) != null)
                        return Tool("dotnet", "dotnet build", ".csproj found", 75);
                    if (FindFirst(root, "*.sln", 0) != null)
                        return Tool("dotnet", "dotnet build", ".sln found", 70);
                    break;
                case ProjectLanguage.JavaScript:
                case ProjectLanguage.TypeScript:
                case ProjectLanguage.Mixed:
                    if (File.Exists(Path.Combine(root, "package.json")))
                    {
                        var scripts = ReadPackageJsonScripts(Path.Combine(root, "package.json"));
                        if (scripts.Contains("build")) return Tool("npm", "npm run build", "package.json scripts.build", 85);
                        if (File.Exists(Path.Combine(root, "pnpm-lock.yaml"))) return Tool("pnpm", "pnpm build", "pnpm-lock", 70);
                        return Tool("npm", "npm install", "package.json (no build script)", 50);
                    }
                    break;
                case ProjectLanguage.Python:
                    if (File.Exists(Path.Combine(root, "setup.py")) || File.Exists(Path.Combine(root, "pyproject.toml")))
                        return Tool("pip", "pip install -e .", "setup.py/pyproject.toml", 60);
                    break;
                case ProjectLanguage.Go:
                    if (File.Exists(Path.Combine(root, "go.mod"))) return Tool("go", "go build ./...", "go.mod", 80);
                    break;
                case ProjectLanguage.Rust:
                    if (File.Exists(Path.Combine(root, "Cargo.toml"))) return Tool("cargo", "cargo build", "Cargo.toml", 90);
                    break;
                case ProjectLanguage.Java:
                case ProjectLanguage.Kotlin:
                    if (File.Exists(Path.Combine(root, "pom.xml"))) return Tool("maven", "mvn compile", "pom.xml", 80);
                    if (File.Exists(Path.Combine(root, "build.gradle")) || File.Exists(Path.Combine(root, "build.gradle.kts")))
                        return Tool("gradle", "gradle build", "build.gradle", 80);
                    break;
            }
            return null;
        }

        static DetectedTool DetectTestTool(string root, ProjectLanguage lang, DetectedTool buildTool)
        {
            // Highest priority: project-local test script.
            if (File.Exists(Path.Combine(root, "src", "scripts", "run-tests.ps1")))
                return Tool("powershell", @"powershell -NoProfile -ExecutionPolicy Bypass -File .\src\scripts\run-tests.ps1", "src/scripts/run-tests.ps1 found", 95);
            if (File.Exists(Path.Combine(root, "run-tests.ps1")))
                return Tool("powershell", @"powershell -NoProfile -ExecutionPolicy Bypass -File .\run-tests.ps1", "run-tests.ps1 found", 90);

            switch (lang)
            {
                case ProjectLanguage.CSharp:
                    if (FindFirst(root, "*.csproj", 1) != null) return Tool("dotnet", "dotnet test", ".csproj", 70);
                    break;
                case ProjectLanguage.JavaScript:
                case ProjectLanguage.TypeScript:
                case ProjectLanguage.Mixed:
                    if (File.Exists(Path.Combine(root, "package.json")))
                    {
                        var scripts = ReadPackageJsonScripts(Path.Combine(root, "package.json"));
                        if (scripts.Contains("test")) return Tool("npm", "npm test", "package.json scripts.test", 85);
                    }
                    break;
                case ProjectLanguage.Python:
                    if (Directory.Exists(Path.Combine(root, "tests"))) return Tool("pytest", "pytest", "tests/ dir", 65);
                    break;
                case ProjectLanguage.Go:
                    if (File.Exists(Path.Combine(root, "go.mod"))) return Tool("go", "go test ./...", "go.mod", 80);
                    break;
                case ProjectLanguage.Rust:
                    if (File.Exists(Path.Combine(root, "Cargo.toml"))) return Tool("cargo", "cargo test", "Cargo.toml", 90);
                    break;
                case ProjectLanguage.Java:
                case ProjectLanguage.Kotlin:
                    if (File.Exists(Path.Combine(root, "pom.xml"))) return Tool("maven", "mvn test", "pom.xml", 80);
                    if (File.Exists(Path.Combine(root, "build.gradle"))) return Tool("gradle", "gradle test", "build.gradle", 80);
                    break;
            }
            return null;
        }

        static DetectedTool DetectLintTool(string root, ProjectLanguage lang)
        {
            switch (lang)
            {
                case ProjectLanguage.JavaScript:
                case ProjectLanguage.TypeScript:
                case ProjectLanguage.Mixed:
                    if (File.Exists(Path.Combine(root, ".eslintrc.js")) || File.Exists(Path.Combine(root, ".eslintrc.json")) || File.Exists(Path.Combine(root, "eslint.config.js")))
                        return Tool("eslint", "npx eslint .", "eslint config", 70);
                    break;
                case ProjectLanguage.Python:
                    if (File.Exists(Path.Combine(root, ".flake8")) || File.Exists(Path.Combine(root, "setup.cfg")))
                        return Tool("flake8", "flake8", "flake8 config", 60);
                    break;
            }
            return null;
        }

        static string DetectEntryFile(string root, ProjectLanguage lang, List<string> files)
        {
            // Prefer files that contain a Main / __main__ / main() entry.
            string[] candidates = EntryCandidates(lang);
            foreach (var c in candidates)
            {
                string full = Path.Combine(root, c);
                if (File.Exists(full)) return c;
            }
            // Fallback: search scanned files for an entry pattern.
            foreach (var f in files)
            {
                string rel = MakeRelative(root, f);
                string name = Path.GetFileName(f);
                foreach (var c in candidates)
                {
                    if (string.Equals(name, Path.GetFileName(c), StringComparison.OrdinalIgnoreCase)) return rel;
                }
            }
            return "";
        }

        static string[] EntryCandidates(ProjectLanguage lang)
        {
            switch (lang)
            {
                case ProjectLanguage.CSharp: return new[] { "Program.cs", "MainForm.cs", "App.cs" };
                case ProjectLanguage.JavaScript: return new[] { "index.js", "main.js", "app.js", "server.js" };
                case ProjectLanguage.TypeScript: return new[] { "index.ts", "main.ts", "app.ts", "server.ts" };
                case ProjectLanguage.Python: return new[] { "main.py", "app.py", "__main__.py", "run.py" };
                case ProjectLanguage.Go: return new[] { "main.go" };
                case ProjectLanguage.Rust: return new[] { "src/main.rs" };
                case ProjectLanguage.Java: case ProjectLanguage.Kotlin: return new[] { "src/main/java/Main.java", "src/main/kotlin/Main.kt" };
                default: return new string[0];
            }
        }

        static string DetectPackageFile(string root, ProjectLanguage lang)
        {
            string[] manifests = PackageManifests(lang);
            foreach (var m in manifests)
            {
                string full = Path.Combine(root, m);
                if (File.Exists(full)) return m;
            }
            return "";
        }

        static string[] PackageManifests(ProjectLanguage lang)
        {
            switch (lang)
            {
                case ProjectLanguage.CSharp: return new[] { "ZhuaQianDesktop.csproj", "package.json" };
                case ProjectLanguage.JavaScript: case ProjectLanguage.TypeScript: case ProjectLanguage.Mixed: return new[] { "package.json" };
                case ProjectLanguage.Python: return new[] { "pyproject.toml", "setup.py", "requirements.txt" };
                case ProjectLanguage.Go: return new[] { "go.mod" };
                case ProjectLanguage.Rust: return new[] { "Cargo.toml" };
                case ProjectLanguage.Java: case ProjectLanguage.Kotlin: return new[] { "pom.xml", "build.gradle" };
                default: return new string[0];
            }
        }

        static void CollectSourceDirectories(ProjectProfile profile, string root, List<string> files)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                string rel = MakeRelative(root, f);
                string dir = Path.GetDirectoryName(rel);
                if (string.IsNullOrEmpty(dir)) continue;
                if (dir.StartsWith("node_modules", StringComparison.OrdinalIgnoreCase)) continue;
                if (seen.Add(dir)) profile.SourceDirectories.Add(dir.Replace('\\', '/'));
            }
        }

        static DetectedTool Tool(string name, string command, string source, int confidence)
        {
            return new DetectedTool { Name = name, Command = command, Source = source, Confidence = confidence };
        }

        static string FindFirst(string root, string pattern, int depth)
        {
            try
            {
                string[] found = Directory.GetFiles(root, pattern, depth == 0 ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories);
                if (found.Length == 0) return null;
                // prefer the shallowest match
                string best = found[0];
                int bestDepth = CountSep(MakeRelative(root, best));
                for (int i = 1; i < found.Length; i++)
                {
                    int d = CountSep(MakeRelative(root, found[i]));
                    if (d < bestDepth) { best = found[i]; bestDepth = d; }
                }
                return best;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("ProjectAnalyzer FindFirst: " + ex.Message); return null; }
        }

        static int CountSep(string path)
        {
            int n = 0;
            foreach (char c in path) if (c == '/' || c == '\\') n++;
            return n;
        }

        static string GrepFirst(string file, string startTag, string endTag)
        {
            try
            {
                using (var r = new StreamReader(file))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        int s = line.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
                        if (s < 0) continue;
                        int e = line.IndexOf(endTag, s + startTag.Length, StringComparison.OrdinalIgnoreCase);
                        if (e < 0) continue;
                        return line.Substring(s + startTag.Length, e - s - startTag.Length);
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("ProjectAnalyzer GrepFirst: " + ex.Message); }
            return "";
        }

        static string ReadPackageJsonDeps(string packageJson)
        {
            return ReadPackageJsonSection(packageJson, "dependencies") + " " + ReadPackageJsonSection(packageJson, "devDependencies");
        }

        static string ReadPackageJsonScripts(string packageJson)
        {
            return ReadPackageJsonSection(packageJson, "scripts");
        }

        // Minimal JSON section reader (no dependency on a JSON parser that may not
        // be available in the test EXE). Returns the raw text between the section
        // key and the matching closing brace.
        static string ReadPackageJsonSection(string packageJson, string section)
        {
            try
            {
                string text = File.ReadAllText(packageJson);
                string key = "\"" + section + "\"";
                int idx = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return "";
                int brace = text.IndexOf('{', idx);
                if (brace < 0) return "";
                int depth = 0;
                var sb = new StringBuilder();
                for (int i = brace; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c == '{') depth++;
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0) break;
                    }
                    sb.Append(c);
                }
                return sb.ToString();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("ProjectAnalyzer ReadSection: " + ex.Message); return ""; }
        }

        static string MakeRelative(string root, string full)
        {
            string r = root.TrimEnd('\\', '/') + "\\";
            if (full.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return full.Substring(r.Length);
            return full;
        }

        public string ToMarkdown(ProjectProfile profile)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Project Profile");
            sb.AppendLine();
            sb.AppendLine("Root: " + (string.IsNullOrWhiteSpace(profile.RootDirectory) ? "(unknown)" : profile.RootDirectory));
            sb.AppendLine("Language: " + profile.Language);
            if (!string.IsNullOrEmpty(profile.Framework)) sb.AppendLine("Framework: " + profile.Framework);
            sb.AppendLine("Build: " + (profile.HasBuild ? "`" + profile.BuildCommand + "` (" + profile.BuildTool.Source + ")" : "(none)"));
            sb.AppendLine("Test: " + (profile.HasTest ? "`" + profile.TestCommand + "` (" + profile.TestTool.Source + ")" : "(none)"));
            if (profile.LintTool != null) sb.AppendLine("Lint: `" + profile.LintCommand + "`");
            if (!string.IsNullOrEmpty(profile.EntryFile)) sb.AppendLine("Entry: " + profile.EntryFile);
            if (!string.IsNullOrEmpty(profile.PackageFile)) sb.AppendLine("Package: " + profile.PackageFile);
            if (profile.SourceDirectories.Count > 0)
            {
                sb.AppendLine("Source dirs:");
                int shown = 0;
                foreach (var d in profile.SourceDirectories)
                {
                    if (shown >= 15) { sb.AppendLine("  ... and " + (profile.SourceDirectories.Count - shown) + " more"); break; }
                    sb.AppendLine("  - " + d);
                    shown++;
                }
            }
            if (profile.Notes.Count > 0)
            {
                sb.AppendLine("Notes:");
                foreach (var n in profile.Notes) sb.AppendLine("  - " + n);
            }
            return sb.ToString().Trim();
        }
    }
}
