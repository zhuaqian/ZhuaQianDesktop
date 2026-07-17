using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ZhuaQianDesktopApp.Knowledge
{
    public class ChunkedDoc
    {
        public string DocId;
        public string ChunkId;
        public string Path;
        public string Name;
        public string Heading;
        public string Text;
        public string Summary;
        public string Tags;
        public string Layer;
        public int Offset;
        public long SizeBytes;
        public DateTime ModifiedAt;
    }

    public class Chunker
    {
        readonly int defaultMaxChars = 1800;

        public Chunker() { }

        public Chunker(int maxChars)
        {
            defaultMaxChars = maxChars;
        }

        public List<string> Split(string text, int maxChars = 0)
        {
            if (maxChars <= 0) maxChars = defaultMaxChars;
            var chunks = new List<string>();
            var current = new StringBuilder();
            string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                string line = raw ?? "";
                string trimmed = line.Trim();
                bool heading = Regex.IsMatch(trimmed, @"^(#{1,6}\s+|第.{1,12}[章节条]|[0-9一二三四五六七八九十]+[\.、]\s*)");
                if (current.Length > 0 && (current.Length + line.Length > maxChars || (heading && current.Length > 350)))
                {
                    chunks.Add(current.ToString().Trim());
                    current.Length = 0;
                }
                current.Append(line).Append('\n');
            }
            if (current.Length > 0) chunks.Add(current.ToString().Trim());
            if (chunks.Count == 0 && !string.IsNullOrWhiteSpace(text))
                chunks.Add(TrimTo(text, maxChars));
            return chunks;
        }

        public string DetectHeading(string text, string fallback)
        {
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = Regex.Replace((raw ?? "").Trim(), @"^#{1,6}\s*", "");
                if (line.Length == 0) continue;
                if (line.Length > 90) line = line.Substring(0, 90);
                return line;
            }
            return fallback ?? "";
        }

        public string BuildSummary(string text, int max = 220)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string normalized = Regex.Replace(text, "\\s+", " ").Trim();
            if (normalized.Length <= max) return normalized;
            return normalized.Substring(0, max) + "...";
        }

        public string InferTags(string name, string text)
        {
            string hay = ((name ?? "") + "\n" + (text ?? "")).ToLowerInvariant();
            var tags = new List<string>();
            AddIf(tags, hay, "finance", "invoice", "payment", "budget", "revenue", "sales", "tax", "账", "发票", "付款", "财务", "销售", "收入");
            AddIf(tags, hay, "code", "error", "exception", "debug", "api", "function", "class", "python", "javascript", "错误", "代码", "接口", "调试");
            AddIf(tags, hay, "meeting", "meeting", "minutes", "todo", "action item", "deadline", "会议", "纪要", "待办", "负责人");
            AddIf(tags, hay, "contract", "contract", "agreement", "party a", "party b", "合同", "协议", "甲方", "乙方");
            AddIf(tags, hay, "marketing", "copy", "campaign", "brand", "ad", "文案", "品牌", "投放", "营销");
            AddIf(tags, hay, "personal", "resume", "profile", "身份证", "手机号", "银行卡", "邮箱");
            if (tags.Count == 0) tags.Add("general");
            return string.Join(",", tags.ToArray());
        }

        public string InferLayer(string path, DateTime modifiedAt)
        {
            string lower = (path ?? "").ToLowerInvariant();
            if (lower.Contains("\\temp\\") || lower.Contains("\\tmp\\") || lower.Contains("\\download") || lower.Contains("临时") || lower.Contains("暫存"))
                return "temp";
            if ((DateTime.Now - modifiedAt).TotalDays > 180 || lower.Contains("archive") || lower.Contains("归档") || lower.Contains("歸檔"))
                return "cold";
            return "hot";
        }

        public string StableDocId(string path)
        {
            int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(path ?? "");
            return Math.Abs(hash).ToString("x8");
        }

        void AddIf(List<string> tags, string hay, string tag, params string[] needles)
        {
            if (tags.Contains(tag)) return;
            foreach (var needle in needles)
            {
                if (!string.IsNullOrWhiteSpace(needle) && hay.IndexOf(needle.ToLowerInvariant(), StringComparison.Ordinal) >= 0)
                {
                    tags.Add(tag);
                    return;
                }
            }
        }

        string TrimTo(string text, int max)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            if (text.Length <= max) return text;
            return text.Substring(0, max) + "\n\n[content truncated]";
        }
    }
}
