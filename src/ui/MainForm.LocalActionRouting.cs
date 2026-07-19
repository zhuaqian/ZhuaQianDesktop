using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Providers;
using ZhuaQianDesktopApp.Tools;

namespace ZhuaQianDesktopApp
{
    // Natural-language local-action parsing, computer-control command execution,
    // and the routing helpers that decide organize / plugin / open / end-process /
    // web-search actions. Extracted from the oversized ZhuaQianDesktop.cs to keep the
    // main file within its line budget. Pure move (no logic change); all members
    // belong to the same partial class and access shared fields directly.
    public partial class MainForm
    {
        bool TryRunLocalComputerCommand(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string trimmed = raw.Trim();
            if (!trimmed.StartsWith("/")) return false;

            var parsed = new Tools.CommandParser().Parse(trimmed);
            if (!parsed.IsCommand) return false;
            if (!string.IsNullOrWhiteSpace(parsed.Error))
            {
                AppendChat("Error", parsed.Error, Color.FromArgb(190, 40, 40));
                return true;
            }

            string verb = (parsed.Verb ?? "").ToLowerInvariant();
            if (verb == "help")
            {
                AppendChat("ZhuaQian",
                    "Local computer-control commands:\r\n" +
                    "  /open notepad\r\n" +
                    "  /open \"C:\\\\path\\\\file.txt\"\r\n" +
                    "  /save docx\r\n" +
                    "  /save format=pptx text=\"meeting outline\"\r\n" +
                    "  /type \"hello\"\r\n" +
                    "  /hotkey ctrl+v\r\n" +
                    "  /key enter\r\n" +
                    "  /click 300 450\r\n" +
                    "  /wait 1000\r\n" +
                    "  /remote check\r\n" +
                    "  /remote run host=user@example.com command=\"pwd && ls\"\r\n" +
                    "  /remote pull host=user@example.com remotePath=/var/log/app.log localPath=C:\\\\Temp\\\\app.log\r\n" +
                    "  /remote push host=user@example.com localPath=C:\\\\Temp\\\\app.txt remotePath=/tmp/app.txt",
                    Color.FromArgb(0, 130, 80));
                input.Clear();
                return true;
            }

            if (verb == "remote" || verb == "ssh" || verb == "server")
            {
                RunRemoteCommand(parsed);
                input.Clear();
                return true;
            }

            if (verb == "save" || verb == "file" || verb == "export")
            {
                RunSaveCommand(parsed);
                input.Clear();
                return true;
            }

            if (verb != "open" && verb != "type" && verb != "hotkey" && verb != "key" && verb != "click" && verb != "wait")
                return false;

            var parameters = new Dictionary<string, object>();
            string target = "";
            string summary = "";

            if (verb == "open")
            {
                target = FlagOrArgs(parsed, "target", "path", "url");
                parameters["action"] = "open";
                parameters["target"] = target;
                summary = "Open " + target;
            }
            else if (verb == "type")
            {
                target = FlagOrArgs(parsed, "text", "target", "");
                parameters["action"] = "type";
                parameters["text"] = target;
                summary = "Type text (" + target.Length + " chars)";
            }
            else if (verb == "hotkey")
            {
                target = FlagOrArgs(parsed, "sequence", "target", "");
                parameters["action"] = "hotkey";
                parameters["sequence"] = target;
                summary = "Send hotkey " + target;
            }
            else if (verb == "key")
            {
                target = FlagOrArgs(parsed, "key", "target", "");
                parameters["action"] = "key";
                parameters["key"] = target;
                summary = "Press key " + target;
            }
            else if (verb == "click")
            {
                string x = parsed.Flags.ContainsKey("x") ? parsed.Flags["x"] : (parsed.Args.Count > 0 ? parsed.Args[0] : "");
                string y = parsed.Flags.ContainsKey("y") ? parsed.Flags["y"] : (parsed.Args.Count > 1 ? parsed.Args[1] : "");
                parameters["action"] = "click";
                parameters["x"] = x;
                parameters["y"] = y;
                if (parsed.Flags.ContainsKey("button")) parameters["button"] = parsed.Flags["button"];
                target = x + "," + y;
                summary = "Click " + target;
            }
            else if (verb == "wait")
            {
                string ms = parsed.Flags.ContainsKey("ms") ? parsed.Flags["ms"] : (parsed.Args.Count > 0 ? parsed.Args[0] : "1000");
                parameters["action"] = "wait";
                parameters["ms"] = ms;
                target = ms + "ms";
                summary = "Wait " + target;
            }

            if (string.IsNullOrWhiteSpace(target) && verb != "wait")
            {
                AppendChat("Error", "Missing target for /" + verb + ". Type /help for examples.", Color.FromArgb(190, 40, 40));
                return true;
            }

            RunComputerControl(parameters, target, summary);
            input.Clear();
            return true;
        }

