using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Documents;
using ZhuaQianDesktopApp.Knowledge;
using ZhuaQianDesktopApp.Tools;
using ZhuaQianDesktopApp;
using ZhuaQianDesktopApp.Plugins;
using ZhuaQianDesktopApp.Agent.Hooks;
using System.Threading.Tasks;

class TestRunner
{
    static int failures = 0;
    static int passed = 0;

    static void Assert(bool cond, string msg)
    {
        if (cond) { passed++; }
        else { failures++; Console.WriteLine("  FAIL: " + msg); }
    }

    static int Main()
    {
        Console.WriteLine("ZhuaQian Desktop module tests");
        Console.WriteLine("================================");

        TestRedactor();
        TestChunker();
        TestOfficeExporter();
        TestPermissionGate();
        TestConfigStore();
        TestOutputsHub();
        TestFolderOrganizer();
        TestPluginRunner();
        TestAgentPlanParser();
        TestAgentPlanCommandMapper();
        TestAgentPipeline();
        TestAgentPipelineAsync();
        TestStreamingBridge();
        TestWebSearchClient();
        TestProcessSnapshotCollector();
        TestSystemDiagnostics();
        TestSmoke();
        TestPluginManifest();
        TestHookRegistry();
        failures += TestWorkspaceScanSummary.RunAll();
        failures += TestCommandRunRecorder.RunAll();
        failures += TestCodingAgentSession.RunAll();

        Console.WriteLine("================================");
        Console.WriteLine("Passed: " + passed + "  Failed: " + failures);
        return failures == 0 ? 0 : 1;
    }

    static void TestRedactor()
    {
        Console.WriteLine("[Redactor]");
        var r = new Redactor(true);
        Assert(r.Apply("call 13800138000 please").Contains("[REDACTED_PHONE]"), "phone redacted");
        Assert(r.Apply("id 11010119900307123X").Contains("[REDACTED_CN_ID]"), "CN id redacted");
        Assert(r.Apply("card 4111 1111 1111 1111").Contains("[REDACTED_CARD]"), "card redacted");
        Assert(r.Apply("mail test@example.com").Contains("[REDACTED_EMAIL]"), "email redacted");
        var off = new Redactor(false);
        Assert(!off.Apply("call 13800138000").Contains("REDACTED"), "disabled keeps text");

        // edge cases
        Assert(r.Apply("") == "", "empty input unchanged");
        Assert(r.Apply("no sensitive data here").Contains("no sensitive"), "benign text kept");
        Assert(r.Apply("call 13800138000 and card 4111 1111 1111 1111").Contains("[REDACTED_PHONE]")
            && r.Apply("call 13800138000 and card 4111 1111 1111 1111").Contains("[REDACTED_CARD]"), "multiple redactions");
        Assert(!r.Apply("call 13800138000").Contains("13800138000"), "phone digits removed");
    }

    static void TestChunker()
    {
        Console.WriteLine("[Chunker]");
        var c = new Chunker();
        var chunks = c.Split("line one\nline two\nline three", 10);
        Assert(chunks.Count >= 1, "split returns chunks");
        Assert(c.DetectHeading("# Title here", "fb") == "Title here", "detect heading strips #");
        Assert(c.InferTags("invoice payment", "报销 发票").Contains("finance"), "finance tag inferred");
        Assert(c.StableDocId("C:\\a.txt") == c.StableDocId("C:\\a.txt"), "docId stable");
        Assert(c.StableDocId("C:\\a.txt") != c.StableDocId("C:\\b.txt"), "docId differs");

        // edge cases
        Assert(c.Split("", 100) != null, "empty text handled");
        Assert(c.DetectHeading("no heading here", "fb") == "no heading here", "no heading unchanged");
        Assert(c.DetectHeading("## Sub", "fb") == "Sub", "## heading stripped");
        Assert(c.InferTags("", "") != null, "empty tags handled");
        var big = c.Split("word word word word word word word word word word word", 5);
        Assert(big.Count >= 1, "long text still chunks");
    }

