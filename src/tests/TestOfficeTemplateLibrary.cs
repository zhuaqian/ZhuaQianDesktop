using System;
using System.IO;
using System.Text;
using System.IO.Compression;

using ZhuaQianDesktopApp.Documents;

class TestOfficeTemplateLibrary
{
    static int failures = 0;
    static int passed = 0;

    static void Assert(bool cond, string msg)
    {
        if (cond) passed++;
        else { failures++; Console.WriteLine("  FAIL: " + msg); }
    }

    public static int RunAll()
    {
        Console.WriteLine("[OfficeTemplateLibrary]");
        TestSalesPitch();
        TestMeetingMinutes();
        TestReport();
        TestDataTable();
        TestDataTableDefaults();
        TestPoster();
        TestRoundTripWithExporter();
        TestRenderByNameFallback();
        return failures;
    }

    static void TestSalesPitch()
    {
        var r = OfficeTemplateLibrary.Render(OfficeTemplateKind.SalesPitch, new TemplateContext { Title = "ZQ 演示" });
        Assert(r.SuggestedExtension == "pptx", "sales pitch -> pptx");
        Assert(r.Text.Contains("# ZQ 演示"), "title present");
        Assert(r.Text.Contains("# 痛点 / The Problem"), "problem slide present");
        Assert(r.Text.Contains("# 行动 / Get Started"), "cta slide present");
        Assert(r.Text.Contains("- "), "has bullets");
    }

    static void TestMeetingMinutes()
    {
        var ctx = new TemplateContext { Title = "周会", Date = "2026-07-17", Author = "老李" };
        ctx.Bullets["决议"] = new System.Collections.Generic.List<string> { "锁定 v1 范围" };
        var r = OfficeTemplateLibrary.Render(OfficeTemplateKind.MeetingMinutes, ctx);
        Assert(r.SuggestedExtension == "docx", "minutes -> docx");
        Assert(r.Text.Contains("日期：2026-07-17"), "date rendered");
        Assert(r.Text.Contains("记录人：老李"), "author rendered");
        Assert(r.Text.Contains("锁定 v1 范围"), "custom bullets honored");
    }

    static void TestReport()
    {
        var r = OfficeTemplateLibrary.Render(OfficeTemplateKind.Report, new TemplateContext { Title = "季度报告" });
        Assert(r.SuggestedExtension == "docx", "report -> docx");
        Assert(r.Text.Contains("## 摘要"), "abstract section");
        Assert(r.Text.Contains("## 1. 背景"), "background section");
        Assert(r.Text.Contains("## 3. 结论"), "conclusion section");
    }

    static void TestDataTable()
    {
        var ctx = new TemplateContext();
        ctx.Columns = new System.Collections.Generic.List<TableColumn> { new TableColumn("名称"), new TableColumn("数量") };
        ctx.Rows = new System.Collections.Generic.List<System.Collections.Generic.List<string>>
        {
            new System.Collections.Generic.List<string> { "A", "10" },
            new System.Collections.Generic.List<string> { "B", "20" }
        };
        var r = OfficeTemplateLibrary.Render(OfficeTemplateKind.DataTable, ctx);
        Assert(r.SuggestedExtension == "xlsx", "table -> xlsx");
        Assert(r.Text.Contains("| 名称 | 数量 |"), "header row");
        Assert(r.Text.Contains("| --- | --- |"), "separator row");
        Assert(r.Text.Contains("| A | 10 |"), "data row");
    }

    static void TestDataTableDefaults()
    {
        var r = OfficeTemplateLibrary.Render(OfficeTemplateKind.DataTable, new TemplateContext());
        Assert(r.Text.Contains("| 项目 | 数值 | 备注 |"), "default columns");
    }

    static void TestPoster()
    {
        var ctx = new TemplateContext { Title = "大促", Subtitle = "全场五折", Closing = "仅限今日" };
        var r = OfficeTemplateLibrary.Render(OfficeTemplateKind.Poster, ctx);
        Assert(r.SuggestedExtension == "png", "poster -> png");
        Assert(r.Text.Contains("# 大促"), "title");
        Assert(r.Text.Contains("全场五折"), "subtitle");
        Assert(r.Text.Contains("仅限今日"), "closing");
    }

    static void TestRoundTripWithExporter()
    {
        var exporter = new OfficeExporter();
        string dir = Path.Combine(Path.GetTempPath(), "zq_tmpl_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            string pptx = Path.Combine(dir, "p.pptx");
            string docx = Path.Combine(dir, "d.docx");
            string xlsx = Path.Combine(dir, "x.xlsx");
            string png = Path.Combine(dir, "p.png");

            exporter.SavePptx(pptx, OfficeTemplateLibrary.Render(OfficeTemplateKind.SalesPitch, new TemplateContext()).Text);
            exporter.SaveDocx(docx, OfficeTemplateLibrary.Render(OfficeTemplateKind.Report, new TemplateContext()).Text);
            exporter.SaveXlsx(xlsx, OfficeTemplateLibrary.Render(OfficeTemplateKind.DataTable, new TemplateContext()).Text);
            exporter.SavePng(png, OfficeTemplateLibrary.Render(OfficeTemplateKind.Poster, new TemplateContext()).Text);

            Assert(File.Exists(pptx) && ValidZip(pptx), "pitch renders to valid pptx");
            Assert(File.Exists(docx) && ValidZip(docx), "report renders to valid docx");
            Assert(File.Exists(xlsx) && ValidZip(xlsx), "table renders to valid xlsx");
            Assert(File.Exists(png) && new FileInfo(png).Length > 0, "poster renders to non-empty png");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    static void TestRenderByNameFallback()
    {
        var known = OfficeTemplateLibrary.RenderByName("pitch", new TemplateContext());
        Assert(known.SuggestedExtension == "pptx", "render by name pitch");
        var unknown = OfficeTemplateLibrary.RenderByName("???", new TemplateContext());
        Assert(unknown.SuggestedExtension == "docx", "unknown name falls back to report/docx");
        Assert(OfficeTemplateLibrary.ListKinds().Count == 5, "lists 5 templates");
    }

    static bool ValidZip(string path)
    {
        try { using (ZipFile.OpenRead(path)) return true; }
        catch { return false; }
    }
}
