using System;
using System.Collections.Generic;
using System.Text;

namespace ZhuaQianDesktopApp.Documents
{
    /// <summary>
    /// 办公工作流模板库（Epic F1）。生成可直接交给 OfficeExporter 渲染的文本骨架，
    /// 不依赖任何 UI，也不触碰现有文件。支持：销售演示 / 会议纪要 / 报告 / 数据表 / 海报。
    /// </summary>
    public enum OfficeTemplateKind
    {
        SalesPitch,
        MeetingMinutes,
        Report,
        DataTable,
        Poster
    }

    /// <summary>
    /// 模板渲染所需的上下文参数。所有字段均有合理默认值，缺省也能产出可用文档。
    /// </summary>
    public sealed class TemplateContext
    {
        public string Title { get; set; } = "未命名文档";
        public string Subtitle { get; set; } = "";
        public string Author { get; set; } = "";
        public string Date { get; set; } = "";
        public string Closing { get; set; } = "";

        /// <summary>按小节标题索引的要点列表，例如 Bullets["痛点"]。</summary>
        public Dictionary<string, List<string>> Bullets { get; set; } = new Dictionary<string, List<string>>();

        public List<TableColumn> Columns { get; set; } = new List<TableColumn>();
        public List<List<string>> Rows { get; set; } = new List<List<string>>();

        public TemplateContext() { }
    }

    public sealed class TableColumn
    {
        public string Name { get; set; }
        public TableColumn(string name) { Name = name ?? ""; }
    }

    /// <summary>渲染结果：文本骨架 + 推荐导出扩展名。</summary>
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
        /// <summary>渲染指定模板，返回可直接交给 OfficeExporter 的文本与推荐扩展名。</summary>
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
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>模板对应的推荐导出扩展名。</summary>
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

        /// <summary>按名称渲染（用于命令映射），未知名称回退到 Report。</summary>
        public static TemplateResult RenderByName(string name, TemplateContext ctx)
        {
            switch ((name ?? "").ToLowerInvariant().Trim())
            {
                case "salespitch":
                case "pitch":
                case "销售演示":
                    return Render(OfficeTemplateKind.SalesPitch, ctx);
                case "meetingminutes":
                case "minutes":
                case "会议纪要":
                    return Render(OfficeTemplateKind.MeetingMinutes, ctx);
                case "report":
                case "报告":
                    return Render(OfficeTemplateKind.Report, ctx);
                case "datatable":
                case "table":
                case "数据表":
                    return Render(OfficeTemplateKind.DataTable, ctx);
                case "poster":
                case "海报":
                    return Render(OfficeTemplateKind.Poster, ctx);
                default:
                    return Render(OfficeTemplateKind.Report, ctx);
            }
        }

        /// <summary>所有可用模板的展示名。</summary>
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

        // ---- 渲染器 ----

        public static string RenderSalesPitch(TemplateContext ctx)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(ctx.Title).Append("\n");
            if (!string.IsNullOrEmpty(ctx.Subtitle)) sb.Append(ctx.Subtitle).Append("\n");
            if (!string.IsNullOrEmpty(ctx.Closing)) sb.Append(ctx.Closing).Append("\n");
            sb.Append("\n");

