using System;
using System.IO;
using System.Collections.Generic;
using System.IO.Compression;

using ZhuaQianDesktopApp.Documents;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Tools;

class TestOfficeTemplateExecutor
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
        Console.WriteLine("[OfficeTemplateExecutor]");
        TestRegistersInFactory();
        TestReportFromDialogText();
        TestFallbackRendersWhenTextMissing();
        TestKindParsingEndToEnd();
        return failures;
    }

    static AgentPipeline BuildPipeline()
    {
        string dir = Path.Combine(Path.GetTempPath(), "zq_offexec_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        var gate = new PermissionGate();
        gate.Set("permFileWrite", PermissionLevel.Allow);
        var factory = new AgentPipelineFactory(Path.Combine(dir, "audit.log"), dir, new OutputsHub(dir), new OfficeExporter(), new WebSearchClient());
        return factory.Create(gate, dir, false);
    }

    static void TestRegistersInFactory()
    {
        var pipeline = BuildPipeline();
        Assert(pipeline.HasExecutor("OfficeTemplate"), "factory registers OfficeTemplate executor");
    }

    static void TestReportFromDialogText()
    {
        string dir = Path.Combine(Path.GetTempPath(), "zq_offexec_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            var executor = new OfficeTemplateExecutor(new OfficeExporter());
            var args = new Dictionary<string, object>();
            args["kind"] = "Report";
            args["format"] = "docx";
            args["text"] = "# 季度报告\n- 结论：稳健增长";
            string path = Path.Combine(dir, "report.docx");
            var result = executor.Execute(new AgentCommand("OfficeTemplate", "permFileWrite", "task1", path, "Generate report", args));
            Assert(result.Status == CommandStatus.Success, "executor succeeds with provided text");
            Assert(File.Exists(path) && ValidZip(path), "report docx written and valid zip");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static void TestFallbackRendersWhenTextMissing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "zq_offexec_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            var executor = new OfficeTemplateExecutor(new OfficeExporter());
            var args = new Dictionary<string, object>();
            args["kind"] = "SalesPitch";
            // no text -> must render via OfficeTemplateLibrary fallback using structured fields
            args["title"] = "ZQ 路演";
            string path = Path.Combine(dir, "pitch.pptx");
            var result = executor.Execute(new AgentCommand("OfficeTemplate", "permFileWrite", "task1", path, "Generate pitch", args));
            Assert(result.Status == CommandStatus.Success, "fallback render succeeds");
            Assert(File.Exists(path) && ValidZip(path), "pitch pptx written and valid zip");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static void TestKindParsingEndToEnd()
    {
        string dir = Path.Combine(Path.GetTempPath(), "zq_offexec_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            var pipeline = BuildPipeline();
            var cases = new Dictionary<string, string> {
                { "MeetingMinutes", "docx" },
                { "DataTable", "xlsx" },
                { "Poster", "png" },
                { "Report", "docx" },
                { "SalesPitch", "pptx" }
            };
            foreach (var kv in cases)
            {
                var args = new Dictionary<string, object>();
                args["kind"] = kv.Key;
                args["text"] = "sample content";
                string path = Path.Combine(dir, "out_" + kv.Key + "." + kv.Value);
                var res = pipeline.Run(new AgentCommand("OfficeTemplate", "permFileWrite", "task1", path, "gen", args));
                Assert(res.Status == CommandStatus.Success && File.Exists(path), "pipeline generates " + kv.Key);
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static bool ValidZip(string path)
    {
        try { using (ZipFile.OpenRead(path)) return true; }
        catch { return false; }
    }
}
