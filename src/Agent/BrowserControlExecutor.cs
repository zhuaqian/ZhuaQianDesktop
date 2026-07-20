using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZhuaQianDesktopApp.Tools;

namespace ZhuaQianDesktopApp.Agent
{
    // Agent command that drives a real, interactive browser session to complete
    // work (navigate, click, fill forms, press keys, submit, screenshot, read DOM).
    // Goes through the same permission pipeline as BrowserFetch (permNetworkUpload).
    //
    // One BrowserAgentClient is held per executor instance = one interactive
    // session. Production should key sessions by taskId; for the closed-loop demo
    // a single shared session is sufficient. Implements IAsyncCommandExecutor.
    public sealed class BrowserControlExecutor : IAsyncCommandExecutor
    {
        readonly BrowserAgentClient client;
        readonly string screenshotDir;
        readonly string sessionDir;

        public BrowserControlExecutor(BrowserAgentClient client = null, string screenshotDir = null, string sessionDir = null)
        {
            // Use the shared session hub so login + savesession/loadsession share one browser.
            this.client = client ?? BrowserSessionHub.Client;
            this.screenshotDir = screenshotDir ?? Path.Combine(Path.GetTempPath(), "zq-browser-shots");
            this.sessionDir = sessionDir ?? Path.Combine(Path.GetTempPath(), "zq-browser-sessions");
        }

        public string CommandType { get { return "BrowserControl"; } }

        public CommandResult Execute(IAgentCommand command)
        {
            try { return ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex) { return CommandResult.Failed(ex.Message); }
        }

        public async Task<CommandResult> ExecuteAsync(IAgentCommand command, CancellationToken token)
        {
            string action = GetStr(command, "action").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(action)) action = "navigate";

