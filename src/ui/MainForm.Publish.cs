using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Tools;

namespace ZhuaQianDesktopApp
{
    // Routing + UI glue for two features requested by the user:
    //   * open-source repo publish (GitHub / Gitee / GitLab)  -> PublishRepo executor
    //   * browser login-session persistence (built-in Chromium) -> BrowserControl executor
    // Kept in its own partial so the main file and LocalActionRouting stay within budget.
    public partial class MainForm
    {
        bool TryRoutePublishAndBrowser(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string trimmed = raw.Trim();
            if (trimmed.StartsWith("/"))
            {
                var parsed = new Tools.CommandParser().Parse(trimmed);
                if (!parsed.IsCommand) return false;
                string verb = (parsed.Verb ?? "").ToLowerInvariant();
                if (verb == "publish") { RunPublishCommand(parsed); return true; }
                if (verb == "settoken") { RunSetTokenCommand(parsed); return true; }
                if (verb == "browser") { RunBrowserSessionCommand(parsed); return true; }
                return false;
            }
            string lower = trimmed.ToLowerInvariant();
            if (LooksLikeSetTokenRequest(lower)) { ExecuteSetToken(trimmed); return true; }
            if (LooksLikePublishRequest(lower)) { ExecutePublishOpenSource(trimmed); return true; }
            if (LooksLikeBrowserLoginRequest(lower)) { ExecuteBrowserLoginSession(trimmed); return true; }
            return false;
        }

        bool LooksLikePublishRequest(string lower)
        {
            return ContainsAny(lower,
                "发布到github", "发布到 gitee", "发布到gitlab", "发布开源", "开源发布", "上传到github", "上传开源", "发布仓库", "开源上传", "发布到 git",
                "publish to github", "publish open source", "upload to github", "open source publish", "publish repo");
        }

        bool LooksLikeBrowserLoginRequest(string lower)
        {
            return ContainsAny(lower,
                "登录并保存会话", "登錄並儲存會話", "保存浏览器登录", "儲存瀏覽器登錄", "保存登录态", "保存浏览器会话", "加载浏览器登录", "載入瀏覽器登錄", "浏览器登录", "瀏覽器登錄", "保存会话", "儲存會話",
                "save browser login", "save browser session", "load browser session", "browser login", "restore browser session");
        }

        bool LooksLikeSetTokenRequest(string lower)
        {
            return ContainsAny(lower, "设置 token", "設定 token", "保存 token", "儲存 token", "保存令牌", "儲存令牌", "set token", "save token", "store token");
        }

        // ---- publish (slash: /publish host=github path="C:\proj" repo=name) ----

        void RunPublishCommand(Tools.ParsedCommand parsed)
        {
            string host = parsed.Flags.ContainsKey("host") ? parsed.Flags["host"].ToLowerInvariant() : (parsed.Args.Count > 0 ? parsed.Args[0].ToLowerInvariant() : "github");
            string localPath = parsed.Flags.ContainsKey("path") ? parsed.Flags["path"]
                : (parsed.Flags.ContainsKey("localpath") ? parsed.Flags["localpath"] : (parsed.Args.Count > 1 ? parsed.Args[1] : ""));
            string repoName = parsed.Flags.ContainsKey("repo") ? parsed.Flags["repo"]
                : (parsed.Flags.ContainsKey("name") ? parsed.Flags["name"] : "");
            string description = parsed.Flags.ContainsKey("desc") ? parsed.Flags["desc"]
                : (parsed.Flags.ContainsKey("description") ? parsed.Flags["description"] : "");
            bool isPublic = !parsed.Flags.ContainsKey("private");
            string tokenFlag = parsed.Flags.ContainsKey("token") ? parsed.Flags["token"] : "";
            if (!string.IsNullOrEmpty(tokenFlag))
            {
                try
                {
                    var s = new ConfigStore(configDir); s.Load(); s.Set(host + "_token", tokenFlag); s.Save();
                    AppendChat("ZhuaQian", Tr("Saved PAT for ", "已保存 ", "已儲存 ") + host + Tr(" to local config.", " 到本地配置。", " 到本機設定。"), ThemeManager.Success);
                }
                catch (Exception ex) { AppendChat("Error", "Save token failed: " + ex.Message, ThemeManager.Error); }
            }
            ExecutePublishCore(host, localPath, repoName, description, isPublic,
                "Publish " + host + "/" + (string.IsNullOrEmpty(repoName) ? Path.GetFileName(localPath.TrimEnd('\\', '/')) : repoName));
            input.Clear();
        }

        void ExecutePublishOpenSource(string raw)
        {
            string lower = raw.ToLowerInvariant();
            string host = "github";
            if (ContainsAny(lower, "gitee")) host = "gitee";
            else if (ContainsAny(lower, "gitlab")) host = "gitlab";
            string localPath = ExtractNaturalTarget(raw, new string[] { "发布到github", "发布到 gitee", "发布到gitlab", "发布开源", "开源发布", "上传到github", "上传开源", "发布仓库", "开源上传", "publish to github", "publish open source", "upload to github", "open source publish" });
            localPath = CleanPath(localPath);
            string repoName = Path.GetFileName(localPath.TrimEnd('\\', '/'));
            ExecutePublishCore(host, localPath, repoName, "", true, "Publish " + host + "/" + repoName);
        }