    static void TestOfficeExporter()
    {
        Console.WriteLine("[OfficeExporter]");
        var x = new OfficeExporter();
        string dir = Path.Combine(Path.GetTempPath(), "zq_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            string docx = Path.Combine(dir, "t.docx");
            string pptx = Path.Combine(dir, "t.pptx");
            string xlsx = Path.Combine(dir, "t.xlsx");
            x.SaveDocx(docx, "Hello\nWorld");
            x.SavePptx(pptx, "# Slide A\nbullet 1\nbullet 2");
            x.SaveXlsx(xlsx, "a|b\n1|2\n3|4");
            Assert(File.Exists(docx) && ValidZip(docx), "docx is valid zip");
            Assert(File.Exists(pptx) && ValidZip(pptx), "pptx is valid zip");
            Assert(File.Exists(xlsx) && ValidZip(xlsx), "xlsx is valid zip");
            using (var z = ZipFile.OpenRead(xlsx))
                Assert(z.GetEntry("xl/worksheets/sheet1.xml") != null, "xlsx has sheet");
            string quotedXlsx = Path.Combine(dir, "quoted.xlsx");
            x.SaveXlsx(quotedXlsx, "\"ACME, Inc.\",100\n\"He said \"\"Hi\"\"\",200");
            using (var z = ZipFile.OpenRead(quotedXlsx))
            {
                var entry = z.GetEntry("xl/worksheets/sheet1.xml");
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                {
                    string xml = reader.ReadToEnd();
                    Assert(xml.Contains("ACME, Inc.") && xml.Contains("He said &quot;Hi&quot;"), "quoted CSV fields preserved in xlsx");
                }
            }

            // edge cases
            string emptyDocx = Path.Combine(dir, "e.docx");
            x.SaveDocx(emptyDocx, "");
            Assert(File.Exists(emptyDocx) && ValidZip(emptyDocx), "empty content still valid docx");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static bool ValidZip(string path)
    {
        try { using (var z = ZipFile.OpenRead(path)) return z.Entries.Count > 0; }
        catch { return false; }
    }

    static void TestPermissionGate()
    {
        Console.WriteLine("[PermissionGate]");
        var gate = new PermissionGate();
        gate.Set("permFileWrite", PermissionLevel.Allow);
        gate.Set("permProcessManage", PermissionLevel.Deny);
        Assert(gate.Get("permFileWrite") == PermissionLevel.Allow, "fileWrite allowed");
        Assert(gate.Get("permProcessManage") == PermissionLevel.Deny, "processManage denied");
        Assert(gate.Get("permUnknown") == PermissionLevel.Ask, "unknown defaults to ask");

        gate.SetPattern("permPluginRun", "dangerous*", PermissionLevel.Deny);
        gate.Set("permPluginRun", PermissionLevel.Allow);
        Assert(gate.Check("permPluginRun", "dangerousThing") == PermissionDecision.Deny, "pattern denies dangerous");
        Assert(gate.Check("permPluginRun", "safe.py") == PermissionDecision.Allow, "pattern allows safe");

        string data = gate.ToJson();
        var round = PermissionGate.FromJson(data);
        Assert(round.Get("permProcessManage") == PermissionLevel.Deny, "deny survives round-trip");
        Assert(round.Check("permPluginRun", "dangerousThing") == PermissionDecision.Deny, "restored pattern matches");

        // edge cases
        gate.SetPattern("permFileWrite", "*.exe", PermissionLevel.Deny);
        Assert(gate.Check("permFileWrite", "C:\\a.EXE") == PermissionDecision.Deny, "glob is case-insensitive");
        Assert(gate.Check("permFileWrite", "C:\\a.TXT") == PermissionDecision.Allow, "non-matching glob falls through to general level");
        gate.SetPattern("permFileWrite", "*", PermissionLevel.Allow);
        Assert(gate.Check("permFileWrite", "anything.xyz") == PermissionDecision.Allow, "wildcard glob matches");
        var empty = PermissionGate.FromJson("");
        Assert(empty.Get("permX") == PermissionLevel.Ask, "empty json yields default gate");

        string baseDir = Path.Combine(Path.GetTempPath(), "zq_perm_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string allowedDir = Path.Combine(baseDir, "safe");
        string siblingDir = Path.Combine(baseDir, "safe-backup");
        Directory.CreateDirectory(allowedDir);
        Directory.CreateDirectory(siblingDir);
        try
        {
            var scoped = new PermissionGate();
            scoped.Set("permFileWrite", PermissionLevel.Allow);
            scoped.AllowedDirectories.Add(allowedDir);
            Assert(scoped.Check("permFileWrite", Path.Combine(allowedDir, "a.txt")) == PermissionDecision.Allow, "allowed directory file stays allow");
            Assert(scoped.Check("permFileWrite", Path.Combine(siblingDir, "a.txt")) == PermissionDecision.Ask, "sibling prefix directory escalates to ask");
        }
        finally { try { Directory.Delete(baseDir, true); } catch { } }
    }

    static void TestConfigStore()
    {
        Console.WriteLine("[ConfigStore]");
        string dir = Path.Combine(Path.GetTempPath(), "zq_cfg_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        var a = new ConfigStore(dir);
        a.Set("GeminiApiKey", "secret-key");
        a.Set("Model", "gemini-flash-lite-latest");
        a.Set("Provider", "OpenRouter");
        a.Set("permProcessManage", "Deny");
        a.Save();
        Assert(File.Exists(Path.Combine(dir, "config.json")), "config written");

        var b = new ConfigStore(dir);
        b.Load();
        Assert(b.Get<string>("GeminiApiKey", "") == "secret-key", "api key round-trips");
        Assert(b.Get<string>("Model", "") == "gemini-flash-lite-latest", "model round-trips");
        Assert(b.Get<string>("Provider", "") == "OpenRouter", "provider round-trips");
        Assert(b.Get<string>("permProcessManage", "") == "Deny", "permission round-trips");

        // edge cases
        Assert(b.Get<string>("missing-key", "fallback") == "fallback", "missing key uses fallback");
        a.Set("Model", "other");
        a.Save();
        var c = new ConfigStore(dir);
        c.Load();
        Assert(c.Get<string>("Model", "") == "other", "overwrite persists");
        try { Directory.Delete(dir, true); } catch { }
    }

    static void TestOutputsHub()
    {
        Console.WriteLine("[OutputsHub]");
        string dir = Path.Combine(Path.GetTempPath(), "zq_out_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        var hub = new OutputsHub(dir);

        string file = Path.Combine(dir, "report.docx");
        File.WriteAllText(file, "content");
        long size = new FileInfo(file).Length;
        hub.RecordOutput("export", "docx", file, "task1", "Report", "", (int)size);
        var rows = hub.LoadOutputRows(50);
        Assert(rows.Count == 1, "one output recorded");
        Assert(Str(rows[0], "type") == "docx", "type stored");
        Assert(rows[0].ContainsKey("exists") && Convert.ToBoolean(rows[0]["exists"]), "exists computed");

        hub.RecordExportHistory("pptx", file, (int)size, "task2", "Old Task");
        var merged = hub.LoadOutputRows(50);
        Assert(merged.Count == 2, "export history merged");

        // rename / delete / add-to-knowledge
        string file2 = Path.Combine(dir, "note.txt");
        File.WriteAllText(file2, "x");
        hub.RecordOutput("export", "txt", file2, "taskX", "TitleX", "", 1);
        var all = hub.LoadOutputRows(50);
        string oid = Str(all[0], "outputId");
        Assert(hub.Rename(oid, "Renamed") == true, "rename returns true");
        bool renameOk = false;
        foreach (var r in hub.LoadOutputRows(50))
            if (IdMatch(r, oid) && Str(r, "displayName") == "Renamed" && Str(r, "path") == file2) renameOk = true;
        Assert(renameOk, "rename persisted");

        Assert(hub.AddToKnowledge(oid) == true, "add to knowledge returns true");
        bool knowOk = false;
        foreach (var r in hub.LoadOutputRows(50))
            if (IdMatch(r, oid) && r.ContainsKey("addedToKnowledge") && Convert.ToBoolean(r["addedToKnowledge"])) knowOk = true;
        Assert(knowOk, "addedToKnowledge persisted");

        int before = hub.LoadOutputRows(50).Count;
        Assert(hub.Delete(oid) == true, "delete returns true");
        int after = hub.LoadOutputRows(50).Count;
        Assert(after == before - 1, "row deleted");

        // edge cases
        string missing = Path.Combine(dir, "nope.docx");
        hub.RecordOutput("export", "docx", missing, "t", "T", "", 0);
        bool missingFlag = false;
        foreach (var r in hub.LoadOutputRows(50))
            if (Str(r, "path") == missing && r.ContainsKey("exists") && !Convert.ToBoolean(r["exists"])) missingFlag = true;
        Assert(missingFlag, "missing file flagged exists=false");
        Assert(hub.LoadOutputRows(1).Count <= 1, "row cap respected");

        string manyDir = Path.Combine(dir, "many");
        Directory.CreateDirectory(manyDir);
        for (int i = 0; i < 120; i++)
        {
            string p = Path.Combine(manyDir, "f" + i.ToString("000") + ".txt");
            File.WriteAllText(p, "x");
            hub.RecordOutput("bulk", "txt", p, "task-many", "Many " + i.ToString(), "", 1);
        }
        var manyRows = hub.LoadOutputRows(200);
        string deleteId = "";
        foreach (var r in manyRows)
            if (Str(r, "path").EndsWith("f050.txt")) { deleteId = Str(r, "outputId"); break; }
        Assert(deleteId.Length > 0 && hub.Delete(deleteId), "delete one row from more than 100 outputs");
        Assert(hub.LoadOutputRows(200).Count >= 119, "delete does not truncate output history to dialog page size");

        string legacyFile = Path.Combine(dir, "legacy.md");
        File.WriteAllText(legacyFile, "legacy");
        hub.RecordExportHistory("md", legacyFile, 6, "legacy-task", "Legacy");
        var withLegacy = hub.LoadOutputRows(200);
        bool sawLegacy = false;
        foreach (var r in withLegacy)
            if (Str(r, "path") == legacyFile && Str(r, "recordSource") == "legacy-export-history") sawLegacy = true;
        Assert(sawLegacy, "legacy output marked with record source");
        hub.RemoveLegacyExportEntry(legacyFile);
        bool legacyGone = true;
        foreach (var r in hub.LoadOutputRows(200))
            if (Str(r, "path") == legacyFile && Str(r, "recordSource") == "legacy-export-history") legacyGone = false;
        Assert(legacyGone, "legacy output removal is permanent");

        try { Directory.Delete(dir, true); } catch { }
    }

    static bool IdMatch(Dictionary<string, object> r, string id)
    {
        return r != null && string.Equals(Str(r, "outputId"), id, StringComparison.OrdinalIgnoreCase);
    }

    static void TestFolderOrganizer()
    {
        Console.WriteLine("[FolderOrganizer]");
        string dir = Path.Combine(Path.GetTempPath(), "zq_org_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "x");
        File.WriteAllText(Path.Combine(dir, "b.png"), "x");
        File.WriteAllText(Path.Combine(dir, "c.cs"), "x");
        try
        {
            var org = new FolderOrganizer(dir);
            var plan = org.BuildPlan(dir);
            var result = org.Execute(dir, plan);
            Assert(result != null && result.Moved >= 3, "organize moved files");
            Assert(File.Exists(result.ManifestPath), "manifest written");
            string organizedRoot = Path.Combine(dir, "_ZhuaQian_Organized");
            int organizedFiles = Directory.GetFiles(organizedRoot, "*", SearchOption.AllDirectories).Length;
            Assert(organizedFiles == 3, "3 files organized under _ZhuaQian_Organized");

            int restored = org.Rollback(result.ManifestPath);
            Assert(restored == 3, "rollback restored 3 files");
            Assert(File.Exists(Path.Combine(dir, "a.txt")), "file back in place");

            // edge cases
            Assert(org.Rollback("C:\\does-not-exist.json") == 0, "rollback missing manifest is safe");
            var emptyPlan = org.BuildPlan(Path.Combine(dir, "a.txt"));
            Assert(emptyPlan != null, "build plan on file path does not crash");
            var emptyResult1 = org.Execute(dir, new List<KeyValuePair<string, string>>());
            var emptyResult2 = org.Execute(dir, new List<KeyValuePair<string, string>>());
            Assert(emptyResult1.ManifestPath != emptyResult2.ManifestPath, "rapid manifests use unique names");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static void TestPluginRunner()
    {
        Console.WriteLine("[PluginRunner]");
        string dir = Path.Combine(Path.GetTempPath(), "zq_plug_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            string py = Path.Combine(dir, "x.py");
            string ps1 = Path.Combine(dir, "x.ps1");
            string exe = Path.Combine(dir, "x.exe");
            string bat = Path.Combine(dir, "x.bat");
            string xyz = Path.Combine(dir, "x.xyz");
            foreach (var f in new[] { py, ps1, exe, bat, xyz })
                File.WriteAllText(f, "");
            var runner = new PluginRunner(dir);
            Assert(runner.Validate(py) == "", "py allowed by default");
            Assert(runner.Validate(ps1) == "", "ps1 allowed by default");
            Assert(runner.Validate(exe) != "", "exe blocked by default");
            Assert(runner.Validate(bat) != "", "bat blocked by default");
            Assert(runner.Validate(xyz) != "", "unknown ext blocked");

            runner.AllowAdvancedPlugins = true;
            Assert(runner.Validate(exe) == "", "exe allowed when advanced on");
            runner.AllowAdvancedPlugins = false;
            Assert(runner.Validate(exe) != "", "exe blocked again when advanced off");

            // edge cases
            Assert(runner.Validate("") != "", "empty path rejected");
            Assert(runner.Validate(Path.Combine(dir, "x.EXE")) != "", "extension check is case-insensitive");
            string outside = Path.Combine(Path.GetTempPath(), "zq_outside_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".ps1");
            File.WriteAllText(outside, "");
            Assert(runner.Validate(outside) != "", "plugin outside trusted folder rejected");
            try { File.Delete(outside); } catch { }

            string slow = Path.Combine(dir, "slow.ps1");
            File.WriteAllText(slow, "Start-Sleep -Seconds 2; Write-Output 'late'", Encoding.UTF8);
            var timeout = runner.Run(slow, "", 100, null);
            Assert(timeout.TimedOut, "plugin timeout is reported as timeout");
            Assert(string.IsNullOrEmpty(timeout.Error), "plugin timeout does not become generic exception");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static void TestAgentPlanParser()
    {
        Console.WriteLine("[AgentPlanParser]");
        var parser = new AgentPlanParser();
        string text = "Goal: prepare a launch report\n" +
            "1. Search latest AI office assistant news\n" +
            "2. Generate a PPTX file to desktop\n" +
            "3. Run plugin \"C:\\tools\\summarize.ps1\"\n" +
            "4. End process pid 214748000\n" +
            "Risk: cloud upload and local file write need approval";
        var plan = parser.Parse(text);
        Assert(plan.Goal == "prepare a launch report", "goal parsed");
        Assert(plan.Steps.Count == 4, "four steps parsed");
        Assert(plan.Steps[0].CommandType == "WebSearch", "web search step classified");
        Assert(plan.Steps[1].CommandType == "ExportFile", "export step classified");
        Assert(plan.Steps[2].CommandType == "RunPlugin", "plugin step classified");
        Assert(plan.Steps[2].Target == "C:\\tools\\summarize.ps1", "quoted target parsed");
        Assert(plan.Steps[3].Permission == "permProcessManage", "process permission classified");
        Assert(plan.NeedsApproval, "approval required for risky plan");
        Assert(plan.ToReviewMarkdown().Contains("Plan Review"), "review markdown produced");

        string structuredText = "Goal: create a folder report\n" +
            "stepId: S1\n" +
            "title: Generate Excel risk register\n" +
            "actionType: ExportFile\n" +
            "target: C:\\reports\\risk.xlsx\n" +
            "riskLevel: medium\n" +
            "requiredPermission: permFileWrite\n" +
            "expectedOutput: Excel report\n" +
            "rollbackPossible: false\n" +
            "status: pending\n" +
            "stepId: S2\n" +
            "title: Organize source folder\n" +
            "actionType: OrganizeFolder\n" +
            "target: C:\\reports\\source\n" +
            "requiredPermission: permFileMoveDelete\n" +
            "rollbackPossible: true";
        var structured = parser.Parse(structuredText);
        Assert(structured.Steps.Count == 2, "structured schema steps parsed");
        Assert(structured.Steps[0].StepId == "S1", "structured step id parsed");
        Assert(structured.Steps[0].ExpectedOutput == "Excel report", "structured expected output parsed");
        Assert(structured.Steps[1].RollbackPossible, "structured rollback flag parsed");
        Assert(structured.Steps[1].Permission == "permFileMoveDelete", "structured permission parsed");
        Assert(structured.ToReviewMarkdown().Contains("Expected output: Excel report"), "structured review includes expected output");

        var simple = parser.Parse("Generate a TXT file");
        Assert(simple.Steps.Count == 1, "fallback single step");
        Assert(simple.Steps[0].CommandType == "ExportFile", "fallback classified");

        var open = parser.Parse("Open \"notepad.exe\"");
        Assert(open.Steps.Count == 1, "open step parsed");
        Assert(open.Steps[0].CommandType == "ComputerControl", "open step maps to registered computer control executor");

        string contractDir = Path.Combine(Path.GetTempPath(), "zq_contract_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(contractDir);
        var gate = new PermissionGate();
        var pipelineFactory = new AgentPipelineFactory(Path.Combine(contractDir, "audit.log"), contractDir, new OutputsHub(contractDir), new OfficeExporter(), new WebSearchClient());
        var pipeline = pipelineFactory.Create(gate, contractDir, false);
        Assert(pipeline.HasExecutor("ExportFile"), "factory registers export executor");
        Assert(pipeline.HasExecutor("OrganizeFolder"), "factory registers organize executor");
        Assert(pipeline.HasExecutor("RunPlugin"), "factory registers plugin executor");
        Assert(pipeline.HasExecutor("EndProcess"), "factory registers process executor");
        Assert(pipeline.HasExecutor("ComputerControl"), "factory registers computer control executor");
        Assert(pipeline.HasExecutor("RollbackFiles"), "factory registers rollback executor");
        Assert(pipeline.HasExecutor("WebSearch"), "factory registers web search executor");
        foreach (var step in plan.Steps)
            if (!string.IsNullOrWhiteSpace(step.CommandType))
                Assert(pipeline.HasExecutor(step.CommandType), "plan command has registered executor: " + step.CommandType);
        foreach (var step in simple.Steps)
            if (!string.IsNullOrWhiteSpace(step.CommandType))
                Assert(pipeline.HasExecutor(step.CommandType), "fallback command has registered executor: " + step.CommandType);
        foreach (var step in open.Steps)
            if (!string.IsNullOrWhiteSpace(step.CommandType))
                Assert(pipeline.HasExecutor(step.CommandType), "open command has registered executor: " + step.CommandType);
    }

    static void TestAgentPlanCommandMapper()
    {
        Console.WriteLine("[AgentPlanCommandMapper]");
        string dir = Path.Combine(Path.GetTempPath(), "zq_planmap_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            string plugin = Path.Combine(dir, "tool.ps1");
            string messy = Path.Combine(dir, "messy");
            Directory.CreateDirectory(messy);
            File.WriteAllText(plugin, "Write-Output 'ok'", Encoding.UTF8);

            string text = "Goal: automate a report\n" +
                "1. Generate a TXT file\n" +
                "2. Wait 5000 ms\n" +
                "3. Search \"plan execution safety\"\n" +
                "4. Run plugin \"" + plugin + "\"\n" +
                "5. Organize folder \"" + messy + "\"\n" +
                "6. End process pid 214748000";
            var plan = new AgentPlanParser().Parse(text);
            var options = new AgentPlanCommandMapperOptions();
            options.TaskId = "task-plan";
            options.TaskTitle = "Plan Mapping";
            options.DefaultOutputDirectory = dir;
            options.DefaultText = "mapped output text";

            var mapping = new AgentPlanCommandMapper().Map(plan, options);
            Assert(mapping.Commands.Count == 6, "all executable plan steps mapped");
            Assert(mapping.Skipped.Count == 0, "no complete plan steps skipped");

            IAgentCommand export = FindCommand(mapping, "ExportFile");
            IAgentCommand wait = FindCommand(mapping, "ComputerControl");
            IAgentCommand search = FindCommand(mapping, "WebSearch");
            IAgentCommand runPlugin = FindCommand(mapping, "RunPlugin");
            IAgentCommand organize = FindCommand(mapping, "OrganizeFolder");
            IAgentCommand endProcess = FindCommand(mapping, "EndProcess");

            Assert(export != null && export.Target.StartsWith(dir, StringComparison.OrdinalIgnoreCase), "export target auto-generated in output dir");
            Assert(export != null && Param(export, "format") == "txt" && Param(export, "text").Contains("mapped output"), "export parameters mapped");
            Assert(wait != null && Param(wait, "action") == "wait" && Param(wait, "ms") == "5000", "wait step mapped to computer wait");
            Assert(search != null && search.Target == "plan execution safety", "search query target mapped");
            Assert(runPlugin != null && runPlugin.Target == plugin, "plugin target mapped");
            Assert(organize != null && Param(organize, "rootDir") == messy, "organize root mapped");
            Assert(endProcess != null && endProcess.Target == "214748000", "process pid mapped");

            var gate = new PermissionGate();
            gate.Set("permFileWrite", PermissionLevel.Allow);
            gate.Set("permAutomationInput", PermissionLevel.Allow);
            var pipeline = new AgentPipeline(gate, new AuditLog(Path.Combine(dir, "audit.log")), new OutputsHub(dir));
            pipeline.Register(new ExportFileExecutor(new OfficeExporter()));
            pipeline.Register(new ComputerControlExecutor());

            var exportResult = pipeline.Run(export);
            Assert(exportResult.Status == CommandStatus.Success && File.Exists(export.Target), "mapped export runs through pipeline");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var waitResult = pipeline.Run(wait);
            sw.Stop();
            Assert(waitResult.Status == CommandStatus.Success && sw.ElapsedMilliseconds < 1500, "mapped long wait runs without blocking pipeline");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static void TestAgentPipeline()
    {
        Console.WriteLine("[AgentPipeline]");
        string dir = Path.Combine(Path.GetTempPath(), "zq_agent_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            var gate = new PermissionGate();
            gate.Set("permFileWrite", PermissionLevel.Allow);
            gate.Set("permFileMoveDelete", PermissionLevel.Allow);
            gate.Set("permPluginRun", PermissionLevel.Allow);
            var outputs = new OutputsHub(dir);
            string auditPath = Path.Combine(dir, "audit.log");
            var audit = new AuditLog(auditPath);
            var pipelineFactory = new AgentPipelineFactory(auditPath, dir, outputs, new OfficeExporter(), new WebSearchClient());
            var pipeline = pipelineFactory.Create(gate, dir, false);
            Assert(pipeline.HasExecutor("WebSearch"), "web search executor registered");

            string file = Path.Combine(dir, "reply.txt");
            var args = new Dictionary<string, object>();
            args["format"] = "txt";
            args["text"] = "hello from src pipeline";
            args["taskTitle"] = "Pipeline Task";
            var command = new AgentCommand("ExportFile", "permFileWrite", "task1", file, "Export test file", args);
            var result = pipeline.Run(command);

            Assert(result.Status == CommandStatus.Success, "export command succeeds");
            Assert(File.Exists(file), "export command writes file");
            Assert(File.ReadAllText(file, Encoding.UTF8).Contains("hello from src pipeline"), "export text persisted");
            Assert(outputs.LoadOutputRows(20).Count == 1, "pipeline records export output");
            Assert(audit.List(20).Count >= 1, "pipeline records audit");

            string messy = Path.Combine(dir, "messy");
            Directory.CreateDirectory(messy);
            string messyFile = Path.Combine(messy, "note.txt");
            File.WriteAllText(messyFile, "to organize");
            var orgArgs = new Dictionary<string, object>();
            orgArgs["rootDir"] = messy;
            orgArgs["taskTitle"] = "Pipeline Task";
            var orgCommand = new AgentCommand("OrganizeFolder", "permFileMoveDelete", "task1", messy, "Organize test folder", orgArgs);
            var orgResult = pipeline.Run(orgCommand);

            Assert(orgResult.Status == CommandStatus.Success, "organize command succeeds");
            Assert(File.Exists(orgResult.RollbackManifestPath), "organize command writes rollback manifest");
            Assert(!File.Exists(messyFile), "organize command moves original file");
            Assert(outputs.LoadOutputRows(20).Count >= 2, "organize command records output");

            gate.Set("permFileMoveDelete", PermissionLevel.Deny);
            var deniedRollback = pipeline.Run(new AgentCommand("RollbackFiles", "permFileMoveDelete", "task1", orgResult.RollbackManifestPath, "Denied rollback", new Dictionary<string, object>()));
            Assert(deniedRollback.Status == CommandStatus.Denied, "rollback command obeys deny permission");
            Assert(!File.Exists(messyFile), "denied rollback leaves file moved");

            gate.Set("permFileMoveDelete", PermissionLevel.Allow);
            var rollbackResult = pipeline.Run(new AgentCommand("RollbackFiles", "permFileMoveDelete", "task1", orgResult.RollbackManifestPath, "Rollback organized folder", new Dictionary<string, object>()));
            Assert(rollbackResult.Status == CommandStatus.Success, "rollback command succeeds");
            Assert(File.Exists(messyFile), "rollback command restores original file");

            string plugin = Path.Combine(dir, "echo.ps1");
            File.WriteAllText(plugin, "$text = [Console]::In.ReadToEnd(); Write-Output (\"plugin:\" + $text)", Encoding.UTF8);
            var pluginArgs = new Dictionary<string, object>();
            pluginArgs["stdin"] = "hello";
            pluginArgs["taskTitle"] = "Pipeline Task";
            var pluginCommand = new AgentCommand("RunPlugin", "permPluginRun", "task1", plugin, "Run test plugin", pluginArgs);
            var pluginResult = pipeline.Run(pluginCommand);

            Assert(pluginResult.Status == CommandStatus.Success, "plugin command succeeds");
            Assert((pluginResult.OutputText ?? "").Contains("plugin:hello"), "plugin output returned");
            Assert(!string.IsNullOrWhiteSpace(pluginResult.ResultPath) && File.Exists(pluginResult.ResultPath), "plugin output persisted as artifact");
            bool pluginOutputRecorded = false;
            foreach (var row in outputs.LoadOutputRows(20))
                if (Str(row, "path") == pluginResult.ResultPath && Str(row, "type") == "plugin-log") pluginOutputRecorded = true;
            Assert(pluginOutputRecorded, "pipeline records plugin output artifact");

            gate.Set("permPluginRun", PermissionLevel.Deny);
            var denied = pipeline.Run(pluginCommand);
            Assert(denied.Status == CommandStatus.Denied, "plugin command obeys deny permission");

            gate.Set("permProcessManage", PermissionLevel.Deny);
            var deniedProcess = pipeline.Run(new AgentCommand("EndProcess", "permProcessManage", "task1", "214748000", "Denied end process", new Dictionary<string, object>()));
            Assert(deniedProcess.Status == CommandStatus.Denied, "process command obeys deny permission");

            gate.Set("permProcessManage", PermissionLevel.Allow);
            var missingProcess = pipeline.Run(new AgentCommand("EndProcess", "permProcessManage", "task1", "214748000", "Missing process", new Dictionary<string, object>()));
            Assert(missingProcess.Status == CommandStatus.Failed, "missing process fails through executor");

            gate.Set("permAutomationInput", PermissionLevel.Deny);
            var controlArgs = new Dictionary<string, object>();
            controlArgs["action"] = "wait";
            controlArgs["ms"] = "1";
            var deniedControl = pipeline.Run(new AgentCommand("ComputerControl", "permAutomationInput", "task1", "1ms", "Denied computer control", controlArgs));
            Assert(deniedControl.Status == CommandStatus.Denied, "computer control obeys deny permission");

            gate.Set("permAutomationInput", PermissionLevel.Allow);
            var waitArgs = new Dictionary<string, object>();
            waitArgs["action"] = "wait";
            waitArgs["ms"] = "5000";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var waitResult = pipeline.Run(new AgentCommand("ComputerControl", "permAutomationInput", "task1", "5000ms", "Long wait", waitArgs));
            sw.Stop();
            Assert(waitResult.Status == CommandStatus.Success && sw.ElapsedMilliseconds < 1500, "long wait does not block command pipeline");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static void TestAgentPipelineAsync()
    {
        Console.WriteLine("[AgentPipeline.Async]");
        string dir = Path.Combine(Path.GetTempPath(), "ZhuaQianAsync-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var gate = new PermissionGate();
            gate.Set("permAutomationInput", PermissionLevel.Allow);
            var pipeline = new AgentPipelineFactory(Path.Combine(dir, "audit.log"), dir, new OutputsHub(dir), new OfficeExporter(), new WebSearchClient())
                .Create(gate, Path.Combine(dir, "plugins"), false);

            var waitArgs = new Dictionary<string, object>();
            waitArgs["action"] = "wait";
            waitArgs["ms"] = "5000";
            var waitCmd = new AgentCommand("ComputerControl", "permAutomationInput", "task1", "5000ms", "Async wait", waitArgs);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = pipeline.RunAsync(waitCmd, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            sw.Stop();

            Assert(result.Status == CommandStatus.Success, "async wait succeeds");
            Assert(sw.ElapsedMilliseconds >= 4000, "async wait actually waits (~5000ms), not fake-complete");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static void TestStreamingBridge()
    {
        Console.WriteLine("[StreamingBridge]");
        string openai = "{\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}";
        Assert(StreamingBridge.ExtractDelta(openai) == "Hello", "openai delta extracted");
        string gemini = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Hi\"}]}}]}";
        Assert(StreamingBridge.ExtractDelta(gemini) == "Hi", "gemini delta extracted");
        Assert(StreamingBridge.ExtractDelta("[DONE]") == "", "done ignored");
        Assert(StreamingBridge.ExtractDelta("not json") == "", "garbage ignored");

        // edge cases
        string multi = "{\"choices\":[{\"delta\":{\"content\":\"A\"}},{\"delta\":{\"content\":\"B\"}}]}";
        Assert(StreamingBridge.ExtractDelta(multi) == "A", "first choice used");
        string withPrefix = "data:{\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}";
        Assert(StreamingBridge.ExtractDelta(withPrefix) == "Hi", "data: prefix stripped");
        Assert(StreamingBridge.ExtractDelta("{\"choices\":[]}") == "", "empty choices ignored");
        Assert(StreamingBridge.ExtractDelta("{not valid json") == "", "malformed json ignored");
        Assert(StreamingBridge.ExtractDelta("") == "", "empty line ignored");
    }

    static void TestWebSearchClient()
    {
        Console.WriteLine("[WebSearchClient]");
        var client = new WebSearchClient();
        Assert(client.Search("", 3).Count == 0, "empty search returns no results");
        var emptySearch = client.SearchDetailed("", 3);
        Assert(!emptySearch.Success && emptySearch.ErrorMessage.Contains("empty"), "detailed empty search explains failure");
        var bad = client.FetchPage("not-a-url", 1000);
        Assert(bad != null && !bad.Success && bad.ErrorMessage.Contains("Invalid URL"), "invalid URL fails without hallucinated page");
        string error;
        Assert(!client.ValidatePublicHttpUrl("http://localhost/", out error), "localhost URL blocked");
        Assert(!client.ValidatePublicHttpUrl("http://127.0.0.1/", out error), "loopback URL blocked");
        Assert(!client.ValidatePublicHttpUrl("http://169.254.169.254/latest/meta-data/", out error), "metadata URL blocked");
        Assert(!client.ValidatePublicHttpUrl("https://example.com:8443/", out error), "non-web port blocked");
    }

    static void TestProcessSnapshotCollector()
    {
        Console.WriteLine("[ProcessSnapshotCollector]");
        string dir = Path.Combine(Path.GetTempPath(), "zq_mon_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string events = Path.Combine(dir, "monitoring-events.jsonl");
        string cases = Path.Combine(dir, "monitoring-cases.jsonl");
        var col = new ProcessSnapshotCollector(dir, events, cases);
        var snaps = col.Collect();
        Assert(snaps != null && snaps.Count > 0, "collects process snapshots");

        int written = col.RecordSnapshot();
        Assert(written > 0, "record snapshot writes events");
        Assert(File.Exists(events), "events file created");

        string caseId = col.OpenCase("Test case", "low", "summary");
        Assert(!string.IsNullOrEmpty(caseId), "open case returns id");
        col.CloseCase(caseId);

        var loadedCases = col.LoadCases(10);
        Assert(loadedCases.Count >= 1, "case loaded");
        bool found = false;
        foreach (var c in loadedCases) if (c.caseId == caseId) found = true;
        Assert(found, "opened case present in loaded cases");

        // edge cases
        col.CloseCase("");               // no-op on empty id
        col.CloseCase("case-does-not-exist"); // no-op on unknown id
        var reloaded = col.LoadCases(10);
        bool closed = false;
        foreach (var c in reloaded) if (c.caseId == caseId && c.status == "closed") closed = true;
        Assert(closed, "case marked closed after CloseCase");

        var loadedEvents = col.LoadEvents(100);
        Assert(loadedEvents.Count >= 1, "events loaded");
        Assert(col.LoadEvents(-5).Count >= 0, "negative max is safe");
        col.ClearRecords();
        Assert(col.LoadEvents(10).Count == 0, "clear removes events");
        Assert(col.LoadCases(10).Count == 0, "clear removes cases");
        try { Directory.Delete(dir, true); } catch { }
    }

    static void TestSystemDiagnostics()
    {
        Console.WriteLine("[SystemDiagnostics]");
        var diag = new SystemDiagnostics();
        string report = diag.BuildReport();
        Assert(report.Contains("Local Computer Diagnostics"), "diagnostic title present");
        Assert(report.Contains("Top Memory Processes"), "process section present");
        Assert(report.Contains("Drives"), "drive section present");
        Assert(report.Contains("Window titles are omitted for privacy"), "window titles are omitted");
        Assert(!report.Contains(" window=\""), "window title values not emitted");

        string dir = Path.Combine(Path.GetTempPath(), "zq_code_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            var exec = new ExportFileExecutor(new OfficeExporter());
            var args = new Dictionary<string, object>();
            args["format"] = "py";
            args["text"] = "print('hello')";
            string path = Path.Combine(dir, "hello.py");
            var result = exec.Execute(new AgentCommand("ExportFile", "permFileWrite", "taskCode", path, "Generate code file", args));
            Assert(result.Status == CommandStatus.Success, "code export succeeds");
            Assert(File.Exists(path), "code file written");
            Assert(File.ReadAllText(path).Contains("print"), "code content persisted");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static void TestSmoke()
    {
        Console.WriteLine("[Smoke]");
        string dir = Path.Combine(Path.GetTempPath(), "zq_smoke_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            string src = Path.Combine(dir, "in");
            Directory.CreateDirectory(src);
            string f = Path.Combine(src, "note.txt");
            File.WriteAllText(f, "call 13800138000 invoice 发票");

            var red = new Redactor(true);
            string cleaned = red.Apply(File.ReadAllText(f));
            Assert(cleaned.Contains("[REDACTED_PHONE]"), "smoke: redaction");

            var chunker = new Chunker();
            var chunks = chunker.Split(cleaned, 50);
            Assert(chunks.Count >= 1, "smoke: chunked");

            var hub = new OutputsHub(dir);
            hub.RecordOutput("export", "txt", f, "taskS", "Smoke", "", (int)new FileInfo(f).Length);
            Assert(hub.LoadOutputRows(10).Count >= 1, "smoke: output recorded");

            var org = new FolderOrganizer(dir);
            var plan = org.BuildPlan(src);
            var res = org.Execute(src, plan);
            Assert(res.Moved >= 1, "smoke: organized");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static string Str(Dictionary<string, object> d, string key)
    {
        return d.ContainsKey(key) ? Convert.ToString(d[key]) : "";
    }

    static IAgentCommand FindCommand(AgentPlanCommandMapping mapping, string commandType)
    {
        foreach (IAgentCommand command in mapping.Commands)
            if (command.CommandType == commandType) return command;
        return null;
    }

    static string Param(IAgentCommand command, string key)
    {
        object value;
        if (command != null && command.Parameters != null && command.Parameters.TryGetValue(key, out value) && value != null)
            return Convert.ToString(value);
        return "";
    }

    static void TestPluginManifest()
    {
        Console.WriteLine("[PluginManifest]");
        string dir = Path.Combine(Path.GetTempPath(), "zq_manifest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            string manifestPath = Path.Combine(dir, "plugin.json");
            File.WriteAllText(manifestPath,
                "{\"id\":\"summarize\",\"name\":\"Summarize\",\"version\":\"1.0.0\",\"author\":\"me\"," +
                "\"description\":\"summarize text\",\"entry\":\"run.ps1\",\"entryType\":\"ps1\"," +
                "\"requiredPermissions\":[\"permFileWrite\"],\"hooks\":[\"AfterCommand\"],\"trusted\":true}");
            File.WriteAllText(Path.Combine(dir, "run.ps1"), "Write-Output hi");

            var parser = new PluginManifestParser();
            var ok = parser.ParseFromFile(manifestPath);
            Assert(ok.Success, "valid manifest parses");
            Assert(ok.Manifest != null && ok.Manifest.Id == "summarize", "id parsed");
            Assert(ok.Manifest.EntryType == PluginEntryType.Ps1, "entryType parsed");
            Assert(ok.Manifest.RequiredPermissions.Contains("permFileWrite"), "permission parsed");
            Assert(ok.Manifest.Hooks.Contains("AfterCommand"), "hook parsed");
            Assert(ok.Manifest.Trusted, "trusted parsed");

            var round = parser.ParseFromString(ok.Manifest.ToJson());
            Assert(round.Success && round.Manifest.Id == "summarize", "round-trips via ToJson");

            var bad = parser.ParseFromString("{\"name\":\"x\",\"entry\":\"run.ps1\",\"entryType\":\"ps1\"}");
            Assert(!bad.Success && bad.Errors.Exists(e => e.Contains("id")), "missing id rejected");

            var badPerm = parser.ParseFromString("{\"id\":\"x\",\"name\":\"x\",\"version\":\"1.0.0\",\"entry\":\"run.ps1\",\"entryType\":\"ps1\",\"requiredPermissions\":[\"permNope\"]}");
            Assert(!badPerm.Success && badPerm.Errors.Exists(e => e.Contains("permNope")), "unknown permission rejected");

            var badHook = parser.ParseFromString("{\"id\":\"x\",\"name\":\"x\",\"version\":\"1.0.0\",\"entry\":\"run.ps1\",\"entryType\":\"ps1\",\"hooks\":[\"OnTuesday\"]}");
            Assert(!badHook.Success && badHook.Errors.Exists(e => e.Contains("OnTuesday")), "unknown hook rejected");

            var traverse = parser.ParseFromString("{\"id\":\"x\",\"name\":\"x\",\"version\":\"1.0.0\",\"entry\":\"..\\\\..\\\\evil.ps1\",\"entryType\":\"ps1\"}");
            Assert(!traverse.Success, "path traversal entry rejected");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static void TestHookRegistry()
    {
        Console.WriteLine("[HookRegistry]");
        var reg = new HookRegistry();
        int fired = 0;
        reg.Register(new TestAfterCommandHook("h1", () => fired++));
        Assert(reg.Count(HookKind.AfterCommand) == 1, "hook registered");
        Assert(reg.Get(HookKind.AfterCommand).Count == 1, "Get returns registered");
        reg.Run(HookKind.AfterCommand, new HookContext { Kind = HookKind.AfterCommand });
        Assert(fired == 1, "hook invoked on Run");

        int fired2 = 0;
        reg.Register(new TestAfterCommandHook("h2", () => fired2++));
        reg.Register(new ThrowingHook("hbad"));
        reg.Run(HookKind.AfterCommand, new HookContext { Kind = HookKind.AfterCommand });
        Assert(fired2 == 1, "good hook still runs after a bad one throws");
        Assert(reg.LastErrors.Exists(e => e.HookId == "hbad"), "bad hook error recorded");

        reg.Run(HookKind.BeforeCommand, new HookContext { Kind = HookKind.BeforeCommand });
        Assert(true, "Run on empty kind is safe");
    }

    sealed class TestAfterCommandHook : IPluginHook
    {
        readonly Action _onInvoke;
        public TestAfterCommandHook(string id, Action onInvoke) { Id = id; _onInvoke = onInvoke; }
        public string Id { get; private set; }
        public HookKind Kind { get { return HookKind.AfterCommand; } }
        public void Invoke(HookContext ctx) { _onInvoke(); }
    }

    sealed class ThrowingHook : IPluginHook
    {
        public string Id { get; private set; }
        public ThrowingHook(string id) { Id = id; }
        public HookKind Kind { get { return HookKind.AfterCommand; } }
        public void Invoke(HookContext ctx) { throw new InvalidOperationException("boom"); }
    }
}