        bool TryRunNaturalLocalAction(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string text = raw.Trim();
            if (text.StartsWith("/")) return false;
            string lower = text.ToLowerInvariant();

            if (LooksLikeComputerDiagnosisRequest(lower))
            {
                string report = ForceRedaction(systemDiagnostics.BuildReport());
                if (!HasUsableProviderKey())
                {
                    AppendChat("ZhuaQian", report, Color.FromArgb(0, 130, 80));
                    input.Clear();
                    return true;
                }
                if (MayUseCloudProvider(null))
                {
                    if (!EnsurePermission(Tr("Cloud/network upload", "云端/网络上传", "雲端/網路上傳"), permNetworkUpload, false, "Upload diagnostics")) return true;
                    if (!ConfirmDiagnosticsCloudUpload(report)) return true;
                }
                pendingParts.Insert(0, NewTextPart("[Local computer diagnostics]\r\n" + report));
                return false;
            }

            if (LooksLikeRemoteCheckRequest(lower))
            {
                var parsed = new Tools.ParsedCommand();
                parsed.Verb = "remote";
                parsed.Args.Add("check");
                RunRemoteCommand(parsed);
                input.Clear();
                return true;
            }

            if (LooksLikeUrlAnalysisRequest(text, lower))
            {
                ExecuteUrlAnalysisReport(text);
                input.Clear();
                return true;
            }

            if (LooksLikeEndProcessRequest(lower))
            {
                int pid;
                if (!TryExtractPid(text, out pid))
                {
                    AppendChat("Error", Tr("Missing PID. Example: ", "缺少 PID。示例：", "缺少 PID。範例：") + "结束 PID 1234", Color.FromArgb(190, 40, 40));
                    return true;
                }
                EndProcessByPid(pid);
                input.Clear();
                return true;
            }

            if (LooksLikePluginRequest(lower))
            {
                string path = ExtractNaturalTarget(text, new string[] { "运行插件", "执行插件", "執行外掛", "run plugin", "execute plugin" });
                path = CleanPath(path);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    AppendChat("Error", Tr("Plugin file not found. Example: ", "找不到插件文件。示例：", "找不到外掛檔案。範例：") + "运行插件 \"C:\\path\\tool.py\"", Color.FromArgb(190, 40, 40));
                    return true;
                }
                RunPluginPath(path, text);
                input.Clear();
                return true;
            }

            if (LooksLikeOrganizeFolderRequest(lower))
            {
                string path = ExtractNaturalTarget(text, new string[] { "整理文件夹", "整理資料夾", "整理文件夾", "整理目录", "整理目錄", "organize folder", "organise folder" });
                path = CleanPath(path);
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    AppendChat("Error", Tr("Folder not found. Example: ", "找不到文件夹。示例：", "找不到資料夾。範例：") + "整理文件夹 \"C:\\path\\folder\"", Color.FromArgb(190, 40, 40));
                    return true;
                }
                ExecuteOrganizeFolder(path);
                input.Clear();
                return true;
            }

            if (LooksLikeOpenRequest(lower))
            {
                string target = ExtractNaturalTarget(text, new string[] { "打开", "開啟", "启动", "啟動", "open", "launch" });
                target = NormalizeOpenTarget(target);
                if (string.IsNullOrWhiteSpace(target))
                {
                    AppendChat("Error", Tr("Missing open target. Example: ", "缺少打开目标。示例：", "缺少開啟目標。範例：") + "打开记事本 / 打开 \"C:\\path\\file.txt\"", Color.FromArgb(190, 40, 40));
                    return true;
                }

                var parameters = new Dictionary<string, object>();
                parameters["action"] = "open";
                parameters["target"] = target;
                RunComputerControl(parameters, target, "Open " + target);
                input.Clear();
                return true;
            }

            if (LooksLikeOfficeGenerateRequest(lower))
            {
                ExecuteOfficeGenerate(text);
                input.Clear();
                return true;
            }

            if (LooksLikeDiagnoseFixRequest(lower))
            {
                ExecuteDiagnoseFix(text);
                input.Clear();
                return true;
            }

