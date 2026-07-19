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

namespace ZhuaQianDesktopApp
{
    // Settings / config / permission / provider-connection-test / localization
    // members of MainForm, extracted from the oversized ZhuaQianDesktop.cs to
    // keep the main file within its line budget. Pure move (no logic change);
    // all members belong to the same partial class and access shared fields directly.
    public partial class MainForm
    {
        void LoadConfig()
        {
            Directory.CreateDirectory(configDir);
            if (!File.Exists(configPath)) return;
            try
            {
                var cfg = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(configPath, Encoding.UTF8));
                if (cfg.ContainsKey("apiKeyProtected")) apiKey = UnprotectSecret(Convert.ToString(cfg["apiKeyProtected"]));
                else if (cfg.ContainsKey("apiKey")) apiKey = Convert.ToString(cfg["apiKey"]);
                if (cfg.ContainsKey("model")) model = Convert.ToString(cfg["model"]);
                if (cfg.ContainsKey("provider")) provider = Convert.ToString(cfg["provider"]);
                if (cfg.ContainsKey("openRouterApiKeyProtected")) openRouterApiKey = UnprotectSecret(Convert.ToString(cfg["openRouterApiKeyProtected"]));
                else if (cfg.ContainsKey("openRouterApiKey")) openRouterApiKey = Convert.ToString(cfg["openRouterApiKey"]);
                if (cfg.ContainsKey("openRouterModel")) openRouterModel = Convert.ToString(cfg["openRouterModel"]);
                if (cfg.ContainsKey("localApiUrl")) localApiUrl = Convert.ToString(cfg["localApiUrl"]);
                if (cfg.ContainsKey("localModel")) localModel = Convert.ToString(cfg["localModel"]);
                if (cfg.ContainsKey("embeddingModel")) embeddingModel = Convert.ToString(cfg["embeddingModel"]);
                if (cfg.ContainsKey("relayUrl")) relayUrl = Convert.ToString(cfg["relayUrl"]);
                if (cfg.ContainsKey("pluginDir")) pluginDir = Convert.ToString(cfg["pluginDir"]);
                if (cfg.ContainsKey("uiLanguage")) uiLanguage = Convert.ToString(cfg["uiLanguage"]);
                if (cfg.ContainsKey("workMode")) workMode = Convert.ToString(cfg["workMode"]);
                if (cfg.ContainsKey("enableHotkey")) enableHotkey = Convert.ToBoolean(cfg["enableHotkey"]);
                if (cfg.ContainsKey("computerControlEnabled")) computerControlEnabled = Convert.ToBoolean(cfg["computerControlEnabled"]);
                if (cfg.ContainsKey("allowAdvancedPlugins")) allowAdvancedPlugins = Convert.ToBoolean(cfg["allowAdvancedPlugins"]);
                if (cfg.ContainsKey("permFileRead")) permFileRead = Convert.ToBoolean(cfg["permFileRead"]);
                if (cfg.ContainsKey("permFileWrite")) permFileWrite = Convert.ToBoolean(cfg["permFileWrite"]);
                if (cfg.ContainsKey("permFileMoveDelete")) permFileMoveDelete = Convert.ToBoolean(cfg["permFileMoveDelete"]);
                if (cfg.ContainsKey("permProcessManage")) permProcessManage = Convert.ToBoolean(cfg["permProcessManage"]);
                if (cfg.ContainsKey("permPluginRun")) permPluginRun = Convert.ToBoolean(cfg["permPluginRun"]);
                if (cfg.ContainsKey("permScreenshot")) permScreenshot = Convert.ToBoolean(cfg["permScreenshot"]);
                if (cfg.ContainsKey("permClipboard")) permClipboard = Convert.ToBoolean(cfg["permClipboard"]);
                if (cfg.ContainsKey("permNetworkUpload")) permNetworkUpload = Convert.ToBoolean(cfg["permNetworkUpload"]);
                if (cfg.ContainsKey("permAutomationInput")) permAutomationInput = Convert.ToBoolean(cfg["permAutomationInput"]);
                if (cfg.ContainsKey("currentInfoMode")) currentInfoMode = Convert.ToBoolean(cfg["currentInfoMode"]);
                if (cfg.ContainsKey("redactSensitive")) redactSensitive = Convert.ToBoolean(cfg["redactSensitive"]);
                if (cfg.ContainsKey("autoMode")) autoMode = Convert.ToBoolean(cfg["autoMode"]);
                if (cfg.ContainsKey("allowedDirs"))
                {
                    allowedDirs.Clear();
                    foreach (var d in Convert.ToString(cfg["allowedDirs"]).Split('|'))
                        if (!string.IsNullOrWhiteSpace(d)) allowedDirs.Add(d);
                }

                // Build the gate baseline from the boolean switches, then load any
                // stored fine-grained gate (patterns / auto / dirs) on top.
                permGate.Set("permFileRead", permFileRead ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permFileWrite", permFileWrite ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permFileMoveDelete", permFileMoveDelete ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permProcessManage", permProcessManage ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permPluginRun", permPluginRun ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permScreenshot", permScreenshot ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permClipboard", permClipboard ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permNetworkUpload", permNetworkUpload ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permAutomationInput", permAutomationInput ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.AutoMode = autoMode;
                permGate.AllowedDirectories = new List<string>(allowedDirs);

                if (cfg.ContainsKey("permGateJson"))
                {
                    var loaded = PermissionGate.FromJson(Convert.ToString(cfg["permGateJson"]));
                    if (loaded != null)
                    {
                        permGate = loaded;
                        autoMode = permGate.AutoMode;
                        allowedDirs = new List<string>(permGate.AllowedDirectories);
                    }
                }
                ApplyBooleanPermissionsToGate();
                if (string.IsNullOrWhiteSpace(model)) model = DefaultModel;
                if (string.IsNullOrWhiteSpace(provider)) provider = DefaultProvider;
                if (string.IsNullOrWhiteSpace(openRouterModel)) openRouterModel = DefaultOpenRouterModel;
                if (string.IsNullOrWhiteSpace(localApiUrl)) localApiUrl = DefaultLocalApiUrl;
                if (string.IsNullOrWhiteSpace(localModel)) localModel = DefaultLocalModel;
                if (string.IsNullOrWhiteSpace(embeddingModel)) embeddingModel = "nomic-embed-text";
                if (string.IsNullOrWhiteSpace(uiLanguage)) uiLanguage = "zh-Hans";
                if (string.IsNullOrWhiteSpace(workMode)) workMode = "Ask";

                // Sync legacy fields -> ProviderManager
                providerManager.GeminiKey = apiKey;
                providerManager.OpenRouterKey = openRouterApiKey;
                providerManager.LocalApiUrl = localApiUrl;
                providerManager.EmbeddingModel = embeddingModel;
                if (cfg.ContainsKey("tencentKeyProtected")) providerManager.TencentKey = UnprotectSecret(Convert.ToString(cfg["tencentKeyProtected"]));
                else if (cfg.ContainsKey("tencentKey")) providerManager.TencentKey = Convert.ToString(cfg["tencentKey"]);
                if (cfg.ContainsKey("alibabaKeyProtected")) providerManager.AlibabaKey = UnprotectSecret(Convert.ToString(cfg["alibabaKeyProtected"]));
                else if (cfg.ContainsKey("alibabaKey")) providerManager.AlibabaKey = Convert.ToString(cfg["alibabaKey"]);
                if (cfg.ContainsKey("zhipuKeyProtected")) providerManager.ZhipuKey = UnprotectSecret(Convert.ToString(cfg["zhipuKeyProtected"]));
                else if (cfg.ContainsKey("zhipuKey")) providerManager.ZhipuKey = Convert.ToString(cfg["zhipuKey"]);
                var found = ModelRegistry.ByProvider(provider).Find(m => m.Id == model);
                if (found != null) providerManager.SelectModel(found);
                else if (provider == "OpenRouter")
                {
                    found = ModelRegistry.ByProvider("OpenRouter").Find(m => m.Id == openRouterModel);
                    if (found != null) providerManager.SelectModel(found);
                }
                else if (provider == "Local")
                {
                    found = ModelRegistry.Local.Find(m => m.Id == localModel);
                    if (found != null) providerManager.SelectModel(found);
                }
            }
            catch (Exception _ex) { LogAction("Warning", "LoadConfig: " + _ex.Message); }
        }

