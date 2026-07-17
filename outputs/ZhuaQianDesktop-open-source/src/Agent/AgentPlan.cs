using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ZhuaQianDesktopApp.Agent
{
    public sealed class AgentPlan
    {
        public string Goal = "";
        public readonly List<AgentPlanStep> Steps = new List<AgentPlanStep>();
        public readonly List<string> Risks = new List<string>();
        public bool NeedsApproval;

        public string ToReviewMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Plan Review");
            if (!string.IsNullOrWhiteSpace(Goal)) sb.AppendLine("Goal: " + Goal);
            sb.AppendLine();
            for (int i = 0; i < Steps.Count; i++)
            {
                var step = Steps[i];
                sb.Append((i + 1).ToString()).Append(". ").Append(step.Title);
                if (!string.IsNullOrWhiteSpace(step.CommandType)) sb.Append(" [").Append(step.CommandType).Append("]");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(step.Target)) sb.AppendLine("   Target: " + step.Target);
                if (!string.IsNullOrWhiteSpace(step.Permission)) sb.AppendLine("   Permission: " + step.Permission);
            }
            if (Risks.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Risks:");
                foreach (string risk in Risks) sb.AppendLine("- " + risk);
            }
            sb.AppendLine();
            sb.AppendLine("Needs approval: " + (NeedsApproval ? "yes" : "no"));
            return sb.ToString().Trim();
        }
    }

    public sealed class AgentPlanStep
    {
        public int Index;
        public string Title = "";
        public string CommandType = "";
        public string Target = "";
        public string Permission = "";
        public string Raw = "";
    }

    public sealed class AgentPlanParser
    {
        public AgentPlan Parse(string text)
        {
            var plan = new AgentPlan();
            if (string.IsNullOrWhiteSpace(text)) return plan;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            plan.Goal = ExtractGoal(lines);

            foreach (string rawLine in lines)
            {
                string line = NormalizePlanLine(rawLine);
                if (line.Length == 0) continue;

                string lower = line.ToLowerInvariant();
                if (LooksLikeRisk(lower))
                {
                    plan.Risks.Add(StripRiskPrefix(line));
                    continue;
                }

                if (!LooksLikeStep(rawLine, line)) continue;
                var step = BuildStep(plan.Steps.Count + 1, line);
                if (!string.IsNullOrWhiteSpace(step.Title)) plan.Steps.Add(step);
            }

            if (plan.Steps.Count == 0)
            {
                var fallback = BuildStep(1, FirstMeaningfulLine(lines));
                if (!string.IsNullOrWhiteSpace(fallback.Title)) plan.Steps.Add(fallback);
            }

            foreach (var step in plan.Steps)
            {
                if (!string.IsNullOrWhiteSpace(step.Permission))
                {
                    plan.NeedsApproval = true;
                    break;
                }
            }
            if (plan.Risks.Count > 0) plan.NeedsApproval = true;
            return plan;
        }

        string ExtractGoal(string[] lines)
        {
            foreach (string raw in lines)
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0) continue;
                string stripped = Regex.Replace(line, "^#+\\s*", "").Trim();
                if (stripped.StartsWith("Goal:", StringComparison.OrdinalIgnoreCase))
                    return stripped.Substring(5).Trim();
                if (stripped.StartsWith("Objective:", StringComparison.OrdinalIgnoreCase))
                    return stripped.Substring(10).Trim();
                if (!LooksLikeStep(raw, NormalizePlanLine(raw)) && !LooksLikeRisk(stripped.ToLowerInvariant()))
                    return stripped;
            }
            return "";
        }

        string FirstMeaningfulLine(string[] lines)
        {
            foreach (string raw in lines)
            {
                string line = NormalizePlanLine(raw);
                if (line.Length > 0) return line;
            }
            return "";
        }

        string NormalizePlanLine(string raw)
        {
            string line = (raw ?? "").Trim();
            line = Regex.Replace(line, "^[-*]\\s+\\[[ xX]\\]\\s*", "");
            line = Regex.Replace(line, "^[-*]\\s+", "");
            line = Regex.Replace(line, "^[0-9]+[\\.)]\\s+", "");
            line = Regex.Replace(line, "^step\\s+[0-9]+\\s*[:\\.-]\\s*", "", RegexOptions.IgnoreCase);
            return line.Trim();
        }

        bool LooksLikeStep(string raw, string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return false;
            string line = (raw ?? "").Trim();
            if (Regex.IsMatch(line, "^[-*]\\s+\\[[ xX]\\]\\s+")) return true;
            if (Regex.IsMatch(line, "^[0-9]+[\\.)]\\s+")) return true;
            if (Regex.IsMatch(line, "^step\\s+[0-9]+\\s*[:\\.-]", RegexOptions.IgnoreCase)) return true;
            string lower = normalized.ToLowerInvariant();
            return lower.StartsWith("export ") || lower.StartsWith("save ") || lower.StartsWith("create ")
                || lower.StartsWith("generate ") || lower.StartsWith("organize ") || lower.StartsWith("run ")
                || lower.StartsWith("execute ") || lower.StartsWith("open ") || lower.StartsWith("end ")
                || lower.StartsWith("search ") || lower.StartsWith("index ");
        }

        bool LooksLikeRisk(string lower)
        {
            if (string.IsNullOrWhiteSpace(lower)) return false;
            return lower.StartsWith("risk") || lower.StartsWith("risks")
                || lower.StartsWith("permission") || lower.StartsWith("permissions")
                || lower.Contains("delete") || lower.Contains("move files") || lower.Contains("run script")
                || lower.Contains("cloud upload") || lower.Contains("network upload");
        }

        string StripRiskPrefix(string line)
        {
            return Regex.Replace(line ?? "", "^(risk|risks|permission|permissions)\\s*[:\\-]\\s*", "", RegexOptions.IgnoreCase).Trim();
        }

        AgentPlanStep BuildStep(int index, string line)
        {
            var step = new AgentPlanStep();
            step.Index = index;
            step.Raw = line ?? "";
            step.Title = step.Raw.Trim();

            string lower = step.Title.ToLowerInvariant();
            if (ContainsAny(lower, "export", "save", "generate", "create", "file", "docx", "ppt", "xlsx", "pdf", "png", "txt", "markdown"))
            {
                step.CommandType = "ExportFile";
                step.Permission = "permFileWrite";
            }
            else if (ContainsAny(lower, "organize folder", "organise folder", "整理"))
            {
                step.CommandType = "OrganizeFolder";
                step.Permission = "permFileMoveDelete";
            }
            else if (ContainsAny(lower, "open ", "launch ", "打开", "啟動", "启动"))
            {
                step.CommandType = "ComputerControl";
                step.Permission = "permAutomationInput";
            }
            else if (ContainsAny(lower, "run plugin", "execute plugin", ".ps1", ".py", ".bat", ".cmd", ".exe"))
            {
                step.CommandType = "RunPlugin";
                step.Permission = "permPluginRun";
            }
            else if (ContainsAny(lower, "end process", "kill process", "terminate process", "pid"))
            {
                step.CommandType = "EndProcess";
                step.Permission = "permProcessManage";
            }
            else if (ContainsAny(lower, "search", "latest", "current", "web", "news", "2026", "最新"))
            {
                step.CommandType = "WebSearch";
                step.Permission = "permNetworkUpload";
            }
            step.Target = ExtractTarget(step.Title);
            return step;
        }

        string ExtractTarget(string line)
        {
            var m = Regex.Match(line ?? "", "\"([^\"]+)\"");
            if (m.Success) return m.Groups[1].Value.Trim();
            m = Regex.Match(line ?? "", "'([^']+)'");
            if (m.Success) return m.Groups[1].Value.Trim();
            m = Regex.Match(line ?? "", "([A-Za-z]:\\\\[^\\s]+)");
            if (m.Success) return m.Groups[1].Value.Trim();
            return "";
        }

        bool ContainsAny(string value, params string[] terms)
        {
            foreach (string term in terms)
            {
                if (!string.IsNullOrEmpty(term) && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
