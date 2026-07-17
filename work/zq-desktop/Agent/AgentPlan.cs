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
                string id = string.IsNullOrWhiteSpace(step.StepId) ? (i + 1).ToString() : step.StepId;
                sb.Append(id).Append(". ").Append(step.Title);
                if (!string.IsNullOrWhiteSpace(step.CommandType)) sb.Append(" [").Append(step.CommandType).Append("]");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(step.Target)) sb.AppendLine("   Target: " + step.Target);
                if (!string.IsNullOrWhiteSpace(step.Permission)) sb.AppendLine("   Permission: " + step.Permission);
                if (!string.IsNullOrWhiteSpace(step.RiskLevel)) sb.AppendLine("   Risk: " + step.RiskLevel);
                if (!string.IsNullOrWhiteSpace(step.ExpectedOutput)) sb.AppendLine("   Expected output: " + step.ExpectedOutput);
                if (!string.IsNullOrWhiteSpace(step.Status)) sb.AppendLine("   Status: " + step.Status);
                sb.AppendLine("   Rollback possible: " + (step.RollbackPossible ? "yes" : "no"));
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
        public string StepId = "";
        public string Title = "";
        public string CommandType = "";
        public string Target = "";
        public string Permission = "";
        public string RiskLevel = "";
        public string ExpectedOutput = "";
        public bool RollbackPossible;
        public string Status = "pending";
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

            AddStructuredSteps(plan, lines);

            if (plan.Steps.Count == 0)
            {
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

        void AddStructuredSteps(AgentPlan plan, string[] lines)
        {
            AgentPlanStep current = null;
            foreach (string raw in lines)
            {
                string line = NormalizeKeyValueLine(raw);
                if (line.Length == 0) continue;

                string key;
                string value;
                if (!TrySplitKeyValue(line, out key, out value)) continue;

                if (IsStepStartKey(key))
                {
                    if (current != null && !string.IsNullOrWhiteSpace(current.Title)) FinalizeStructuredStep(plan, current);
                    current = new AgentPlanStep();
                    current.Index = plan.Steps.Count + 1;
                    current.StepId = value.Length == 0 ? "S" + current.Index.ToString() : value;
                    current.Status = "pending";
                    continue;
                }

                if (current == null && IsStepFieldKey(key))
                {
                    current = new AgentPlanStep();
                    current.Index = plan.Steps.Count + 1;
                    current.StepId = "S" + current.Index.ToString();
                    current.Status = "pending";
                }

                if (current != null) ApplyStepField(current, key, value);
            }

            if (current != null && !string.IsNullOrWhiteSpace(current.Title)) FinalizeStructuredStep(plan, current);
        }

        string NormalizeKeyValueLine(string raw)
        {
            string line = (raw ?? "").Trim();
            line = Regex.Replace(line, "^[-*]\\s+", "");
            line = Regex.Replace(line, "^\\{\\s*", "");
            line = Regex.Replace(line, "\\s*[,}]\\s*$", "");
            return line.Trim();
        }

        bool TrySplitKeyValue(string line, out string key, out string value)
        {
            key = "";
            value = "";
            var m = Regex.Match(line ?? "", "^\"?([A-Za-z][A-Za-z0-9_-]*)\"?\\s*[:=]\\s*\"?(.*?)\"?$");
            if (!m.Success) return false;
            key = m.Groups[1].Value.Trim();
            value = m.Groups[2].Value.Trim().Trim(',');
            value = value.Trim().Trim('"');
            return key.Length > 0;
        }

        bool IsStepStartKey(string key)
        {
            return string.Equals(key, "stepId", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "step", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "id", StringComparison.OrdinalIgnoreCase);
        }

        bool IsStepFieldKey(string key)
        {
            return string.Equals(key, "title", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "actionType", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "target", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "riskLevel", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "requiredPermission", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "permission", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "expectedOutput", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "rollbackPossible", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "status", StringComparison.OrdinalIgnoreCase);
        }

        void ApplyStepField(AgentPlanStep step, string key, string value)
        {
            if (string.Equals(key, "title", StringComparison.OrdinalIgnoreCase)) step.Title = value;
            else if (string.Equals(key, "actionType", StringComparison.OrdinalIgnoreCase)) step.CommandType = NormalizeCommandType(value);
            else if (string.Equals(key, "target", StringComparison.OrdinalIgnoreCase)) step.Target = value;
            else if (string.Equals(key, "riskLevel", StringComparison.OrdinalIgnoreCase)) step.RiskLevel = value;
            else if (string.Equals(key, "requiredPermission", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "permission", StringComparison.OrdinalIgnoreCase)) step.Permission = value;
            else if (string.Equals(key, "expectedOutput", StringComparison.OrdinalIgnoreCase)) step.ExpectedOutput = value;
            else if (string.Equals(key, "rollbackPossible", StringComparison.OrdinalIgnoreCase)) step.RollbackPossible = IsTruthy(value);
            else if (string.Equals(key, "status", StringComparison.OrdinalIgnoreCase)) step.Status = string.IsNullOrWhiteSpace(value) ? "pending" : value;
        }

        void FinalizeStructuredStep(AgentPlan plan, AgentPlanStep step)
        {
            var classified = BuildStep(step.Index, step.Title);
            if (string.IsNullOrWhiteSpace(step.CommandType)) step.CommandType = classified.CommandType;
            if (string.IsNullOrWhiteSpace(step.Permission)) step.Permission = classified.Permission;
            if (string.IsNullOrWhiteSpace(step.Target)) step.Target = classified.Target;
            if (string.IsNullOrWhiteSpace(step.RiskLevel)) step.RiskLevel = InferRiskLevel(step);
            if (string.IsNullOrWhiteSpace(step.Status)) step.Status = "pending";
            step.Raw = step.Title;
            plan.Steps.Add(step);
        }

        string NormalizeCommandType(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0) return "";
            string lower = value.ToLowerInvariant();
            if (lower == "export" || lower == "exportfile" || lower == "writefile") return "ExportFile";
            if (lower == "organize" || lower == "organizefolder" || lower == "movefiles") return "OrganizeFolder";
            if (lower == "plugin" || lower == "runplugin" || lower == "script") return "RunPlugin";
            if (lower == "process" || lower == "endprocess" || lower == "killprocess") return "EndProcess";
            if (lower == "computercontrol" || lower == "open" || lower == "hotkey") return "ComputerControl";
            if (lower == "websearch" || lower == "search") return "WebSearch";
            return value;
        }

        bool IsTruthy(string value)
        {
            value = (value ?? "").Trim().ToLowerInvariant();
            return value == "true" || value == "yes" || value == "1" || value == "y";
        }

        string InferRiskLevel(AgentPlanStep step)
        {
            string permission = (step.Permission ?? "").ToLowerInvariant();
            if (permission.Contains("movedelete") || permission.Contains("process") || permission.Contains("plugin")) return "high";
            if (permission.Contains("write") || permission.Contains("network") || permission.Contains("automation")) return "medium";
            return "low";
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
                || lower.StartsWith("search ") || lower.StartsWith("index ") || lower.StartsWith("wait ")
                || lower.StartsWith("sleep ") || lower.StartsWith("pause ");
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
            step.StepId = "S" + index.ToString();
            step.Raw = line ?? "";
            step.Title = step.Raw.Trim();
            step.Status = "pending";

            string lower = step.Title.ToLowerInvariant();
            if (ContainsAny(lower, "export", "save", "generate", "create", "file", "docx", "ppt", "xlsx", "pdf", "png", "txt", "markdown"))
            {
                step.CommandType = "ExportFile";
                step.Permission = "permFileWrite";
                step.ExpectedOutput = "Generated file";
            }
            else if (ContainsAny(lower, "organize folder", "organise folder", "整理"))
            {
                step.CommandType = "OrganizeFolder";
                step.Permission = "permFileMoveDelete";
                step.ExpectedOutput = "Organized folder and rollback manifest";
                step.RollbackPossible = true;
            }
            else if (ContainsAny(lower, "open ", "launch ", "打开", "啟動", "启动", "wait ", "sleep ", "pause "))
            {
                step.CommandType = "ComputerControl";
                step.Permission = "permAutomationInput";
                step.ExpectedOutput = "Windows desktop action";
            }
            else if (ContainsAny(lower, "run plugin", "execute plugin", ".ps1", ".py", ".bat", ".cmd", ".exe"))
            {
                step.CommandType = "RunPlugin";
                step.Permission = "permPluginRun";
                step.ExpectedOutput = "Plugin output log";
            }
            else if (ContainsAny(lower, "end process", "kill process", "terminate process", "pid"))
            {
                step.CommandType = "EndProcess";
                step.Permission = "permProcessManage";
                step.ExpectedOutput = "Process ended";
            }
            else if (ContainsAny(lower, "search", "latest", "current", "web", "news", "2026", "最新"))
            {
                step.CommandType = "WebSearch";
                step.Permission = "permNetworkUpload";
                step.ExpectedOutput = "Search result summary";
            }
            step.Target = ExtractTarget(step.Title);
            step.RiskLevel = InferRiskLevel(step);
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