        void NormalizeModel()
        {
            if (string.Equals(model, "gemini-2.5-flash-latest", StringComparison.OrdinalIgnoreCase))
            {
                model = DefaultModel;
                SaveConfig();
            }
            if (string.Equals(model, "gemini-2.5-flash", StringComparison.OrdinalIgnoreCase))
            {
                model = DefaultModel;
                SaveConfig();
            }
            if (string.Equals(model, "gemini-3.1-flash-lite", StringComparison.OrdinalIgnoreCase))
            {
                model = DefaultModel;
                SaveConfig();
            }
        }

        void SaveConfig()
        {
            Directory.CreateDirectory(configDir);
            ApplyBooleanPermissionsToGate();
            var cfg = new Dictionary<string, object> {
                { "apiKey", "" },
                { "apiKeyProtected", ProtectSecret(apiKey) },
                { "model", model },
                { "provider", provider },
                { "openRouterApiKey", "" },
                { "openRouterApiKeyProtected", ProtectSecret(openRouterApiKey) },
                { "openRouterModel", openRouterModel },
                { "tencentKey", "" },
                { "tencentKeyProtected", ProtectSecret(providerManager.TencentKey) },
                { "alibabaKey", "" },
                { "alibabaKeyProtected", ProtectSecret(providerManager.AlibabaKey) },
                { "zhipuKey", "" },
                { "zhipuKeyProtected", ProtectSecret(providerManager.ZhipuKey) },
                { "localApiUrl", localApiUrl },
                { "localModel", localModel },
                { "embeddingModel", embeddingModel },
                { "pluginDir", pluginDir },
                { "uiLanguage", uiLanguage },
                { "workMode", workMode },
                { "enableHotkey", enableHotkey },
                { "computerControlEnabled", computerControlEnabled },
                { "allowAdvancedPlugins", allowAdvancedPlugins },
                { "permFileRead", permFileRead },
                { "permFileWrite", permFileWrite },
                { "permFileMoveDelete", permFileMoveDelete },
                { "permProcessManage", permProcessManage },
                { "permPluginRun", permPluginRun },
                { "permScreenshot", permScreenshot },
                { "permClipboard", permClipboard },
                { "permNetworkUpload", permNetworkUpload },
                { "permAutomationInput", permAutomationInput },
                { "currentInfoMode", currentInfoMode },
                { "redactSensitive", redactSensitive },
                { "autoMode", autoMode },
                { "allowedDirs", string.Join("|", allowedDirs.ToArray()) },
                { "permGateJson", permGate.ToJson() },
                { "relayUrl", relayUrl }
            };
            File.WriteAllText(configPath, json.Serialize(cfg), Encoding.UTF8);
        }