            AppendSlide(sb, "痛点 / The Problem",
                BulletsOr(ctx, "痛点", new[] { "手动流程耗时费力", "信息分散难追踪", "成本高且容易出错" }));
            AppendSlide(sb, "方案 / Our Solution",
                BulletsOr(ctx, "方案", new[] { "一站式自动化处理", "可视化进度与审计", "安全合规可回溯" }));
            AppendSlide(sb, "价值 / Why Us",
                BulletsOr(ctx, "价值", new[] { "效率提升 10 倍", "零额外人力投入", "开箱即用" }));
            AppendSlide(sb, "行动 / Get Started",
                BulletsOr(ctx, "行动", new[] { "预约一次演示", "免费试用 14 天" }));
            return sb.ToString();
        }

        public static string RenderMeetingMinutes(TemplateContext ctx)
        {
            var sb = new StringBuilder();
            sb.Append("# 会议纪要：").Append(ctx.Title).Append("\n");
            sb.Append("日期：").Append(string.IsNullOrEmpty(ctx.Date) ? "待填" : ctx.Date)
              .Append("    记录人：").Append(string.IsNullOrEmpty(ctx.Author) ? "待填" : ctx.Author).Append("\n\n");

            sb.Append("## 参会与议程\n");
            foreach (var b in BulletsOr(ctx, "议程", new[] { "确认本次会议目标", "同步上周进展", "讨论待决事项" }))
                sb.Append("- ").Append(b).Append("\n");
            sb.Append("\n");

            sb.Append("## 讨论要点\n");
            foreach (var b in BulletsOr(ctx, "讨论", new[] { "关键风险与阻塞点", "资源与排期评估", "方案取舍说明" }))
                sb.Append("- ").Append(b).Append("\n");
            sb.Append("\n");

            sb.Append("## 决议事项\n");
            foreach (var b in BulletsOr(ctx, "决议", new[] { "明确负责人与截止时间", "锁定下一里程碑" }))
                sb.Append("- ").Append(b).Append("\n");
            sb.Append("\n");

            sb.Append("## 待办跟进\n");
            foreach (var b in BulletsOr(ctx, "待办", new[] { "负责人整理行动项", "下次会议复盘" }))
                sb.Append("- ").Append(b).Append("\n");
            if (!string.IsNullOrEmpty(ctx.Closing)) sb.Append("\n").Append(ctx.Closing).Append("\n");
            return sb.ToString();
        }

        public static string RenderReport(TemplateContext ctx)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(ctx.Title).Append("\n");
            if (!string.IsNullOrEmpty(ctx.Subtitle)) sb.Append(ctx.Subtitle).Append("\n");
            sb.Append("作者：").Append(string.IsNullOrEmpty(ctx.Author) ? "待填" : ctx.Author)
              .Append("    日期：").Append(string.IsNullOrEmpty(ctx.Date) ? "待填" : ctx.Date).Append("\n\n");

            sb.Append("## 摘要\n");
            foreach (var b in BulletsOr(ctx, "摘要", new[] { "一句话概述结论与价值" }))
                sb.Append("- ").Append(b).Append("\n");
            sb.Append("\n");

            sb.Append("## 1. 背景\n");
            foreach (var b in BulletsOr(ctx, "背景", new[] { "问题来源与现状", "涉及的系统与干系人" }))
                sb.Append("- ").Append(b).Append("\n");
            sb.Append("\n");

            sb.Append("## 2. 分析\n");
            foreach (var b in BulletsOr(ctx, "分析", new[] { "数据与方法", "关键发现" }))
                sb.Append("- ").Append(b).Append("\n");
            sb.Append("\n");

            sb.Append("## 3. 结论\n");
            foreach (var b in BulletsOr(ctx, "结论", new[] { "核心结论", "建议与下一步" }))
                sb.Append("- ").Append(b).Append("\n");
            if (!string.IsNullOrEmpty(ctx.Closing)) sb.Append("\n").Append(ctx.Closing).Append("\n");
            return sb.ToString();
        }

        public static string RenderDataTable(TemplateContext ctx)
        {
            var sb = new StringBuilder();
            var cols = ctx.Columns;
            if (cols == null || cols.Count == 0)
                cols = new List<TableColumn> { new TableColumn("项目"), new TableColumn("数值"), new TableColumn("备注") };

            sb.Append("| ");
            foreach (var c in cols) sb.Append(c.Name).Append(" | ");
            sb.Append("\n| ");
            for (int i = 0; i < cols.Count; i++) sb.Append("--- | ");
            sb.Append("\n");

            var rows = ctx.Rows;
            if (rows == null || rows.Count == 0)
            {
                rows = new List<List<string>>
                {
                    new List<string> { "示例 A", "100", "占位" },
                    new List<string> { "示例 B", "200", "占位" }
                };
            }
            foreach (var row in rows)
            {
                sb.Append("| ");
                for (int i = 0; i < cols.Count; i++)
                {
                    string cell = (i < row.Count) ? row[i] : "";
                    sb.Append(cell).Append(" | ");
                }
                sb.Append("\n");
            }
            return sb.ToString();
        }

        public static string RenderPoster(TemplateContext ctx)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(ctx.Title).Append("\n");
            if (!string.IsNullOrEmpty(ctx.Subtitle)) sb.Append(ctx.Subtitle).Append("\n");
            if (!string.IsNullOrEmpty(ctx.Closing)) sb.Append("\n").Append(ctx.Closing).Append("\n");
            return sb.ToString();
        }

        // ---- 内部辅助 ----

        static void AppendSlide(StringBuilder sb, string title, IEnumerable<string> bullets)
        {
            sb.Append("# ").Append(title).Append("\n");
            foreach (var b in bullets) sb.Append("- ").Append(b).Append("\n");
            sb.Append("\n");
        }

        static IList<string> BulletsOr(TemplateContext ctx, string key, string[] defaults)
        {
            if (ctx.Bullets != null && ctx.Bullets.TryGetValue(key, out var list) && list != null && list.Count > 0)
                return list;
            return new List<string>(defaults);
        }
    }
}
