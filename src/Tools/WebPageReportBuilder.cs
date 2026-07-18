using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ZhuaQianDesktopApp.Tools
{
    public sealed class WebPageAnalysisReport
    {
        public string Markdown = "";
        public int SuccessCount;
        public int FailureCount;
        public int SearchResultCount;
        public string TitleHint = "网站分析报告";
    }

    public static class WebPageReportBuilder
    {
        public static WebPageAnalysisReport Build(string userRequest, IList<WebPageFetchResult> pages, DateTime capturedAt)
        {
            return Build(userRequest, pages, null, "", "", capturedAt);
        }

        public static WebPageAnalysisReport Build(
            string userRequest,
            IList<WebPageFetchResult> pages,
            IList<WebSearchResult> searchResults,
            string searchProvider,
            string searchQuery,
            DateTime capturedAt)
        {
            var report = new WebPageAnalysisReport();
            var sb = new StringBuilder();
            var successfulPages = SuccessfulPages(pages);
            report.SuccessCount = successfulPages.Count;
            report.FailureCount = CountFailures(pages);
            report.SearchResultCount = searchResults == null ? 0 : searchResults.Count;
            if (successfulPages.Count > 0 && !string.IsNullOrWhiteSpace(successfulPages[0].Title))
                report.TitleHint = CleanFileTitle(successfulPages[0].Title);

            sb.AppendLine("# 网站深度分析报告");
            sb.AppendLine();
            sb.AppendLine("- 生成时间: " + capturedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            if (!string.IsNullOrWhiteSpace(userRequest))
                sb.AppendLine("- 用户需求: " + OneLine(userRequest, 260));
            sb.AppendLine("- 读取范围: 成功读取 " + report.SuccessCount + " 个网页；失败 " + report.FailureCount + " 个网页；搜索结果 " + report.SearchResultCount + " 条。");
            if (!string.IsNullOrWhiteSpace(searchQuery))
                sb.AppendLine("- 搜索查询: " + OneLine(searchQuery, 220));
            if (!string.IsNullOrWhiteSpace(searchProvider))
                sb.AppendLine("- 搜索来源: " + searchProvider);
            sb.AppendLine("- 可信边界: 下面结论只基于本次真实抓取到的网页文本和搜索摘要；未成功读取的页面不做内容推断。");
            sb.AppendLine();

            AppendSearchResults(sb, searchResults);
            AppendExecutiveSummary(sb, successfulPages);
            AppendEvidenceMatrix(sb, pages);
            AppendCrossSourceAnalysis(sb, successfulPages);
            AppendRiskAndGaps(sb, pages, searchResults);

            report.Markdown = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(report.TitleHint)) report.TitleHint = "网站分析报告";
            return report;
        }

        static void AppendSearchResults(StringBuilder sb, IList<WebSearchResult> searchResults)
        {
            sb.AppendLine("## 搜索引擎返回的候选来源");
            sb.AppendLine();
            if (searchResults == null || searchResults.Count == 0)
            {
                sb.AppendLine("- 未拿到搜索结果；本报告只使用用户提供的网址。");
                sb.AppendLine();
                return;
            }

            for (int i = 0; i < searchResults.Count; i++)
            {
                WebSearchResult result = searchResults[i];
                sb.AppendLine((i + 1).ToString() + ". " + OneLine(result.Title, 120));
                sb.AppendLine("   - URL: " + result.Url);
                if (!string.IsNullOrWhiteSpace(result.Snippet))
                    sb.AppendLine("   - 摘要: " + OneLine(result.Snippet, 220));
            }
            sb.AppendLine();
        }

        static void AppendExecutiveSummary(StringBuilder sb, IList<WebPageFetchResult> pages)
        {
            sb.AppendLine("## 综合摘要");
            sb.AppendLine();
            if (pages == null || pages.Count == 0)
            {
                sb.AppendLine("- 没有成功读取到网页正文，无法生成可靠综合分析。");
                sb.AppendLine();
                return;
            }

            List<string> bullets = TopEvidenceLines(pages, 6, 230);
            foreach (string item in bullets)
                sb.AppendLine("- " + item);
            sb.AppendLine();
        }

        static void AppendEvidenceMatrix(StringBuilder sb, IList<WebPageFetchResult> pages)
        {
            sb.AppendLine("## 来源证据");
            sb.AppendLine();
            if (pages == null || pages.Count == 0)
            {
                sb.AppendLine("- 未检测到可读取的网址。");
                sb.AppendLine();
                return;
            }

            for (int i = 0; i < pages.Count; i++)
            {
                WebPageFetchResult page = pages[i];
                sb.AppendLine("### 来源 " + (i + 1).ToString());
                sb.AppendLine();
                sb.AppendLine("- URL: " + (page == null ? "(unknown)" : page.Url));
                if (page == null || !page.Success)
                {
                    string error = page == null ? "Unknown fetch error." : page.ErrorMessage;
                    sb.AppendLine("- 抓取状态: 失败");
                    sb.AppendLine("- 失败原因: " + (string.IsNullOrWhiteSpace(error) ? "Unknown fetch error." : error));
                    sb.AppendLine();
                    sb.AppendLine("> 该页面未被成功读取，因此不生成摘要、评分或结论。");
                    sb.AppendLine();
                    continue;
                }

                string text = page.Text ?? "";
                sb.AppendLine("- 抓取状态: 成功");
                if (!string.IsNullOrWhiteSpace(page.Title)) sb.AppendLine("- 标题: " + OneLine(page.Title, 180));
                sb.AppendLine("- 抓取文本长度: " + text.Length.ToString() + " 字符");
                sb.AppendLine();

                sb.AppendLine("**核心摘录**");
                foreach (string item in KeyLines(text, 5, 210))
                    sb.AppendLine("- " + item);
                sb.AppendLine();
            }
        }

        static void AppendCrossSourceAnalysis(StringBuilder sb, IList<WebPageFetchResult> pages)
        {
            sb.AppendLine("## 多来源分析");
            sb.AppendLine();
            if (pages == null || pages.Count == 0)
            {
                sb.AppendLine("- 需要至少一个成功来源才能做多来源分析。");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("### 共同证据");
            foreach (string item in TopEvidenceLines(pages, 5, 220))
                sb.AppendLine("- " + item);
            sb.AppendLine();

            sb.AppendLine("### 重点字段");
            AppendFieldFindings(sb, pages, "价格/定价", "价格", "定价", "pricing", "price", "plan", "plans");
            AppendFieldFindings(sb, pages, "发布时间/版本", "发布", "更新", "版本", "release", "released", "version", "updated");
            AppendFieldFindings(sb, pages, "功能/能力", "功能", "能力", "支持", "feature", "features", "capability", "supports");
            AppendFieldFindings(sb, pages, "限制/风险", "限制", "风险", "隐私", "条款", "limit", "risk", "privacy", "terms", "policy");
            sb.AppendLine();

            sb.AppendLine("### 初步判断");
            sb.AppendLine("- 如果多个来源都指向同一事实，该事实可作为报告正文的候选依据；如果只在单一来源出现，应在最终文件里标注来源。");
            sb.AppendLine("- 如果页面正文很短、存在脚本渲染或登录限制，本报告应被视为“初步网页研究”，不应冒充完整人工尽调。");
            sb.AppendLine();
        }

        static void AppendRiskAndGaps(StringBuilder sb, IList<WebPageFetchResult> pages, IList<WebSearchResult> searchResults)
        {
            sb.AppendLine("## 风险与缺口");
            sb.AppendLine();
            int failed = CountFailures(pages);
            if (failed > 0)
                sb.AppendLine("- 有 " + failed.ToString() + " 个页面抓取失败；这些页面不能作为已阅读证据。");
            if (searchResults == null || searchResults.Count == 0)
                sb.AppendLine("- 搜索引擎没有返回候选来源，覆盖面有限。");
            if (SuccessfulPages(pages).Count < 3)
                sb.AppendLine("- 成功来源少于 3 个，深度分析可信度有限，建议补充更多来源。");
            sb.AppendLine("- 对强 JavaScript 渲染、登录后内容、反爬页面，当前版本只能读取服务端返回的可见文本。");
            sb.AppendLine();

            sb.AppendLine("## 下一步建议");
            sb.AppendLine();
            sb.AppendLine("- 将本报告保存为 Markdown/Word/PDF 后，可继续要求模型基于这些真实来源改写成商业报告、竞品分析、调研纪要或 PPT 大纲。");
            sb.AppendLine("- 如需更完整读取动态网页，下一步应接入浏览器渲染抓取，而不是只用 HTTP 文本抓取。");
        }

        static void AppendFieldFindings(StringBuilder sb, IList<WebPageFetchResult> pages, string label, params string[] keywords)
        {
            sb.AppendLine("**" + label + "**");
            List<string> lines = new List<string>();
            foreach (WebPageFetchResult page in pages)
            {
                foreach (string line in KeyLines(page == null ? "" : page.Text, 8, 180))
                {
                    if (!ContainsAny(line, keywords)) continue;
                    if (!ContainsSimilar(lines, line)) lines.Add(line);
                    if (lines.Count >= 3) break;
                }
                if (lines.Count >= 3) break;
            }
            if (lines.Count == 0) sb.AppendLine("- 本次抓取文本中没有稳定命中。");
            else foreach (string line in lines) sb.AppendLine("- " + line);
        }

        static List<WebPageFetchResult> SuccessfulPages(IList<WebPageFetchResult> pages)
        {
            var result = new List<WebPageFetchResult>();
            if (pages == null) return result;
            foreach (WebPageFetchResult page in pages)
                if (page != null && page.Success && !string.IsNullOrWhiteSpace(page.Text))
                    result.Add(page);
            return result;
        }

        static int CountFailures(IList<WebPageFetchResult> pages)
        {
            if (pages == null || pages.Count == 0) return pages == null ? 1 : 0;
            int count = 0;
            foreach (WebPageFetchResult page in pages)
                if (page == null || !page.Success) count++;
            return count;
        }

        static List<string> TopEvidenceLines(IList<WebPageFetchResult> pages, int max, int lineMax)
        {
            var result = new List<string>();
            foreach (WebPageFetchResult page in pages)
            {
                foreach (string line in KeyLines(page == null ? "" : page.Text, 12, lineMax))
                {
                    if (ContainsSimilar(result, line)) continue;
                    result.Add(WithSource(line, page));
                    if (result.Count >= max) return result;
                }
            }
            if (result.Count == 0) result.Add("页面文本较短或结构不清晰，未能抽取稳定摘要。");
            return result;
        }

        static string WithSource(string line, WebPageFetchResult page)
        {
            if (page == null || string.IsNullOrWhiteSpace(page.Url)) return line;
            return line + " （来源: " + page.Url + "）";
        }

        static List<string> KeyLines(string text, int max, int lineMax)
        {
            var result = new List<string>();
            foreach (string paragraph in SplitParagraphs(text))
            {
                string value = OneLine(paragraph, lineMax);
                if (value.Length < 12) continue;
                if (LooksBoilerplate(value)) continue;
                if (ContainsAny(value, "价格", "定价", "发布", "更新", "版本", "功能", "服务", "隐私", "条款", "联系", "报告", "研究", "数据", "客户", "企业", "download", "pricing", "release", "feature", "privacy", "terms", "contact", "report", "research", "data", "customer", "enterprise"))
                    result.Add(value);
                if (result.Count >= max) break;
            }
            if (result.Count == 0)
            {
                foreach (string paragraph in SplitParagraphs(text))
                {
                    string value = OneLine(paragraph, lineMax);
                    if (value.Length < 12 || LooksBoilerplate(value)) continue;
                    result.Add(value);
                    if (result.Count >= max) break;
                }
            }
            return result;
        }

        static IEnumerable<string> SplitParagraphs(string text)
        {
            foreach (string raw in Regex.Split(text ?? "", @"\r?\n+|(?<=[。！？.!?])\s+"))
            {
                string value = Regex.Replace(raw ?? "", @"\s+", " ").Trim();
                if (value.Length > 0) yield return value;
            }
        }

        static bool LooksBoilerplate(string value)
        {
            return ContainsAny(value, "cookie", "cookies", "javascript", "enable", "privacy preferences", "all rights reserved", "版权所有", "隐私偏好", "订阅", "newsletter");
        }

        static bool ContainsSimilar(IList<string> existing, string value)
        {
            string norm = NormalizeForCompare(value);
            foreach (string item in existing)
            {
                string other = NormalizeForCompare(item);
                if (norm.Length > 0 && (other.Contains(norm) || norm.Contains(other))) return true;
            }
            return false;
        }

        static string NormalizeForCompare(string value)
        {
            value = Regex.Replace(value ?? "", @"\s+", "").ToLowerInvariant();
            return value.Length > 80 ? value.Substring(0, 80) : value;
        }

        static bool ContainsAny(string value, params string[] needles)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (string needle in needles)
                if (!string.IsNullOrWhiteSpace(needle) && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static string OneLine(string value, int max)
        {
            value = Regex.Replace(value ?? "", @"\s+", " ").Trim();
            if (value.Length <= max) return value;
            return value.Substring(0, Math.Max(0, max - 3)).TrimEnd() + "...";
        }

        static string CleanFileTitle(string value)
        {
            value = Regex.Replace(value ?? "", "[\\\\/:*?\"<>|]+", "_").Trim();
            value = Regex.Replace(value, @"\s+", " ").Trim(' ', '.', '_', '-');
            if (value.Length > 36) value = value.Substring(0, 36).Trim();
            return value.Length == 0 ? "网站分析报告" : value;
        }
    }
}