        void ApplyBooleanPermissionsToGate()
        {
            permGate.Set("permFileRead", permFileRead ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permFileWrite", permFileWrite ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permFileMoveDelete", permFileMoveDelete ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permProcessManage", permProcessManage ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permPluginRun", permPluginRun ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permScreenshot", permScreenshot ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permClipboard", permClipboard ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permNetworkUpload", permNetworkUpload ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permAutomationInput", permAutomationInput ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.AutoMode = autoMode;
            permGate.AllowedDirectories = new List<string>(allowedDirs);
        }

        string ProtectSecret(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try
            {
                byte[] raw = Encoding.UTF8.GetBytes(value);
                byte[] protectedBytes = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
                return "dpapi:" + Convert.ToBase64String(protectedBytes);
            }
            catch (Exception ex)
            {
                LogAction("Warning", "ProtectSecret failed: " + ex.Message);
                return "";
            }
        }

        string UnprotectSecret(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            try
            {
                if (!value.StartsWith("dpapi:", StringComparison.OrdinalIgnoreCase)) return value;
                byte[] protectedBytes = Convert.FromBase64String(value.Substring("dpapi:".Length));
                byte[] raw = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(raw);
            }
            catch (Exception ex)
            {
                LogAction("Warning", "UnprotectSecret failed: " + ex.Message);
                return "";
            }
        }

        string Tr(string en, string zhHans, string zhHant)
        {
            if (string.Equals(uiLanguage, "zh-Hant", StringComparison.OrdinalIgnoreCase)) return zhHant;
            if (string.Equals(uiLanguage, "en", StringComparison.OrdinalIgnoreCase)) return en;
            return zhHans;
        }

        string LanguageDisplay(string code)
        {
            if (string.Equals(code, "zh-Hant", StringComparison.OrdinalIgnoreCase)) return "繁體中文";
            if (string.Equals(code, "en", StringComparison.OrdinalIgnoreCase)) return "English";
            return "简体中文";
        }

        string LanguageCode(string display)
        {
            if (display == "繁體中文") return "zh-Hant";
            if (display == "English") return "en";
            return "zh-Hans";
        }

        string CurrentModelLabel()
        {
            return providerManager.CurrentModelLabel();
        }

        void ShowSettings()
        {
            using (var dlg = new SettingsDialog(providerManager, configPath, Tr, uiLanguage))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    string oldLanguage = uiLanguage;

                    var sel = dlg.SelectedModel;
                    if (sel != null)
                    {
                        provider = sel.Endpoint;
                        model = sel.Id;
                        if (sel.Endpoint == "Local") localModel = sel.Id;
                        providerManager.SelectModel(sel);
                    }

                    apiKey = dlg.GeminiKey;
                    openRouterApiKey = dlg.OpenRouterKey;
                    localApiUrl = dlg.LocalApiUrl;
                    embeddingModel = providerManager.EmbeddingModel;
                    uiLanguage = dlg.SelectedLanguage;
                    providerManager.GeminiKey = dlg.GeminiKey;
                    providerManager.OpenRouterKey = dlg.OpenRouterKey;
                    providerManager.LocalApiUrl = dlg.LocalApiUrl;
                    providerManager.CustomApiUrl = dlg.CustomApiUrl;
                    providerManager.CustomApiKey = dlg.CustomApiKey;
                    providerManager.TencentKey = dlg.TencentKey;
                    providerManager.AlibabaKey = dlg.AlibabaKey;
                    providerManager.ZhipuKey = dlg.ZhipuKey;

                    SaveConfig();
                    PopulateTopModelCombo();
                    RefreshTopModelSwitcher();
                    ApplyHotkeyRegistration();
                    if (!string.Equals(oldLanguage, uiLanguage, StringComparison.OrdinalIgnoreCase))
                        RebuildUiForLanguage();
                }
            }
        }

