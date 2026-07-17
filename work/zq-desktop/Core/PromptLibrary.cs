using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZhuaQianDesktopApp.Core
{
    public class PromptAssemblyInfo
    {
        public string Id;
        public string Title;
        public string Domain;
        public string Description;

        public override string ToString()
        {
            return Title + " (" + Id + ")";
        }
    }

    public class PromptMemoryItem
    {
        public string Name;
        public string Path;

        public override string ToString()
        {
            return Name;
        }
    }

    public class PromptLibrary
    {
        readonly string configDir;
        readonly string memoryDir;

        public PromptLibrary(string configDir)
        {
            this.configDir = string.IsNullOrWhiteSpace(configDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZhuaQianDesktop")
                : configDir;
            memoryDir = Path.Combine(this.configDir, "prompt-memory");
        }

        public List<PromptAssemblyInfo> ListAssemblies()
        {
            return new List<PromptAssemblyInfo>
            {
                new PromptAssemblyInfo { Id = "programmer_bugfix", Title = "Programmer Bug Fix", Domain = "programmer", Description = "Diagnose a bug, propose a minimal fix, and list verification." },
                new PromptAssemblyInfo { Id = "programmer_review", Title = "Programmer Code Review", Domain = "programmer", Description = "Review code for bugs, security, tests, and maintainability." },
                new PromptAssemblyInfo { Id = "office_summary", Title = "Office Document Summary", Domain = "office", Description = "Summarize documents, extract risks, facts, and action items." },
                new PromptAssemblyInfo { Id = "office_meeting", Title = "Office Meeting Minutes", Domain = "office", Description = "Create meeting minutes, decisions, owners, and deadlines." },
                new PromptAssemblyInfo { Id = "media_script", Title = "Media Topic And Script", Domain = "media", Description = "Create topics, hooks, scripts, storyboard, title, and caption." },
                new PromptAssemblyInfo { Id = "media_calendar", Title = "Media Batch Calendar", Domain = "media", Description = "Plan a batch calendar with pillars, topics, assets, and status." }
            };
        }

        public List<PromptMemoryItem> ListMemories()
        {
            var result = new List<PromptMemoryItem>();
            if (!Directory.Exists(memoryDir)) return result;
            foreach (var path in Directory.GetFiles(memoryDir, "*.md"))
            {
                result.Add(new PromptMemoryItem
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    Path = path
                });
            }
            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public string ReadMemory(string name)
        {
            string path = MemoryPath(name);
            if (!File.Exists(path)) return "";
            return File.ReadAllText(path, Encoding.UTF8);
        }

        public string WriteMemory(string name, string content)
        {
            name = SafeName(name);
            Directory.CreateDirectory(memoryDir);
            string path = MemoryPath(name);
            File.WriteAllText(path, (content ?? "").Trim() + Environment.NewLine, Encoding.UTF8);
            return path;
        }

        public string Assemble(string assemblyId, string task, IEnumerable<string> memoryNames, IEnumerable<string> attachmentLabels)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ZhuaQian Desktop Prompt Assembly");
            sb.AppendLine();
            sb.AppendLine("You are ZhuaQian Desktop, a local-first Windows AI workbench.");
            sb.AppendLine("Use permission-aware, auditable, practical workflows. Do not claim external side effects unless the desktop app reports real results.");
            sb.AppendLine("Use public knowledge and clean-room reasoning only. Do not use leaked private prompts or proprietary hidden implementation details.");
            sb.AppendLine();
            AppendPermissionPolicy(sb);
            AppendDomain(sb, assemblyId);
            AppendWorkflow(sb, assemblyId);
            AppendMemory(sb, memoryNames);
            AppendAttachments(sb, attachmentLabels);
            sb.AppendLine("## User Task");
            sb.AppendLine(string.IsNullOrWhiteSpace(task) ? "(No task provided yet.)" : task.Trim());
            return sb.ToString();
        }

        void AppendPermissionPolicy(StringBuilder sb)
        {
            sb.AppendLine("## Permission Policy");
            sb.AppendLine("- Local draft work and attaching files to the current task are allowed.");
            sb.AppendLine("- Uploading, publishing, sending, deleting, moving files, changing permissions, Git push, and running plugins require explicit approval.");
            sb.AppendLine("- Reading secrets, browser cookies, wallets, private keys, and password stores is denied unless the user explicitly authorizes a narrow task.");
            sb.AppendLine("- Every risky action should state action, target, data involved, and consequence before execution.");
            sb.AppendLine();
        }

        void AppendDomain(StringBuilder sb, string assemblyId)
        {
            sb.AppendLine("## Domain");
            if (assemblyId.StartsWith("programmer_", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("Programmer: inspect the codebase first, keep changes small, run tests when available, and summarize file paths and verification.");
            }
            else if (assemblyId.StartsWith("office_", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("Office worker: preserve facts, extract decisions/action items, use clear tables when helpful, and avoid sending anything without confirmation.");
            }
            else if (assemblyId.StartsWith("media_", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("Media creator: create publishable drafts with hook, body, CTA, title options, captions, and compliance notes.");
            }
            else
            {
                sb.AppendLine("General: produce concise, actionable outputs with risks and next steps.");
            }
            sb.AppendLine();
        }

        void AppendWorkflow(StringBuilder sb, string assemblyId)
        {
            sb.AppendLine("## Workflow");
            if (assemblyId == "programmer_bugfix")
            {
                sb.AppendLine("1. Restate the bug.");
                sb.AppendLine("2. Inspect relevant files/logs/tests.");
                sb.AppendLine("3. Identify root cause.");
                sb.AppendLine("4. Propose or make the smallest safe fix.");
                sb.AppendLine("5. Run verification.");
                sb.AppendLine("Output: Bug, Root cause, Fix, Files changed, Verification, Residual risk.");
            }
            else if (assemblyId == "programmer_review")
            {
                sb.AppendLine("Review order: correctness, security/privacy, data loss, tests, performance, maintainability, documentation.");
                sb.AppendLine("Output findings first with severity, impact, and suggested fix.");
            }
            else if (assemblyId == "office_summary")
            {
                sb.AppendLine("Extract executive summary, key facts, numbers, dates, risks, action items, and missing information.");
            }
            else if (assemblyId == "office_meeting")
            {
                sb.AppendLine("Extract meeting title, attendees, summary, decisions, action items, owners, deadlines, open questions, and follow-up draft.");
            }
            else if (assemblyId == "media_script")
            {
                sb.AppendLine("Generate platform, audience, topic angle, hook, script, storyboard, title options, cover copy, caption, and compliance notes.");
            }
            else if (assemblyId == "media_calendar")
            {
                sb.AppendLine("Create content pillars, calendar rows, asset list, production checklist, reuse plan, and risks.");
            }
            else
            {
                sb.AppendLine("Plan, execute, verify, and summarize.");
            }
            sb.AppendLine();
        }

        void AppendMemory(StringBuilder sb, IEnumerable<string> memoryNames)
        {
            if (memoryNames == null) return;
            bool wroteHeader = false;
            foreach (var name in memoryNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                string content = ReadMemory(name);
                if (string.IsNullOrWhiteSpace(content)) continue;
                if (!wroteHeader)
                {
                    sb.AppendLine("## Local Memory");
                    wroteHeader = true;
                }
                sb.AppendLine("### " + name);
                sb.AppendLine(content.Trim());
                sb.AppendLine();
            }
        }

        void AppendAttachments(StringBuilder sb, IEnumerable<string> attachmentLabels)
        {
            if (attachmentLabels == null) return;
            bool wroteHeader = false;
            foreach (var label in attachmentLabels)
            {
                if (string.IsNullOrWhiteSpace(label)) continue;
                if (!wroteHeader)
                {
                    sb.AppendLine("## Current Local Attachments");
                    sb.AppendLine("These are attached locally to the desktop task. Do not upload or share them externally without confirmation.");
                    wroteHeader = true;
                }
                sb.AppendLine("- " + label);
            }
            if (wroteHeader) sb.AppendLine();
        }

        string MemoryPath(string name)
        {
            return Path.Combine(memoryDir, SafeName(name) + ".md");
        }

        string SafeName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) name = "memory";
            var sb = new StringBuilder();
            foreach (char ch in name)
            {
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') sb.Append(ch);
            }
            if (sb.Length == 0) sb.Append("memory");
            return sb.ToString();
        }
    }
}
