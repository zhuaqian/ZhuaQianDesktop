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
                    "  /wait 1000",
                    Color.FromArgb(0, 130, 80));
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

            return false;
        }

        bool LooksLikeEndProcessRequest(string lower)
        {
            return ContainsAny(lower, "结束进程", "終止處理程序", "结束 pid", "終止 pid", "kill pid", "end process", "terminate process");
        }

        bool LooksLikeComputerDiagnosisRequest(string lower)
        {
            return ContainsAny(lower,
                "分析电脑", "分析電腦", "诊断电脑", "診斷電腦", "电脑很卡", "電腦很卡", "系统诊断", "系統診斷",
                "电脑问题", "電腦問題", "本机诊断", "本機診斷", "computer diagnostic", "diagnose computer",
                "analyze my computer", "slow computer", "system diagnostic", "what is wrong with my pc");
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