        void ShowPermissionSettings(IWin32Window owner)
        {
            using (var dlg = new Form())
            {
                dlg.Text = Tr("Permissions", "权限细分", "權限細分");
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Size = new Size(460, 440);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.Font = Font;
                dlg.BackColor = zqPanelBg;

                var intro = new Label
                {
                    Text = Tr("Power is the master switch. These switches decide what local actions are allowed after Power is on.",
                              "Power 是总开关。下面这些开关决定 Power 开启后允许哪些本地动作。",
                              "Power 是總開關。下面這些開關決定 Power 開啟後允許哪些本機動作。"),
                    Location = new Point(16, 16),
                    Size = new Size(410, 42),
                    ForeColor = zqMuted
                };
                var chkRead = new CheckBox { Text = Tr("Read local files", "读取本地文件", "讀取本機檔案"), Location = new Point(24, 70), Width = 380, Checked = permFileRead };
                var chkWrite = new CheckBox { Text = Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"), Location = new Point(24, 100), Width = 380, Checked = permFileWrite };
                var chkMove = new CheckBox { Text = Tr("Move/delete files", "移动/删除文件", "移動/刪除檔案"), Location = new Point(24, 130), Width = 380, Checked = permFileMoveDelete };
                var chkProc = new CheckBox { Text = Tr("End processes", "结束进程", "結束處理程序"), Location = new Point(24, 160), Width = 380, Checked = permProcessManage };
                var chkPlugin = new CheckBox { Text = Tr("Run plugins", "运行插件", "執行外掛"), Location = new Point(24, 190), Width = 380, Checked = permPluginRun };
                var chkShot = new CheckBox { Text = Tr("Take screenshots", "截屏识别", "截圖識別"), Location = new Point(24, 220), Width = 380, Checked = permScreenshot };
                var chkClip = new CheckBox { Text = Tr("Read clipboard monitor", "读取剪贴板监控", "讀取剪貼簿監控"), Location = new Point(24, 250), Width = 380, Checked = permClipboard };
                var chkNetwork = new CheckBox { Text = Tr("Cloud/network upload", "云端/网络上传", "雲端/網路上傳"), Location = new Point(24, 280), Width = 380, Checked = permNetworkUpload };
                var chkAutoInput = new CheckBox { Text = Tr("Automation/click/keyboard input", "自动化点击/键盘输入", "自動化點擊/鍵盤輸入"), Location = new Point(24, 310), Width = 380, Checked = permAutomationInput };
                var chkAutoMode = new CheckBox { Text = Tr("Auto mode (auto-approve Ask permissions)", "自动模式（自动批准 Ask 权限）", "自動模式（自動批准 Ask 權限）"), Location = new Point(24, 340), Width = 380, Checked = autoMode };
                var ok = new Button { Text = Tr("Save", "保存", "儲存"), Location = new Point(260, 385), Width = 76 };
                var cancel = new Button { Text = Tr("Cancel", "取消", "取消"), Location = new Point(348, 355), Width = 76 };
                StyleButton(ok, ZqButtonRole.Primary);
                StyleButton(cancel, ZqButtonRole.Ghost);
                ok.Click += (s, e) =>
                {
                    permFileRead = chkRead.Checked;
                    permFileWrite = chkWrite.Checked;
                    permFileMoveDelete = chkMove.Checked;
                    permProcessManage = chkProc.Checked;
                    permPluginRun = chkPlugin.Checked;
                    permScreenshot = chkShot.Checked;
                    permClipboard = chkClip.Checked;
                    permNetworkUpload = chkNetwork.Checked;
                    permAutomationInput = chkAutoInput.Checked;
                    autoMode = chkAutoMode.Checked;
                    permGate.AutoMode = autoMode;
                    SaveConfig();
                    LogAction("Permissions", "Updated fine-grained permissions");
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                };
                cancel.Click += (s, e) => dlg.Close();
                dlg.Controls.AddRange(new Control[] { intro, chkRead, chkWrite, chkMove, chkProc, chkPlugin, chkShot, chkClip, chkNetwork, chkAutoInput, chkAutoMode, ok, cancel });
                dlg.ShowDialog(owner);
            }
        }

        string TestGeminiConnection(string key, string testModel)
        {
            if (!permNetworkUpload) return "Cloud/network upload permission is off.";
            if (string.IsNullOrWhiteSpace(key)) return "Gemini API Key is empty.";
            try
            {
                var payload = new Dictionary<string, object>
                {
                    { "contents", new ArrayList { new Dictionary<string, object> {
                        { "role", "user" },
                        { "parts", new ArrayList { NewTextPart("Reply with OK.") } }
                    } } },
                    { "generationConfig", new Dictionary<string, object> { { "temperature", 0 }, { "maxOutputTokens", 16 } } }
                };
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    string url = "https://generativelanguage.googleapis.com/v1beta/models/" + Uri.EscapeDataString(testModel) + ":generateContent?key=" + Uri.EscapeDataString(key);
                    wc.UploadString(url, "POST", json.Serialize(payload));
                }
                return "Gemini connection OK.";
            }
            catch (Exception ex)
            {
                return "Gemini test failed:\r\n" + ShortError(ex);
            }
        }