            switch (action)
            {
                case "start":
                {
                    bool headless = !IsTruthy(GetObj(command, "visible"));
                    var r = await client.StartAsync(headless, GetStr(command, "viewport"), null, GetStr(command, "storageState"), token).ConfigureAwait(false);
                    return r.Ok ? CommandResult.Ok(null, false, null, "browser-control", 0, r.Detail)
                                : CommandResult.Failed(r.Detail);
                }
                case "stop":
                    await client.StopAsync().ConfigureAwait(false);
                    return CommandResult.Ok(null, false, null, "browser-control", 0, "browser stopped");

                case "navigate":
                {
                    string url = FirstNonEmpty(GetStr(command, "url"), command.Target);
                    if (string.IsNullOrWhiteSpace(url)) return CommandResult.Failed("navigate url is empty");
                    int t; if (!int.TryParse(GetStr(command, "timeoutMs"), out t)) t = 30000;
                    var r = await client.NavigateAsync(url, t, token).ConfigureAwait(false);
                    return r.Ok ? CommandResult.Ok(null, false, null, "browser-control", 0, r.Detail) : CommandResult.Failed(r.Detail);
                }
                case "click":
                {
                    string sel = GetStr(command, "selector");
                    if (string.IsNullOrWhiteSpace(sel)) return CommandResult.Failed("click missing selector");
                    var r = await client.ClickAsync(sel, GetTimeout(command, 10000), token).ConfigureAwait(false);
                    return r.Ok ? CommandResult.Ok(null, false, null, "browser-control", 0, r.Detail) : CommandResult.Failed(r.Detail);
                }
                case "clicktext":
                {
                    string txt = GetStr(command, "text");
                    if (string.IsNullOrWhiteSpace(txt)) return CommandResult.Failed("clicktext missing text");
                    var r = await client.ClickTextAsync(txt, GetTimeout(command, 10000), token).ConfigureAwait(false);
                    return r.Ok ? CommandResult.Ok(null, false, null, "browser-control", 0, r.Detail) : CommandResult.Failed(r.Detail);
                }
                case "fill":
                {
                    string sel = GetStr(command, "selector");
                    string val = GetStr(command, "value");
                    if (string.IsNullOrWhiteSpace(sel)) return CommandResult.Failed("fill missing selector");
                    var r = await client.FillAsync(sel, val, GetTimeout(command, 10000), token).ConfigureAwait(false);
                    return r.Ok ? CommandResult.Ok(null, false, null, "browser-control", 0, r.Detail) : CommandResult.Failed(r.Detail);
                }
                case "type":
                {
                    string sel = GetStr(command, "selector");
                    string txt = GetStr(command, "text");
                    if (string.IsNullOrWhiteSpace(txt)) return CommandResult.Failed("type missing text");
                    var r = await client.TypeAsync(sel, txt, GetTimeout(command, 10000), token).ConfigureAwait(false);
                    return r.Ok ? CommandResult.Ok(null, false, null, "browser-control", 0, r.Detail) : CommandResult.Failed(r.Detail);
                }
                case "press":
                {
                    string key = FirstNonEmpty(GetStr(command, "key"), command.Target, "Enter");
                    var r = await client.PressKeyAsync(key, token).ConfigureAwait(false);
                    return r.Ok ? CommandResult.Ok(null, false, null, "browser-control", 0, r.Detail) : CommandResult.Failed(r.Detail);
                }
                case "submit":
                {
                    var r = await client.SubmitAsync(GetStr(command, "form") ?? "form", token).ConfigureAwait(false);
                    return r.Ok ? CommandResult.Ok(null, false, null, "browser-control", 0, r.Detail) : CommandResult.Failed(r.Detail);
                }
                case "screenshot":
                {
                    try
                    {
                        Directory.CreateDirectory(screenshotDir);
                        string path = Path.Combine(screenshotDir, "shot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                        byte[] png = await client.ScreenshotAsync(path, token).ConfigureAwait(false);
                        return CommandResult.Ok(path, false, null, "browser-control", png.Length, "screenshot saved: " + path);
                    }
                    catch (Exception ex) { return CommandResult.Failed("screenshot failed: " + ex.Message); }
                }
                case "dom":
                {
                    var els = await client.DomSnapshotAsync(token).ConfigureAwait(false);
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("DOM snapshot (" + els.Count + " elements):");
                    foreach (var e in els)
                        sb.AppendLine("  " + e.Tag + " | " + (e.Text.Length > 40 ? e.Text.Substring(0, 40) + "..." : e.Text) + " | " + e.Selector + (e.HasBox ? " @(" + e.X + "," + e.Y + " " + e.W + "x" + e.H + ")" : ""));
                    return CommandResult.Ok(null, false, null, "browser-control", 0, sb.ToString().Trim());
                }
                case "title":
                    return CommandResult.Ok(null, false, null, "browser-control", 0, "title: " + await client.GetTitleAsync(token).ConfigureAwait(false));
                case "url":
                    return CommandResult.Ok(null, false, null, "browser-control", 0, "url: " + client.CurrentUrl);
                case "text":
                    return CommandResult.Ok(null, false, null, "browser-control", 0, await client.SnapshotTextAsync(token).ConfigureAwait(false));
                case "login":
                {
                    bool visible = IsTruthy(GetObj(command, "visible"));
                    string url = FirstNonEmpty(GetStr(command, "url"), command.Target);
                    if (!client.IsStarted)
                    {
                        var r = await client.StartAsync(!visible, GetStr(command, "viewport"), null, null, token).ConfigureAwait(false);
                        if (!r.Ok) return CommandResult.Failed(r.Detail);
                    }
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        var r = await client.NavigateAsync(url, GetTimeout(command, 30000), token).ConfigureAwait(false);
                        if (!r.Ok) return CommandResult.Failed(r.Detail);
                        return CommandResult.Ok(null, false, null, "browser-control", 0, "Opened " + url + " (visible=" + visible + "). Log in manually, then run: /browser savesession name=<session>");
                    }
                    return CommandResult.Ok(null, false, null, "browser-control", 0, "Browser started (visible=" + visible + "). Navigate then log in, then run: /browser savesession name=<session>");
                }
                case "savesession":
                {
                    string name = FirstNonEmpty(GetStr(command, "sessionName"), GetStr(command, "name"), "default");
                    Directory.CreateDirectory(sessionDir);
                    string path = Path.Combine(sessionDir, SanitizeName(name) + ".json");
                    var r = await client.SaveSessionAsync(path, token).ConfigureAwait(false);
                    return r.Ok ? CommandResult.Ok(path, false, null, "browser-control", 0, "Login session saved: " + path) : CommandResult.Failed(r.Detail);
                }
                case "loadsession":
                {
                    string name = FirstNonEmpty(GetStr(command, "sessionName"), GetStr(command, "name"), "default");
                    string path = Path.Combine(sessionDir, SanitizeName(name) + ".json");
                    if (!File.Exists(path)) return CommandResult.Failed("No saved session named '" + name + "' at " + path);
                    bool visible = IsTruthy(GetObj(command, "visible"));
                    var r = await client.StartAsync(!visible, GetStr(command, "viewport"), null, path, token).ConfigureAwait(false);
                    return r.Ok ? CommandResult.Ok(null, false, null, "browser-control", 0, "Loaded session '" + name + "'. Browser restored with saved cookies.") : CommandResult.Failed(r.Detail);
                }
                default:
                    return CommandResult.Failed("unsupported browser control action: " + action);
            }
        }

        static string GetStr(IAgentCommand c, string key)
        {
            object v; if (c.Parameters != null && c.Parameters.TryGetValue(key, out v) && v != null) return v.ToString();
            return "";
        }
        static object GetObj(IAgentCommand c, string key)
        {
            object v; if (c.Parameters != null && c.Parameters.TryGetValue(key, out v)) return v;
            return null;
        }
        static string FirstNonEmpty(params string[] vals) { foreach (var s in vals) if (!string.IsNullOrWhiteSpace(s)) return s; return ""; }
        static int GetTimeout(IAgentCommand c, int def) { int t; return int.TryParse(GetStr(c, "timeoutMs"), out t) ? t : def; }
        static bool IsTruthy(object v) { return v != null && (v.ToString().Trim().ToLowerInvariant() == "true" || v.ToString() == "1" || v.ToString().ToLowerInvariant() == "yes"); }
        static string SanitizeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "default";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            string r = sb.ToString();
            return r.Length == 0 ? "default" : r;
        }
    }
}
