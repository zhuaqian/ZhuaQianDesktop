using System;
using System.Collections.Generic;
using System.Text;

namespace ZhuaQianDesktopApp.Documents
{
    public enum OfficeTemplateKind
    {
        SalesPitch,
        MeetingMinutes,
        Report,
        DataTable,
        Poster
    }

    public sealed class TemplateContext
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Author { get; set; }
        public string Date { get; set; }
        public string Closing { get; set; }
        public Dictionary<string, List<string>> Bullets { get; set; }
        public List<TableColumn> Columns { get; set; }
        public List<List<string>> Rows { get; set; }

        public TemplateContext()
        {
            Title = "未命名文档";
            Subtitle = "";
            Author = "";
            Date = "";
            Closing = "";
            Bullets = new Dictionary<string, List<string>>();
            Columns = new List<TableColumn>();
            Rows = new List<List<string>>();
        }
    }

    public sealed class TableColumn
    {
        public string Name { get; set; }

        public TableColumn(string name)
        {
            Name = name ?? "";
        }
    }

    public sealed class TemplateResult
    {
        public OfficeTemplateKind Kind { get; private set; }
        public string Text { get; private set; }
        public string SuggestedExtension { get; private set; }

        public TemplateResult(OfficeTemplateKind kind, string text, string ext)
        {
            Kind = kind;
            Text = text ?? "";
            SuggestedExtension = ext ?? "txt";
        }
    }

    public static class OfficeTemplateLibrary
    {
        public static TemplateResult Render(OfficeTemplateKind kind, TemplateContext ctx)
        {
            ctx = ctx ?? new TemplateContext();
            switch (kind)
            {
                case OfficeTemplateKind.SalesPitch:
                    return new TemplateResult(kind, RenderSalesPitch(ctx), "pptx");
                case OfficeTemplateKind.MeetingMinutes:
                    return new TemplateResult(kind, RenderMeetingMinutes(ctx), "docx");
                case OfficeTemplateKind.Report:
                    return new TemplateResult(kind, RenderReport(ctx), "docx");
                case OfficeTemplateKind.DataTable:
                    return new TemplateResult(kind, RenderDataTable(ctx), "xlsx");
                case OfficeTemplateKind.Poster:
                    return new TemplateResult(kind, RenderPoster(ctx), "png");
                default:
                    throw new ArgumentOutOfRangeException("kind");
            }
        }

        public static string SuggestedExtension(OfficeTemplateKind kind)
        {
            switch (kind)
            {
                case OfficeTemplateKind.SalesPitch: return "pptx";
                case OfficeTemplateKind.MeetingMinutes: return "docx";
                case OfficeTemplateKind.Report: return "docx";
                case OfficeTemplateKind.DataTable: return "xlsx";
                case OfficeTemplateKind.Poster: return "png";
                default: return "txt";
            }
        }

        public static TemplateResult RenderByName(string name, TemplateContext ctx)
        {
            string value = (name ?? "").ToLowerInvariant().Trim();
            switch (value)
            {
                case "salespitch":
                case "sales-pitch":
                case "pitch":
                case "销售演示":
                    return Render(OfficeTemplateKind.SalesPitch, ctx);
                case "meetingminutes":
                case "meeting-minutes":
                case "minutes":
                case "会议纪要":
                    return Render(OfficeTemplateKind.MeetingMinutes, ctx);
                case "datatable":
                case "data-table":
                case "table":
                case "数据表":
                    return Render(OfficeTemplateKind.DataTable, ctx);
                case "poster":
                case "海报":
                    return Render(OfficeTemplateKind.Poster, ctx);
                case "report":
                case "报告":
                default:
                    return Render(OfficeTemplateKind.Report, ctx);
            }
        }

        public static IList<string> ListKinds()
        {
            return new List<string>
            {
                "SalesPitch (销售演示)",
                "MeetingMinutes (会议纪要)",
                "Report (报告)",
                "DataTable (数据表)",
                "Poster (海报)"
            };
        }

        public static string RenderSalesPitch(TemplateContext ctx)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(CleanTitle(ctx.Title)).AppendLine();
            AppendOptional(sb, ctx.Subtitle);
            AppendOptional(sb, ctx.Closing);
            sb.AppendLine();

            AppendSlide(sb, "痛点 / The Problem", BulletsOr(ctx, "痛点",
                new[] { "手动流程耗时费力", "信息分散难追踪", "成本高且容易出错" }));
            AppendSlide(sb, "方案 / Our Solution", BulletsOr(ctx, "方案",
                new[] { "一站式自动化处理", "可视化进度与审计", "安全可控并可回滚" }));
            AppendSlide(sb, "价值 / Why Us", BulletsOr(ctx, "价值",
                new[] { "效率提升", "减少重复劳动", "开箱即可使用" }));
            AppendSlide(sb, "行动 / Get Started", BulletsOr(ctx, "行动",
                new[] { "预约一次演示", "用一个真实任务试跑" }));
            return sb.ToString();
        }

        public static string RenderMeetingMinutes(TemplateContext ctx)
        {
            var sb = new StringBuilder();
            sb.Append("# 会议纪要：").Append(CleanTitle(ctx.Title)).AppendLine();
            sb.Append("日期：").Append(string.IsNullOrEmpty(ctx.Date) ? "待填" : ctx.Date)
              .Append("    记录人：").Append(string.IsNullOrEmpty(ctx.Author) ? "待填" : ctx.Author)
              .AppendLine().AppendLine();

            AppendSection(sb, "参会与议程", BulletsOr(ctx, "议程",
                new[] { "确认本次会议目标", "同步近期进展", "讨论待决事项" }));
            AppendSection(sb, "讨论要点", BulletsOr(ctx, "讨论",
                new[] { "关键风险与阻塞点", "资源与排期评估", "方案取舍说明" }));
            AppendSection(sb, "决议事项", BulletsOr(ctx, "决议",
                new[] { "明确负责人和截止时间", "锁定下一里程碑" }));
            AppendSection(sb, "待办跟进", BulletsOr(ctx, "待办",
                new[] { "负责人整理行动项", "下次会议复盘" }));
            AppendOptional(sb, ctx.Closing);
            return sb.ToString();
        }

        public static string RenderReport(TemplateContext ctx)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(CleanTitle(ctx.Title)).AppendLine();
            AppendOptional(sb, ctx.Subtitle);
            sb.Append("作者：").Append(string.IsNullOrEmpty(ctx.Author) ? "待填" : ctx.Author)
              .Append("    日期：").Append(string.IsNullOrEmpty(ctx.Date) ? "待填" : ctx.Date)
              .AppendLine().AppendLine();

            AppendSection(sb, "摘要", BulletsOr(ctx, "摘要", new[] { "一句话概述结论与价值" }));
            AppendSection(sb, "1. 背景", BulletsOr(ctx, "背景", new[] { "问题来源与现状", "涉及的系统与干系人" }));
            AppendSection(sb, "2. 分析", BulletsOr(ctx, "分析", new[] { "数据与方法", "关键发现" }));
            AppendSection(sb, "3. 结论", BulletsOr(ctx, "结论", new[] { "核心结论", "建议与下一步" }));
            AppendOptional(sb, ctx.Closing);
            return sb.ToString();
        }

        public static string RenderDataTable(TemplateContext ctx)
        {
            var cols = ctx.Columns;
            if (cols == null || cols.Count == 0)
                cols = new List<TableColumn> { new TableColumn("项目"), new TableColumn("数值"), new TableColumn("备注") };

            var rows = ctx.Rows;
            if (rows == null || rows.Count == 0)
            {
                rows = new List<List<string>>
                {
                    new List<string> { "示例 A", "100", "占位" },
                    new List<string> { "示例 B", "200", "占位" }
                };
            }

            var sb = new StringBuilder();
            sb.Append("| ");
            foreach (var c in cols) sb.Append(c.Name).Append(" | ");
            sb.AppendLine();
            sb.Append("| ");
            for (int i = 0; i < cols.Count; i++) sb.Append("--- | ");
            sb.AppendLine();

            foreach (var row in rows)
            {
                sb.Append("| ");
                for (int i = 0; i < cols.Count; i++)
                {
                    string cell = (row != null && i < row.Count) ? row[i] : "";
                    sb.Append(cell).Append(" | ");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static string RenderPoster(TemplateContext ctx)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(CleanTitle(ctx.Title)).AppendLine();
            AppendOptional(sb, ctx.Subtitle);
            AppendOptional(sb, ctx.Closing);
            return sb.ToString();
        }

        static void AppendSlide(StringBuilder sb, string title, IEnumerable<string> bullets)
        {
            sb.Append("# ").Append(title).AppendLine();
            foreach (var b in bullets) sb.Append("- ").Append(b).AppendLine();
            sb.AppendLine();
        }

        static void AppendSection(StringBuilder sb, string title, IEnumerable<string> bullets)
        {
            sb.Append("## ").Append(title).AppendLine();
            foreach (var b in bullets) sb.Append("- ").Append(b).AppendLine();
            sb.AppendLine();
        }

        static IList<string> BulletsOr(TemplateContext ctx, string key, string[] defaults)
        {
            List<string> list;
            if (ctx.Bullets != null && ctx.Bullets.TryGetValue(key, out list) && list != null && list.Count > 0)
                return list;
            return new List<string>(defaults);
        }

        static string CleanTitle(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "未命名文档" : value;
        }

        static void AppendOptional(StringBuilder sb, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine(value);
        }
    }
}