        string TestOpenRouterConnection(string key, string testModel)
        {
            if (!permNetworkUpload) return "Cloud/network upload permission is off.";
            if (string.IsNullOrWhiteSpace(key)) return "OpenRouter API Key is empty.";
            try
            {
                var payload = new Dictionary<string, object> {
                    { "model", string.IsNullOrWhiteSpace(testModel) ? DefaultOpenRouterModel : testModel },
                    { "messages", new ArrayList {
                        new Dictionary<string, object> { { "role", "user" }, { "content", "Reply with OK." } }
                    } },
                    { "temperature", 0 },
                    { "max_tokens", 16 }
                };
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + key;
                    wc.Headers["HTTP-Referer"] = "https://zhuaqian.local";
                    wc.Headers["X-Title"] = "ZhuaQian Desktop";
                    wc.UploadString("https://openrouter.ai/api/v1/chat/completions", "POST", json.Serialize(payload));
                }
                return "OpenRouter connection OK.";
            }
            catch (Exception ex)
            {
                return "OpenRouter test failed:\r\n" + ShortError(ex);
            }
        }

        string TestLocalConnection(string url, string testModel)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(testModel)) return "Local URL/model is empty.";
            try
            {
                var payload = new Dictionary<string, object> {
                    { "model", testModel },
                    { "messages", new ArrayList {
                        new Dictionary<string, object> { { "role", "user" }, { "content", "Reply with OK." } }
                    } },
                    { "stream", false }
                };
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    wc.UploadString(url, "POST", json.Serialize(payload));
                }
                return "Local model connection OK.";
            }
            catch (Exception ex)
            {
                return "Local model test failed:\r\n" + ShortError(ex);
            }
        }

        string TestEmbeddingConnection(string chatUrl, string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return "Embedding model is empty.";
            try
            {
                string url = EmbeddingUrlFromChatUrl(string.IsNullOrWhiteSpace(chatUrl) ? DefaultLocalApiUrl : chatUrl);
                var payload = new Dictionary<string, object> {
                    { "model", modelName },
                    { "prompt", "ZhuaQian embedding test" }
                };
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    string resp = wc.UploadString(url, "POST", json.Serialize(payload));
                    if (!resp.Contains("embedding")) return "Embedding endpoint responded, but no embedding field was found.";
                }
                return "Embedding connection OK.";
            }
            catch (Exception ex)
            {
                return "Embedding test failed:\r\n" + ShortError(ex);
            }
        }

        string EmbeddingUrlFromChatUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "http://localhost:11434/api/embeddings";
            if (url.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
                return url.Substring(0, url.Length - "/api/chat".Length) + "/api/embeddings";
            if (url.EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase))
                return url.Substring(0, url.Length - "/api/generate".Length) + "/api/embeddings";
            return url.TrimEnd('/') + "/embeddings";
        }

        string ShortError(Exception ex)
        {
            var web = ex as WebException;
            if (web != null && web.Response != null)
            {
                try
                {
                    using (var stream = web.Response.GetResponseStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        return TrimForPrompt(reader.ReadToEnd(), 1200);
                }
                catch (Exception _ex) { LogAction("Warning", "ShortError: " + _ex.Message); }
            }
            return TrimForPrompt(ex.Message, 1200);
        }
    }
}