        void ExecutePublishCore(string host, string localPath, string repoName, string description, bool isPublic, string summary)
        {
            if (string.IsNullOrWhiteSpace(localPath) || !Directory.Exists(localPath))
            {
                AppendChat("Error", Tr("Project folder not found. Example: ", "找不到项目文件夹。示例：", "找不到專案資料夾。範例：") + "发布到github \"C:\\path\\project\"", ThemeManager.Error);
                return;
            }
            if (!EnsurePermission(Tr("Cloud/network upload", "云端/网络上传", "雲端/網路上傳"), permNetworkUpload, true, "Publish open-source repo")) return;

            var pubGate = PermissionGate.FromJson(permGate.ToJson());
            pubGate.Set("permNetworkUpload", PermissionLevel.Ask);
            var pipeline = agentPipelineFactory.Create(pubGate, pluginDir, allowAdvancedPlugins);
            pipeline.RequestApproval = approvalCommand => ShowApprovalCard(
                "PublishRepo",
                Tr("Confirm open-source publish", "确认发布开源仓库", "確認發佈開源倉庫"),
                Tr("Publish", "发布", "發佈"),
                Tr("Cloud/network upload", "云端/网络上传", "雲端/網路上傳"),
                repoName,
                Tr("This creates a public repository on " + host + " and pushes your local project. Your PAT is read from local config (sent only to " + host + ").",
                   "这会在 " + host + " 上创建公开仓库并推送你的本地项目。你的 PAT 仅从本地配置读取（只发往 " + host + "）。",
                   "這會在 " + host + " 上建立公開倉庫並推送你的本機專案。你的 PAT 僅從本機設定讀取（只傳往 " + host + "）。"),
                summary,
                "Host: " + host + "\r\nRepo: " + repoName + "\r\nLocal: " + localPath + "\r\nPublic: " + isPublic,
                "");

            var parameters = new Dictionary<string, object>();
            parameters["host"] = host;
            parameters["localPath"] = localPath;
            parameters["repoName"] = repoName;
            parameters["description"] = description;
            parameters["isPublic"] = isPublic;
            var command = new AgentCommand("PublishRepo", "permNetworkUpload", currentTaskId, repoName, summary, parameters);
            var result = pipeline.Run(command);
            if (result.Status == CommandStatus.Success)
            {
                LogAction("PublishRepo", summary);
                RecordAction("PublishRepo", "success", summary, result.ResultPath ?? "");
                AppendChat("ZhuaQian", result.OutputText ?? Tr("Published.", "已发布。", "已發佈。"), ThemeManager.Success);
            }
            else if (result.Status == CommandStatus.Cancelled)
            {
                RecordAction("PublishRepo", "cancelled", summary, "");
            }
            else if (result.Status == CommandStatus.Denied)
            {
                SetCurrentTaskStatus("needs_input", "Publish denied", true);
                RecordAction("PublishRepo", "denied", result.ErrorMessage, "");
                MessageBox.Show(this, result.ErrorMessage, "Publish denied");
            }
            else
            {
                SetCurrentTaskStatus("failed", "Publish failed", true);
                RecordAction("PublishRepo", "failed", result.ErrorMessage, "");
                MessageBox.Show(this, result.ErrorMessage, "Publish failed");
            }
        }

        // ---- token storage (slash: /settoken host=github token=XXX) ----

        void RunSetTokenCommand(Tools.ParsedCommand parsed)
        {
            string host = parsed.Flags.ContainsKey("host") ? parsed.Flags["host"].ToLowerInvariant() : (parsed.Args.Count > 0 ? parsed.Args[0].ToLowerInvariant() : "github");
            string token = parsed.Flags.ContainsKey("token") ? parsed.Flags["token"] : (parsed.Args.Count > 1 ? parsed.Args[1] : "");
            if (string.IsNullOrEmpty(token))
            {
                AppendChat("Error", Tr("Missing token. Example: ", "缺少 token。示例：", "缺少 token。範例：") + "/settoken host=github token=YOUR_TOKEN", ThemeManager.Error);
                input.Clear();
                return;
            }
            try
            {
                var s = new ConfigStore(configDir); s.Load(); s.Set(host + "_token", token); s.Save();
                AppendChat("ZhuaQian", Tr("Saved PAT for ", "已保存 ", "已儲存 ") + host + Tr(" to local config (plaintext, machine-local).", " 到本地配置（明文，仅本机）。", " 到本機設定（明文，僅本機）。"), ThemeManager.Success);
            }
            catch (Exception ex) { AppendChat("Error", "Save token failed: " + ex.Message, ThemeManager.Error); }
            input.Clear();
        }