            return false;
        }

        bool LooksLikeEndProcessRequest(string lower)
        {
            return ContainsAny(lower, "结束进程", "終止處理程序", "结束 pid", "終止 pid", "kill pid", "end process", "terminate process");
        }

        bool LooksLikeUrlAnalysisRequest(string text, string lower)
        {
            List<string> urls = ExtractWebUrls(text);
            if (urls.Count == 0) return false;
            string withoutUrls = Regex.Replace(text ?? "", @"(?:https?://[^\s""'<>]+|www\.[a-z0-9][a-z0-9-]*(?:\.[a-z0-9][a-z0-9-]*)+(?::\d{1,5})?(?:/[^\s""'<>]*)?|[a-z0-9][a-z0-9-]*(?:\.[a-z0-9][a-z0-9-]*)*\.(?:com|cn|net|org|io|ai|app|dev|top|shop|site|xyz|cc|co|info|biz|me|tv|edu|gov)(?::\d{1,5})?(?:/[^\s""'<>]*)?)", "", RegexOptions.IgnoreCase).Trim();
            if (withoutUrls.Length == 0) return true;
            return ContainsAny(lower,
                "分析网址", "分析网站", "分析网页", "分析链接", "分析页面", "读取网址", "读取网站", "读取网页", "读取链接", "读一下", "抓取", "抓取网站", "抓取网页", "打开网页", "看一下", "总结网页", "总结网站", "总结链接", "提取网页", "整理网页", "生成报告", "生成分析报告", "分析报告",
                "分析這個網址", "分析連結", "讀取網址", "讀取網站", "讀取網頁", "讀取連結", "總結網頁", "產生報告",
                "analyze url", "analyze website", "analyze webpage", "analyze link", "read url", "read website", "read webpage", "read link", "fetch url", "fetch page", "fetch website", "open webpage", "summarize url", "summarize website", "summarize link", "generate report", "analysis report");
        }

        void ExecuteUrlAnalysisReport(string raw)
        {
            List<string> urls = ExtractWebUrls(raw);
            if (urls.Count == 0)
            {
                AppendChat("Error", Tr("No URL found.", "没有找到网址。", "沒有找到網址。"), Color.FromArgb(190, 40, 40));
                return;
            }

            if (!EnsurePermission(Tr("Fetch/analyze URL", "读取/分析网址", "讀取/分析網址"), permNetworkUpload, false, "URL Page Fetch"))
                return;

            AppendChat("You", "[Mode: " + ModeDisplayName(workMode) + "]\r\n" + raw, Color.FromArgb(30, 90, 180));
            SetCurrentTaskStatus("running", "Fetching URL page text", false);

            var pages = new List<WebPageFetchResult>();
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string url in urls)
            {
                WebPageFetchResult page = Tools.WebResearchFetcher.FetchOne(url, 50000, browserRenderClient, true);
                if (page != null && page.Success)
                    page.Text = ApplyRedaction(page.Text ?? "");
                pages.Add(page);
                if (page != null && !string.IsNullOrWhiteSpace(page.Url)) seenUrls.Add(page.Url);
            }

            SetCurrentTaskStatus("running", "Searching related web sources", false);
            string searchQuery = BuildUrlResearchSearchQuery(raw, pages, urls);
            WebSearchResponse search = webSearchClient.SearchDetailed(searchQuery, 8);
            if (search != null && search.Results != null)
            {
                foreach (WebSearchResult result in search.Results)
                {
                    if (result == null || string.IsNullOrWhiteSpace(result.Url)) continue;
                    string cleanUrl = webSearchClient.CleanUrl(result.Url);
                    if (seenUrls.Contains(cleanUrl)) continue;
                    WebPageFetchResult page = Tools.WebResearchFetcher.FetchOne(cleanUrl, 40000, browserRenderClient, true);
                    if (page != null && page.Success)
                        page.Text = ApplyRedaction(page.Text ?? "");
                    pages.Add(page);
                    seenUrls.Add(cleanUrl);
                    if (pages.Count >= 8) break;
                }
            }

            string provider = search == null ? "" : search.Provider;
            if (search != null && !search.Success && !string.IsNullOrWhiteSpace(search.ErrorMessage))
                provider = provider + " failed: " + search.ErrorMessage;
            WebPageAnalysisReport report = WebPageReportBuilder.Build(raw, pages, search == null ? null : search.Results, provider, searchQuery, DateTime.Now);
            AppendChat("ZhuaQian", report.Markdown, Color.FromArgb(0, 130, 80));

            int fetched = report.SuccessCount;
            int failed = report.FailureCount;
            LogAction("UrlAnalysis", "Fetched " + fetched.ToString() + " URL(s), failed " + failed.ToString() + ", search results " + report.SearchResultCount.ToString());
            RecordAction("UrlAnalysis", failed == 0 ? "success" : (fetched > 0 ? "partial" : "failed"), "Fetched " + fetched.ToString() + " URL(s), failed " + failed.ToString() + ", search results " + report.SearchResultCount.ToString(), "");
            SetCurrentTaskStatus(fetched > 0 ? "ready_for_review" : "failed", "URL analysis fetched " + fetched.ToString() + " page(s)", true);

            string format = DetectExportFormat(raw);
            if (string.IsNullOrWhiteSpace(format) && LooksLikeReportFileRequest(raw.ToLowerInvariant()))
                format = "md";
            if (!string.IsNullOrWhiteSpace(format))
            {
                lastExportNameHint = string.IsNullOrWhiteSpace(report.TitleHint) ? "网站分析报告" : report.TitleHint;
                if (!SaveTextAsFormat(report.Markdown, format, true))
                    AppendChat("Error", Tr("Report file generation failed. Use Save File to choose a path manually.", "报告文件生成失败。可用“保存文件”手动选择路径。", "報告檔案產生失敗。可用「儲存檔案」手動選擇路徑。"), Color.FromArgb(190, 40, 40));
            }

            SaveCurrentTask();
        }

        string BuildUrlResearchSearchQuery(string raw, IList<WebPageFetchResult> pages, IList<string> urls)
        {
            string query = Regex.Replace(raw ?? "", @"(?:https?://[^\s""'<>]+|www\.[a-z0-9][a-z0-9-]*(?:\.[a-z0-9][a-z0-9-]*)+(?::\d{1,5})?(?:/[^\s""'<>]*)?|[a-z0-9][a-z0-9-]*(?:\.[a-z0-9][a-z0-9-]*)*\.(?:com|cn|net|org|io|ai|app|dev|top|shop|site|xyz|cc|co|info|biz|me|tv|edu|gov)(?::\d{1,5})?(?:/[^\s""'<>]*)?)", " ", RegexOptions.IgnoreCase);
            query = Regex.Replace(query, @"\b(分析|读取|读一下|抓取|总结|生成|报告|网页|网站|链接|网址|深度|analyze|read|fetch|summarize|generate|report|url|website|webpage|link)\b", " ", RegexOptions.IgnoreCase);
            query = Regex.Replace(query, @"\s+", " ").Trim();
            if (query.Length < 4 && pages != null)
            {
                foreach (WebPageFetchResult page in pages)
                {
                    if (page != null && page.Success && !string.IsNullOrWhiteSpace(page.Title))
                    {
                        query = page.Title.Trim();
                        break;
                    }
                }
            }
            if (query.Length < 4 && urls != null && urls.Count > 0)
            {
                try { query = new Uri(webSearchClient.CleanUrl(urls[0])).Host; }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("BuildUrlResearchSearchQuery: " + ex.Message); query = urls[0]; }
            }
            if (query.Length > 160) query = query.Substring(0, 160).Trim();
            return query.Length == 0 ? "website analysis" : query;
        }

        bool LooksLikeReportFileRequest(string lower)
        {
            return ContainsAny(lower, "生成报告", "生成分析报告", "保存报告", "导出报告", "產生報告", "儲存報告", "匯出報告", "generate report", "save report", "export report");
        }

        bool LooksLikeComputerDiagnosisRequest(string lower)
        {
            return ContainsAny(lower,
                "分析电脑", "分析電腦", "诊断电脑", "診斷電腦", "电脑很卡", "電腦很卡", "系统诊断", "系統診斷",
                "电脑问题", "電腦問題", "本机诊断", "本機診斷", "computer diagnostic", "diagnose computer",
                "analyze my computer", "slow computer", "system diagnostic", "what is wrong with my pc");
        }

        bool LooksLikeRemoteCheckRequest(string lower)
        {
            return ContainsAny(lower,
                "检查ssh", "检查 ssh", "检查远程工具", "检查服务器工具", "远程工具", "服务器工具", "云主机工具",
                "檢查ssh", "檢查 ssh", "遠端工具", "伺服器工具", "雲主機工具",
                "check ssh", "remote tools", "server tools", "cloud host tools");
        }

        bool LooksLikePluginRequest(string lower)
        {
            return ContainsAny(lower, "运行插件", "执行插件", "執行外掛", "run plugin", "execute plugin")
                || (ContainsAny(lower, "运行", "执行", "run", "execute") && ContainsAny(lower, ".py", ".ps1", ".bat", ".cmd", ".exe"));
        }

        bool LooksLikeOrganizeFolderRequest(string lower)
        {
            return ContainsAny(lower, "整理文件夹", "整理文件夾", "整理資料夾", "整理目录", "整理目錄", "organize folder", "organise folder")
                || (ContainsAny(lower, "整理", "分类", "分類", "organize", "organise") && ContainsAny(lower, "文件夹", "文件夾", "資料夾", "目录", "目錄", "folder"));
        }

        bool LooksLikeOpenRequest(string lower)
        {
            return ContainsAny(lower, "打开", "開啟", "启动", "啟動", "open ", "launch ");
        }

        bool LooksLikeOfficeGenerateRequest(string lower)
        {
            return ContainsAny(lower,
                "生成报告", "產生報告", "生成纪要", "產生紀要", "生成会议纪要", "生成會議紀要",
                "生成演示", "產生簡報", "做演示", "做一份报告", "做一份報告", "写一份报告", "寫一份報告",
                "写一份纪要", "寫一份紀要", "做海报", "做海報", "生成海报", "生成海報",
                "生成表格", "產生表格", "生成 ppt", "生成 pptx", "生成 word", "生成 docx",
                "生成 excel", "生成 xlsx", "生成文档", "生成文件", "產生文件",
                "generate report", "generate a report", "create report", "make a report",
                "generate presentation", "generate ppt", "generate pptx",
                "generate poster", "make a poster", "generate spreadsheet",
                "generate word", "generate docx", "generate excel", "generate xlsx",
                "make a docx", "create a document");
        }

        void ExecuteOfficeGenerate(string raw)
        {
            using (var dlg = new Ui.OfficeGenerateDialog(raw, Tr, uiLanguage))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    input.Clear();
                    return;
                }

                if (!EnsurePermission(
                        Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"),
                        permFileWrite,
                        true,
                        "Generate Office File"))
                    return;

                var parameters = new Dictionary<string, object>();
                parameters["kind"] = dlg.ResultKind.ToString();
                parameters["format"] = dlg.ResultFormat;
                parameters["text"] = dlg.ResultText;
                string target = dlg.ResultTarget;

                var genGate = PermissionGate.FromJson(permGate.ToJson());
                genGate.Set("permFileWrite", PermissionLevel.Ask);
                var pipeline = agentPipelineFactory.Create(genGate, pluginDir, allowAdvancedPlugins);
                pipeline.RequestApproval = approvalCommand => ShowApprovalCard(
                    "OfficeTemplate",
                    Tr("Confirm office file generation", "确认生成办公文件", "確認產生辦公檔案"),
                    Tr("Generate", "生成", "產生"),
                    Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"),
                    target,
                    Tr("This writes a file to your disk at the path you chose.",
                       "这会把文件写入你选择的路径。",
                       "這會把檔案寫入你選擇的路徑。"),
                    Tr("Generate", "生成", "產生") + " " + dlg.ResultKind,
                    "Kind: " + dlg.ResultKind + "\r\nTarget: " + target,
                    target);

                var genCommand = new AgentCommand("OfficeTemplate", "permFileWrite", currentTaskId, target,
                    "Generate " + dlg.ResultKind + " -> " + target, parameters);
                var result = pipeline.Run(genCommand);
                if (result.Status == CommandStatus.Success)
                {
                    LogAction("OfficeTemplate", "Generated " + dlg.ResultKind + " -> " + target);
                    RecordAction("OfficeTemplate", "success", "Generated " + dlg.ResultKind + " -> " + target, "");
                    AppendChat("ZhuaQian",
                        Tr("Generated ", "已生成 ", "已產生 ") + dlg.ResultKind + ": " + target,
                        Color.FromArgb(0, 130, 80));
                }
                else if (result.Status == CommandStatus.Cancelled)
                {
                    RecordAction("OfficeTemplate", "cancelled", "Generate " + dlg.ResultKind, "");
                }
                else if (result.Status == CommandStatus.Denied)
                {
                    SetCurrentTaskStatus("needs_input", "Office file generation denied", true);
                    RecordAction("OfficeTemplate", "denied", result.ErrorMessage, "");
                    MessageBox.Show(this, result.ErrorMessage, "Office file generation denied");
                }
                else
                {
                    SetCurrentTaskStatus("failed", "Office file generation failed", true);
                    RecordAction("OfficeTemplate", "failed", result.ErrorMessage, "");
                    MessageBox.Show(this, result.ErrorMessage, "Office file generation failed");
                }
            }
        }

        bool ContainsAny(string value, params string[] needles)
        {
            if (value == null) return false;
            foreach (string needle in needles)
                if (!string.IsNullOrEmpty(needle) && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        string ExtractNaturalTarget(string text, string[] verbs)
        {
            string quoted = ExtractQuotedText(text);
            if (!string.IsNullOrWhiteSpace(quoted)) return TrimNaturalTarget(quoted);

            var url = Regex.Match(text, @"https?://\S+", RegexOptions.IgnoreCase);
            if (url.Success) return TrimNaturalTarget(url.Value);

            var path = Regex.Match(text, @"[A-Za-z]:\\[^""'<>|\r\n]+");
            if (path.Success) return TrimNaturalTarget(path.Value);

            foreach (string verb in verbs)
            {
                int index = text.IndexOf(verb, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                    return TrimNaturalTarget(text.Substring(index + verb.Length));
            }
            return "";
        }

        string ExtractQuotedText(string text)
        {
            var match = Regex.Match(text, "\"([^\"]+)\"|'([^']+)'|“([^”]+)”|‘([^’]+)’");
            if (!match.Success) return "";
            for (int i = 1; i < match.Groups.Count; i++)
                if (match.Groups[i].Success) return match.Groups[i].Value;
            return "";
        }

        string TrimNaturalTarget(string target)
        {
            if (target == null) return "";
            target = target.Trim();
            target = target.Trim('"', '\'', '“', '”', '‘', '’', ' ', '\t', '\r', '\n', '，', '。', '；', ';');
            string[] prefixes = { "一下", "这个", "這個", "该", "該", "文件夹", "文件夾", "資料夾", "目录", "目錄", "文件" };
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (string prefix in prefixes)
                {
                    if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        target = target.Substring(prefix.Length).Trim();
                        changed = true;
                    }
                }
            }
            string[] stops = { " 这个", " 這個", " 文件夹", " 文件夾", " 資料夾", " 目录", " 目錄", " folder", " 然后", " 然後", " 并", " 並", "，", "。", "\r", "\n" };
            foreach (string stop in stops)
            {
                int index = target.IndexOf(stop, StringComparison.OrdinalIgnoreCase);
                if (index > 0) target = target.Substring(0, index).Trim();
            }
            return target.Trim('"', '\'', '“', '”', '‘', '’', ' ', '\t', '，', '。', '；', ';');
        }

        string NormalizeOpenTarget(string target)
        {
            target = TrimNaturalTarget(target);
            string lower = (target ?? "").ToLowerInvariant();
            if (lower == "记事本" || lower == "記事本") return "notepad";
            if (lower == "计算器" || lower == "計算機" || lower == "計算器") return "calc";
            if (lower == "画图" || lower == "小画家" || lower == "小畫家") return "mspaint";
            if (lower == "资源管理器" || lower == "檔案總管" || lower == "文件资源管理器") return "explorer";
            if (File.Exists(target) || Directory.Exists(target)) return CleanPath(target);
            return target;
        }

        bool TryExtractPid(string text, out int pid)
        {
            pid = 0;
            var match = Regex.Match(text, @"(?:pid|PID|进程|處理程序|process)\D{0,12}(\d{1,10})");
            if (!match.Success) match = Regex.Match(text, @"\b(\d{2,10})\b");
            return match.Success && int.TryParse(match.Groups[1].Value, out pid);
        }

        void RunSaveCommand(Tools.ParsedCommand parsed)
        {
            string format = "";
            if (parsed.Flags.ContainsKey("format")) format = parsed.Flags["format"];
            else if (parsed.Flags.ContainsKey("type")) format = parsed.Flags["type"];
            else if (parsed.Args.Count > 0) format = parsed.Args[0];
            format = NormalizeExportFormat(format);
            if (string.IsNullOrWhiteSpace(format)) format = "txt";

            string content = "";
            if (parsed.Flags.ContainsKey("text")) content = parsed.Flags["text"];
            else if (parsed.Flags.ContainsKey("content")) content = parsed.Flags["content"];
            else if (parsed.Args.Count > 1)
            {
                var tail = new List<string>();
                for (int i = 1; i < parsed.Args.Count; i++) tail.Add(parsed.Args[i]);
                content = string.Join(" ", tail.ToArray());
            }
            if (string.IsNullOrWhiteSpace(content)) content = GetLastModelReply();
            if (string.IsNullOrWhiteSpace(content) && chat != null) content = chat.SelectedText;
            if (string.IsNullOrWhiteSpace(content))
            {
                AppendChat("Error", "Nothing to save. Use /save docx \"content\" or ask the model first, then run /save docx.", Color.FromArgb(190, 40, 40));
                return;
            }

            if (!SaveTextAsFormat(content, format, true))
                AppendChat("Error", "File generation failed. Check write/export permission.", Color.FromArgb(190, 40, 40));
        }

        string FlagOrArgs(Tools.ParsedCommand parsed, string firstFlag, string secondFlag, string thirdFlag)
        {
            if (!string.IsNullOrEmpty(firstFlag) && parsed.Flags.ContainsKey(firstFlag)) return parsed.Flags[firstFlag];
            if (!string.IsNullOrEmpty(secondFlag) && parsed.Flags.ContainsKey(secondFlag)) return parsed.Flags[secondFlag];
            if (!string.IsNullOrEmpty(thirdFlag) && parsed.Flags.ContainsKey(thirdFlag)) return parsed.Flags[thirdFlag];
            return string.Join(" ", parsed.Args.ToArray()).Trim();
        }

        void RunRemoteCommand(Tools.ParsedCommand parsed)
        {
            string action = parsed.Args.Count > 0 ? parsed.Args[0].ToLowerInvariant() : "";
            if (parsed.Flags.ContainsKey("action")) action = parsed.Flags["action"].ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(action)) action = "check";

            var parameters = new Dictionary<string, object>();
            parameters["action"] = action;
            foreach (var kv in parsed.Flags) parameters[kv.Key] = kv.Value;

            string host = parsed.Flags.ContainsKey("host") ? parsed.Flags["host"] : "";
            string remoteCommand = parsed.Flags.ContainsKey("command") ? parsed.Flags["command"] : "";
            string localPath = parsed.Flags.ContainsKey("localPath") ? parsed.Flags["localPath"] : "";
            string remotePath = parsed.Flags.ContainsKey("remotePath") ? parsed.Flags["remotePath"] : "";

            if ((action == "run" || action == "pull" || action == "push") && string.IsNullOrWhiteSpace(host))
            {
                AppendChat("Error", Tr("Missing host. Example: /remote run host=user@example.com command=\"pwd\"",
                                       "缺少 host。示例：/remote run host=user@example.com command=\"pwd\"",
                                       "缺少 host。範例：/remote run host=user@example.com command=\"pwd\""), Color.FromArgb(190, 40, 40));
                return;
            }
            if (action == "run" && string.IsNullOrWhiteSpace(remoteCommand))
            {
                AppendChat("Error", Tr("Missing command. Example: /remote run host=user@example.com command=\"pwd && ls\"",
                                       "缺少 command。示例：/remote run host=user@example.com command=\"pwd && ls\"",
                                       "缺少 command。範例：/remote run host=user@example.com command=\"pwd && ls\""), Color.FromArgb(190, 40, 40));
                return;
            }
            if (action == "pull" && string.IsNullOrWhiteSpace(remotePath))
            {
                AppendChat("Error", Tr("Missing remotePath for pull.",
                                       "pull 缺少 remotePath。",
                                       "pull 缺少 remotePath。"), Color.FromArgb(190, 40, 40));
                return;
            }
            if (action == "push" && (string.IsNullOrWhiteSpace(localPath) || string.IsNullOrWhiteSpace(remotePath)))
            {
                AppendChat("Error", Tr("Push requires localPath and remotePath.",
                                       "push 需要 localPath 和 remotePath。",
                                       "push 需要 localPath 和 remotePath。"), Color.FromArgb(190, 40, 40));
                return;
            }

            string target = string.IsNullOrWhiteSpace(host) ? "local OpenSSH tools" : host;
            string summary = "Remote " + action + (string.IsNullOrWhiteSpace(remoteCommand) ? "" : ": " + remoteCommand);
            if (!EnsurePermission(Tr("Cloud/network upload", "云端/网络上传", "雲端/網路上傳"), permNetworkUpload, true, "Remote host")) return;

            var remoteGate = PermissionGate.FromJson(permGate.ToJson());
            remoteGate.Set("permNetworkUpload", PermissionLevel.Ask);
            var pipeline = agentPipelineFactory.Create(remoteGate, pluginDir, allowAdvancedPlugins);
            pipeline.RequestApproval = approvalCommand => ShowApprovalCard(
                "RemoteHost",
                Tr("Confirm remote host action", "确认远程主机动作", "確認遠端主機動作"),
                Tr("Execute", "执行", "執行"),
                Tr("Cloud/network upload", "云端/网络上传", "雲端/網路上傳"),
                target,
                Tr("This action connects to a server/cloud host through local OpenSSH. Passwords and private keys are not stored by ZhuaQian.",
                   "该动作会通过本机 OpenSSH 连接服务器/云主机。抓钱不会保存密码或私钥。",
                   "該動作會透過本機 OpenSSH 連線伺服器/雲主機。抓錢不會儲存密碼或私鑰。"),
                summary,
                "Action: " + action + "\r\nHost: " + host + "\r\nCommand: " + remoteCommand + "\r\nLocalPath: " + localPath + "\r\nRemotePath: " + remotePath,
                "");

            var command = new AgentCommand("RemoteHost", "permNetworkUpload", currentTaskId, target, summary, parameters);
            var result = pipeline.Run(command);
            if (result.Status == CommandStatus.Success)
            {
                LogAction("RemoteHost", summary + " -> " + target);
                RecordAction("RemoteHost", "success", summary + " -> " + target, result.ResultPath ?? "");
                AppendChat("ZhuaQian", result.OutputText ?? "Remote action completed.", Color.FromArgb(0, 130, 80));
            }
            else if (result.Status == CommandStatus.Cancelled)
            {
                RecordAction("RemoteHost", "cancelled", summary + " -> " + target, "");
            }
            else
            {
                SetCurrentTaskStatus("failed", "Remote host action failed", true);
                RecordAction("RemoteHost", "failed", result.ErrorMessage, "");
                MessageBox.Show(this, result.ErrorMessage, "Remote host");
            }
        }

        void RunComputerControl(Dictionary<string, object> parameters, string target, string summary)
        {
            if (!EnsurePermission(
                    Tr("Automation/click/keyboard input", "自动化点击/键盘输入", "自動化點擊/鍵盤輸入"),
                    permAutomationInput,
                    true,
                    "Computer Control"))
                return;

            var controlGate = PermissionGate.FromJson(permGate.ToJson());
            controlGate.Set("permAutomationInput", PermissionLevel.Ask);
            var pipeline = agentPipelineFactory.Create(controlGate, pluginDir, allowAdvancedPlugins);
            pipeline.RequestApproval = approvalCommand => ShowApprovalCard(
                "ComputerControl",
                Tr("Confirm computer control", "确认操控电脑", "確認操控電腦"),
                Tr("Execute", "执行", "執行"),
                Tr("Automation/click/keyboard input", "自动化点击/键盘输入", "自動化點擊/鍵盤輸入"),
                target,
                Tr("This action affects the active Windows desktop. Make sure the correct window is focused.",
                   "该动作会影响当前 Windows 桌面。请确认焦点窗口正确。",
                   "該動作會影響目前 Windows 桌面。請確認焦點視窗正確。"),
                summary,
                "Command: " + summary + "\r\nTarget: " + target,
                "");

            var controlCommand = new AgentCommand("ComputerControl", "permAutomationInput", currentTaskId, target, summary, parameters);
            var result = pipeline.Run(controlCommand);
            if (result.Status == CommandStatus.Success)
            {
                LogAction("ComputerControl", summary + " -> " + target);
                RecordAction("ComputerControl", "success", summary + " -> " + target, "");
                AppendChat("ZhuaQian", result.OutputText ?? "Computer control completed.", Color.FromArgb(0, 130, 80));
            }
            else if (result.Status == CommandStatus.Cancelled)
            {
                RecordAction("ComputerControl", "cancelled", summary + " -> " + target, "");
            }
            else if (result.Status == CommandStatus.Denied)
            {
                SetCurrentTaskStatus("needs_input", "Computer control denied", true);
                RecordAction("ComputerControl", "denied", result.ErrorMessage, "");
                MessageBox.Show(this, result.ErrorMessage, "Computer control denied");
            }
            else
            {
                SetCurrentTaskStatus("failed", "Computer control failed", true);
                RecordAction("ComputerControl", "failed", result.ErrorMessage, "");
                MessageBox.Show(this, result.ErrorMessage, "Computer control failed");
            }
        }
    }
}
