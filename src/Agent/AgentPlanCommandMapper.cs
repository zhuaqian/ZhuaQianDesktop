using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ZhuaQianDesktopApp.Agent
{
    public sealed class AgentPlanCommandMapperOptions
    {
        public string TaskId = "";
        public string TaskTitle = "";
        public string DefaultOutputDirectory = "";
        public string DefaultText = "";
    }

    public sealed class AgentPlanCommandMapping
    {
        public readonly List<IAgentCommand> Commands = new List<IAgentCommand>();
        public readonly List<string> Skipped = new List<string>();

        public bool HasCommands
        {
            get { return Commands.Count > 0; }
        }
    }

    public sealed class AgentPlanCommandMapper
    {
        public AgentPlanCommandMapping Map(AgentPlan plan, AgentPlanCommandMapperOptions options)
        {
            var mapping = new AgentPlanCommandMapping();
            if (plan == null) return mapping;
            if (options == null) options = new AgentPlanCommandMapperOptions();

            for (int i = 0; i < plan.Steps.Count; i++)
            {
                var step = plan.Steps[i];
                IAgentCommand command = BuildCommand(plan, step, options);
                if (command == null)
                    mapping.Skipped.Add(DescribeStep(step) + " (missing required target)");
                else
                    mapping.Commands.Add(command);
            }
            return mapping;
        }

        IAgentCommand BuildCommand(AgentPlan plan, AgentPlanStep step, AgentPlanCommandMapperOptions options)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.CommandType)) return null;
            if (step.CommandType == "ExportFile") return BuildExportCommand(plan, step, options);
            if (step.CommandType == "ComputerControl") return BuildComputerCommand(step, options);
            if (step.CommandType == "WebSearch") return BuildWebSearchCommand(step, options);
            if (step.CommandType == "BrowserFetch") return BuildBrowserFetchCommand(step, options);
            if (step.CommandType == "RunPlugin") return BuildPluginCommand(step, options);
            if (step.CommandType == "OrganizeFolder") return BuildOrganizeCommand(step, options);
            if (step.CommandType == "EndProcess") return BuildEndProcessCommand(step, options);
            return null;
        }

        IAgentCommand BuildExportCommand(AgentPlan plan, AgentPlanStep step, AgentPlanCommandMapperOptions options)
        {
            string format = InferExportFormat(step);
            string target = FirstNonEmpty(step.Target, BuildDefaultOutputPath(plan, step, options, format));
            if (string.IsNullOrWhiteSpace(target)) return null;

            var args = BaseArgs(options);
            args["format"] = format;
            args["text"] = FirstNonEmpty(options.DefaultText, BuildPlanText(plan));
            return new AgentCommand("ExportFile", "permFileWrite", options.TaskId, target, step.Title, args);
        }

        IAgentCommand BuildComputerCommand(AgentPlanStep step, AgentPlanCommandMapperOptions options)
        {
            string lower = (step.Title ?? "").ToLowerInvariant();
            var args = BaseArgs(options);
            if (lower.Contains("wait") || lower.Contains("sleep") || lower.Contains("pause"))
            {
                string ms = ExtractMilliseconds(step.Title);
                args["action"] = "wait";
                args["ms"] = ms;
                return new AgentCommand("ComputerControl", "permAutomationInput", options.TaskId, ms + "ms", step.Title, args);
            }

            string target = FirstNonEmpty(step.Target, ExtractOpenTarget(step.Title));
            if (string.IsNullOrWhiteSpace(target)) return null;
            args["action"] = "open";
            args["target"] = target;
            return new AgentCommand("ComputerControl", "permAutomationInput", options.TaskId, target, step.Title, args);
        }

        IAgentCommand BuildWebSearchCommand(AgentPlanStep step, AgentPlanCommandMapperOptions options)
        {
            string query = FirstNonEmpty(step.Target, StripLeadingVerb(step.Title, "web search", "look up", "search", "find"));
            if (string.IsNullOrWhiteSpace(query)) return null;
            var args = BaseArgs(options);
            args["query"] = query;
            return new AgentCommand("WebSearch", "permNetworkUpload", options.TaskId, query, step.Title, args);
        }

        IAgentCommand BuildBrowserFetchCommand(AgentPlanStep step, AgentPlanCommandMapperOptions options)
        {
            string url = FirstNonEmpty(step.Target, StripLeadingVerb(step.Title, "fetch", "open", "browse", "read", "读取", "打开", "抓取", "浏览"));
            if (string.IsNullOrWhiteSpace(url)) return null;
            var args = BaseArgs(options);
            args["url"] = url;
            return new AgentCommand("BrowserFetch", "permNetworkUpload", options.TaskId, url, step.Title, args);
        }

        IAgentCommand BuildPluginCommand(AgentPlanStep step, AgentPlanCommandMapperOptions options)
        {
            string target = FirstNonEmpty(step.Target, ExtractPath(step.Title));
            if (string.IsNullOrWhiteSpace(target)) return null;
            var args = BaseArgs(options);
            args["stdin"] = options.DefaultText ?? "";
            return new AgentCommand("RunPlugin", "permPluginRun", options.TaskId, target, step.Title, args);
        }

        IAgentCommand BuildOrganizeCommand(AgentPlanStep step, AgentPlanCommandMapperOptions options)
        {
            string target = FirstNonEmpty(step.Target, ExtractPath(step.Title));
            if (string.IsNullOrWhiteSpace(target)) return null;
            var args = BaseArgs(options);
            args["rootDir"] = target;
            return new AgentCommand("OrganizeFolder", "permFileMoveDelete", options.TaskId, target, step.Title, args);
        }

        IAgentCommand BuildEndProcessCommand(AgentPlanStep step, AgentPlanCommandMapperOptions options)
        {
            string pid = ExtractPid(FirstNonEmpty(step.Target, step.Title));
            if (string.IsNullOrWhiteSpace(pid)) return null;
            var args = BaseArgs(options);
            args["pid"] = pid;
            return new AgentCommand("EndProcess", "permProcessManage", options.TaskId, pid, step.Title, args);
        }

        Dictionary<string, object> BaseArgs(AgentPlanCommandMapperOptions options)
        {
            var args = new Dictionary<string, object>();
            args["taskTitle"] = options.TaskTitle ?? "";
            return args;
        }

        string BuildDefaultOutputPath(AgentPlan plan, AgentPlanStep step, AgentPlanCommandMapperOptions options, string format)
        {
            string dir = options.DefaultOutputDirectory;
            if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(Path.GetTempPath(), "ZhuaQianDesktop");
            string title = FirstNonEmpty(options.TaskTitle, plan == null ? "" : plan.Goal, step == null ? "" : step.Title, "agent-plan");
            string name = SafeFileName(title);
            return Path.Combine(dir, name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "." + format);
        }

        string BuildPlanText(AgentPlan plan)
        {
            if (plan == null) return "";
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(plan.Goal)) sb.AppendLine(plan.Goal).AppendLine();
            for (int i = 0; i < plan.Steps.Count; i++)
                sb.Append((i + 1).ToString()).Append(". ").AppendLine(plan.Steps[i].Title);
            return sb.ToString().Trim();
        }

        string InferExportFormat(AgentPlanStep step)
        {
            string value = ((step == null ? "" : step.Target) + " " + (step == null ? "" : step.Title)).ToLowerInvariant();
            if (value.Contains(".docx") || value.Contains("docx") || value.Contains("word")) return "docx";
            if (value.Contains(".pptx") || value.Contains("pptx") || value.Contains("powerpoint")) return "pptx";
            if (value.Contains(".xlsx") || value.Contains("xlsx") || value.Contains("excel")) return "xlsx";
            if (value.Contains(".pdf") || value.Contains("pdf")) return "pdf";
            if (value.Contains(".png") || value.Contains("png")) return "png";
            if (value.Contains(".md") || value.Contains("markdown")) return "md";
            return "txt";
        }

        string ExtractMilliseconds(string text)
        {
            var m = Regex.Match(text ?? "", @"(\d+)\s*(ms|millisecond|milliseconds|毫秒)", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(text ?? "", @"(\d+)\s*(s|sec|second|seconds|秒)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int seconds;
                if (int.TryParse(m.Groups[1].Value, out seconds)) return Math.Min(seconds * 1000, 60000).ToString();
            }
            m = Regex.Match(text ?? "", @"\b(\d{1,5})\b");
            return m.Success ? m.Groups[1].Value : "1000";
        }

        string ExtractOpenTarget(string text)
        {
            string target = StripLeadingVerb(text, "open", "launch", "打开", "启动", "啟動");
            if (string.IsNullOrWhiteSpace(target)) target = ExtractPath(text);
            return TrimTarget(target);
        }

        string ExtractPath(string text)
        {
            var m = Regex.Match(text ?? "", "\"([^\"]+)\"|'([^']+)'|([A-Za-z]:\\\\[^\"'<>|\r\n]+)");
            if (!m.Success) return "";
            for (int i = 1; i < m.Groups.Count; i++)
                if (m.Groups[i].Success) return TrimTarget(m.Groups[i].Value);
            return "";
        }

        string ExtractPid(string text)
        {
            var m = Regex.Match(text ?? "", @"(?:pid|process|进程|處理程序)\D{0,12}(\d{1,10})", RegexOptions.IgnoreCase);
            if (!m.Success) m = Regex.Match(text ?? "", @"\b(\d{2,10})\b");
            return m.Success ? m.Groups[1].Value : "";
        }

        string StripLeadingVerb(string text, params string[] verbs)
        {
            string value = text ?? "";
            foreach (string verb in verbs)
            {
                int index = value.IndexOf(verb, StringComparison.OrdinalIgnoreCase);
                if (index >= 0) return TrimTarget(value.Substring(index + verb.Length));
            }
            return TrimTarget(value);
        }

        string TrimTarget(string value)
        {
            if (value == null) return "";
            value = value.Trim();
            value = value.Trim('"', '\'', ' ', '\t', '\r', '\n', ':', '-', '，', '。', ';', '；');
            return value.Trim();
        }

        string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "agent-plan";
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
            value = Regex.Replace(value, @"\s+", "-").Trim('-');
            if (value.Length == 0) value = "agent-plan";
            if (value.Length > 48) value = value.Substring(0, 48).Trim('-');
            return value;
        }

        string DescribeStep(AgentPlanStep step)
        {
            if (step == null) return "unknown step";
            return "#" + step.Index.ToString() + " " + step.Title;
        }

        string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value)) return value;
            return "";
        }

        // Public wrapper so a runner can build a command for one step and keep
        // its StepId for per-step state correlation. No change to existing Map logic.
        public IAgentCommand BuildCommandForStep(AgentPlan plan, AgentPlanStep step, AgentPlanCommandMapperOptions options)
        {
            if (step == null) return null;
            return BuildCommand(plan, step, options);
        }
    }
}