        void ExecuteSetToken(string raw)
        {
            string lower = raw.ToLowerInvariant();
            string host = "github";
            if (ContainsAny(lower, "gitee")) host = "gitee";
            else if (ContainsAny(lower, "gitlab")) host = "gitlab";
            var m = Regex.Match(raw, @"(?:token|令牌)\s*[:=]?\s*[""']?([A-Za-z0-9_\-]{8,})");
            if (!m.Success)
            {
                AppendChat("Error", Tr("Token not found. Example: ", "没找到 token。示例：", "沒找到 token。範例：") + "设置 github token ghp_xxx", ThemeManager.Error);
                return;
            }
            string token = m.Groups[1].Value;
            try
            {
                var s = new ConfigStore(configDir); s.Load(); s.Set(host + "_token", token); s.Save();
                AppendChat("ZhuaQian", Tr("Saved PAT for ", "已保存 ", "已儲存 ") + host + Tr(" to local config.", " 到本地配置。", " 到本機設定。"), ThemeManager.Success);
            }
            catch (Exception ex) { AppendChat("Error", "Save token failed: " + ex.Message, ThemeManager.Error); }
        }

        // ---- browser login session (slash: /browser login url=... ; /browser savesession name=x ; /browser loadsession name=x) ----

        void RunBrowserSessionCommand(Tools.ParsedCommand parsed)
        {
            string action = parsed.Args.Count > 0 ? parsed.Args[0].ToLowerInvariant() : "login";
            if (parsed.Flags.ContainsKey("action")) action = parsed.Flags["action"].ToLowerInvariant();
            var parameters = new Dictionary<string, object>();
            parameters["action"] = action;
            if (parsed.Flags.ContainsKey("name")) parameters["sessionName"] = parsed.Flags["name"];
            else if (parsed.Args.Count > 1) parameters["sessionName"] = parsed.Args[1];
            if (parsed.Flags.ContainsKey("url")) parameters["url"] = parsed.Flags["url"];
            if (parsed.Flags.ContainsKey("visible")) parameters["visible"] = parsed.Flags["visible"];
            RunBrowserSessionCore(parameters, "browser " + action);
            input.Clear();
        }

        void ExecuteBrowserLoginSession(string text)
        {
            string lower = text.ToLowerInvariant();
            string action = "login";
            if (ContainsAny(lower, "保存", "儲存", "save")) action = "savesession";
            else if (ContainsAny(lower, "加载", "載入", "load")) action = "loadsession";
            string name = "default";
            var m = Regex.Match(text, @"(?:会话|會話|session|命名为|name)\s*[:=]?\s*[""']?([A-Za-z0-9_\-]+)");
            if (m.Success) name = m.Groups[1].Value;
            var parameters = new Dictionary<string, object> { { "action", action }, { "sessionName", name } };
            if (action == "login")
            {
                string url = ExtractNaturalTarget(text, new string[] { "登录", "登錄", "login", "浏览器", "瀏覽器" });
                parameters["url"] = url;
                parameters["visible"] = "true";
            }
            RunBrowserSessionCore(parameters, "browser " + action);
        }

        void RunBrowserSessionCore(Dictionary<string, object> parameters, string summary)
        {
            if (!EnsurePermission(Tr("Cloud/network upload", "云端/网络上传", "雲端/網路上傳"), permNetworkUpload, true, "Browser session")) return;

            var gate = PermissionGate.FromJson(permGate.ToJson());
            gate.Set("permNetworkUpload", PermissionLevel.Ask);
            var pipeline = agentPipelineFactory.Create(gate, pluginDir, allowAdvancedPlugins);
            pipeline.RequestApproval = a => ShowApprovalCard("BrowserControl",
                Tr("Confirm browser session", "确认浏览器会话", "確認瀏覽器會話"),
                Tr("Run", "运行", "執行"),
                Tr("Cloud/network upload", "云端/网络上传", "雲端/網路上傳"),
                summary,
                Tr("Browser navigates / logs in / saves or loads a login session (cookies).",
                   "浏览器将导航/登录/保存或加载登录会话(cookie)。",
                   "瀏覽器將導覽/登入/儲存或載入登入會話(cookie)。"),
                summary, "Action: " + parameters["action"], "");

            var cmd = new AgentCommand("BrowserControl", "permNetworkUpload", currentTaskId, summary, summary, parameters);
            var result = pipeline.Run(cmd);
            if (result.Status == CommandStatus.Success)
                AppendChat("ZhuaQian", result.OutputText ?? Tr("Done.", "完成。", "完成。"), ThemeManager.Success);
            else if (result.Status == CommandStatus.Cancelled)
                RecordAction("BrowserControl", "cancelled", summary, "");
            else
            {
                SetCurrentTaskStatus("failed", "Browser session failed", true);
                RecordAction("BrowserControl", "failed", result.ErrorMessage, "");
                MessageBox.Show(this, result.ErrorMessage, "Browser session");
            }
        }
    }
}
