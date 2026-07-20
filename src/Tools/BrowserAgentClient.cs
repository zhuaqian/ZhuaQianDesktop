using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
#if PLAYWRIGHT
using Microsoft.Playwright;
#endif

namespace ZhuaQianDesktopApp.Tools
{
#if PLAYWRIGHT
    public sealed class BrowserAgentClient
    {
        readonly WebSearchClient validator;
        static bool installAttempted;

        IPlaywright playwright;
        IBrowser browser;
        IBrowserContext context;
        IPage page;

        public bool IsStarted { get { return page != null; } }
        public string CurrentUrl { get { return page != null ? page.Url : ""; } }

        public BrowserAgentClient(WebSearchClient validator = null)
        {
            this.validator = validator ?? new WebSearchClient();
        }

        public async Task<BrowserActionResult> StartAsync(bool headless = true, string viewport = "1280x800", string userAgent = null, string storageStatePath = null, CancellationToken token = default(CancellationToken))
        {
            if (IsStarted) return BrowserActionResult.Success("already started");
            try
            {
                EnsureBrowsersInstalled();
                playwright = await Playwright.CreateAsync().ConfigureAwait(false);
                var launchOpts = new BrowserTypeLaunchOptions { Headless = headless, Args = new[] { "--disable-blink-features=AutomationControlled" } };
                browser = await playwright.Chromium.LaunchAsync(launchOpts).ConfigureAwait(false);

                var ctxOpts = new BrowserNewContextOptions
                {
                    Locale = "zh-CN",
                    ViewportSize = ParseViewport(viewport),
                };
                if (!string.IsNullOrEmpty(userAgent)) ctxOpts.UserAgent = userAgent;
                if (!string.IsNullOrEmpty(storageStatePath) && File.Exists(storageStatePath))
                    ctxOpts.StorageStatePath = storageStatePath;

                context = await browser.NewContextAsync(ctxOpts).ConfigureAwait(false);
                page = await context.NewPageAsync().ConfigureAwait(false);
                return BrowserActionResult.Success("browser started");
            }
            catch (Exception ex)
            {
                return BrowserActionResult.Fail("start failed: " + ex.Message);
            }
        }

        public async Task StopAsync()
        {
            if (page != null) { try { await page.CloseAsync().ConfigureAwait(false); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("BrowserAgentClient close page: " + ex.Message); } page = null; }
            if (context != null) { try { await context.CloseAsync().ConfigureAwait(false); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("BrowserAgentClient close context: " + ex.Message); } context = null; }
            if (browser != null) { try { await browser.CloseAsync().ConfigureAwait(false); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("BrowserAgentClient close browser: " + ex.Message); } browser = null; }
            if (playwright != null) { try { playwright.Dispose(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("BrowserAgentClient dispose playwright: " + ex.Message); } playwright = null; }
        }

        public async Task<string> SnapshotTextAsync(CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return "";
            try { return await page.InnerTextAsync("body").ConfigureAwait(false); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("BrowserAgentClient snapshot text: " + ex.Message); return ""; }
        }

        public async Task<string> GetTitleAsync(CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return "";
            try { return await page.TitleAsync().ConfigureAwait(false); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("BrowserAgentClient title: " + ex.Message); return ""; }
        }

        public async Task<List<DomElement>> DomSnapshotAsync(CancellationToken token = default(CancellationToken))
        {
            var outList = new List<DomElement>();
            if (!IsStarted) return outList;
            try
            {
                var handles = await page.QuerySelectorAllAsync("a,button,input,select,textarea,label").ConfigureAwait(false);
                foreach (var h in handles)
                {
                    try
                    {
                        string tag = (await h.GetAttributeAsync("tagName").ConfigureAwait(false)) ?? "";
                        string id = await h.GetAttributeAsync("id").ConfigureAwait(false) ?? "";
                        string name = await h.GetAttributeAsync("name").ConfigureAwait(false) ?? "";
                        string type = await h.GetAttributeAsync("type").ConfigureAwait(false) ?? "";
                        string text = "";
                        try { text = (await h.InnerTextAsync().ConfigureAwait(false)) ?? ""; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("BrowserAgentClient inner text: " + ex.Message); }
                        string selector = BuildSelector(tag, id, name, type);
                        BoundingBox box = null;
                        try { box = await h.BoundingBoxAsync().ConfigureAwait(false); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("BrowserAgentClient bounding box: " + ex.Message); }
                        var el = new DomElement
                        {
                            Tag = tag.ToLowerInvariant(),
                            Text = (text ?? "").Trim(),
                            Selector = selector,
                        };
                        if (box != null)
                        {
                            el.X = (int)box.X; el.Y = (int)box.Y;
                            el.W = (int)box.Width; el.H = (int)box.Height;
                        }
                        outList.Add(el);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine("BrowserAgentClient collect element: " + ex.Message); }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("BrowserAgentClient observe DOM: " + ex.Message); }
            return outList;
        }

        public async Task<byte[]> ScreenshotAsync(string path = null, CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return new byte[0];
            try
            {
                if (!string.IsNullOrEmpty(path))
                    return await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false }).ConfigureAwait(false);
                return await page.ScreenshotAsync(new PageScreenshotOptions { FullPage = false }).ConfigureAwait(false);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("BrowserAgentClient screenshot: " + ex.Message); return new byte[0]; }
        }

        public async Task<BrowserActionResult> SaveSessionAsync(string path, CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted || context == null) return BrowserActionResult.Fail("not started");
            try
            {
                await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = path }).ConfigureAwait(false);
                return BrowserActionResult.Success("session saved: " + path);
            }
            catch (Exception ex) { return BrowserActionResult.Fail("save session failed: " + ex.Message); }
        }

        public async Task<BrowserActionResult> NavigateAsync(string url, int timeoutMs = 30000, CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return BrowserActionResult.Fail("not started");
            string err;
            if (!validator.ValidatePublicHttpUrl(url, out err))
                return BrowserActionResult.Fail(err);
            try
            {
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Networkidle, Timeout = timeoutMs }).ConfigureAwait(false);
                return BrowserActionResult.Success("navigated to " + url);
            }
            catch (Exception ex) { return BrowserActionResult.Fail("navigate failed: " + ex.Message); }
        }

        public async Task<BrowserActionResult> ClickAsync(string selector, int timeoutMs = 10000, CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return BrowserActionResult.Fail("not started");
            try
            {
                await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions { Timeout = timeoutMs, State = WaitForSelectorState.Visible }).ConfigureAwait(false);
                await page.ClickAsync(selector, new PageClickOptions { Timeout = timeoutMs }).ConfigureAwait(false);
                return BrowserActionResult.Success("clicked " + selector);
            }
            catch (Exception ex) { return BrowserActionResult.Fail("click failed: " + ex.Message); }
        }

        public async Task<BrowserActionResult> ClickTextAsync(string text, int timeoutMs = 10000, CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return BrowserActionResult.Fail("not started");
            try
            {
                var locator = page.GetByText(text);
                await locator.First.ClickAsync(new LocatorClickOptions { Timeout = timeoutMs }).ConfigureAwait(false);
                return BrowserActionResult.Success("clicked text: " + text);
            }
            catch (Exception ex) { return BrowserActionResult.Fail("click-text failed: " + ex.Message); }
        }

        public async Task<BrowserActionResult> FillAsync(string selector, string value, int timeoutMs = 10000, CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return BrowserActionResult.Fail("not started");
            try
            {
                await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions { Timeout = timeoutMs }).ConfigureAwait(false);
                await page.FillAsync(selector, value ?? "", new PageFillOptions { Timeout = timeoutMs }).ConfigureAwait(false);
                return BrowserActionResult.Success("filled " + selector);
            }
            catch (Exception ex) { return BrowserActionResult.Fail("fill failed: " + ex.Message); }
        }

        public async Task<BrowserActionResult> TypeAsync(string selector, string text, int timeoutMs = 10000, CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return BrowserActionResult.Fail("not started");
            try
            {
                if (!string.IsNullOrEmpty(selector))
                    await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions { Timeout = timeoutMs }).ConfigureAwait(false);
                var handle = string.IsNullOrEmpty(selector) ? null : await page.QuerySelectorAsync(selector).ConfigureAwait(false);
                if (handle != null) await handle.FocusAsync().ConfigureAwait(false);
                await page.Keyboard.TypeAsync(text ?? "", new KeyboardTypeOptions { Delay = 20 }).ConfigureAwait(false);
                return BrowserActionResult.Success("typed " + (text ?? "").Length + " chars");
            }
            catch (Exception ex) { return BrowserActionResult.Fail("type failed: " + ex.Message); }
        }

        public async Task<BrowserActionResult> PressKeyAsync(string key, CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return BrowserActionResult.Fail("not started");
            try
            {
                await page.Keyboard.PressAsync(key ?? "Enter").ConfigureAwait(false);
                return BrowserActionResult.Success("pressed " + key);
            }
            catch (Exception ex) { return BrowserActionResult.Fail("press failed: " + ex.Message); }
        }

        public async Task<BrowserActionResult> SubmitAsync(string formSelector = "form", CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return BrowserActionResult.Fail("not started");
            try
            {
                await page.PressAsync(formSelector + " input", "Enter").ConfigureAwait(false);
                return BrowserActionResult.Success("submitted " + formSelector);
            }
            catch (Exception ex) { return BrowserActionResult.Fail("submit failed: " + ex.Message); }
        }

        static string BuildSelector(string tag, string id, string name, string type)
        {
            if (!string.IsNullOrEmpty(id)) return tag.ToLowerInvariant() + "#" + id;
            if (!string.IsNullOrEmpty(name)) return tag.ToLowerInvariant() + "[name=\"" + name + "\"]";
            if (!string.IsNullOrEmpty(type)) return tag.ToLowerInvariant() + "[type=\"" + type + "\"]";
            return tag.ToLowerInvariant();
        }

        static ViewportSize ParseViewport(string v)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                var parts = v.Split('x');
                int w, h;
                if (parts.Length == 2 && int.TryParse(parts[0], out w) && int.TryParse(parts[1], out h) && w > 0 && h > 0)
                    return new ViewportSize { Width = w, Height = h };
            }
            return new ViewportSize { Width = 1280, Height = 800 };
        }

        void EnsureBrowsersInstalled()
        {
            if (installAttempted) return;
            installAttempted = true;
            try { Playwright.InstallAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Playwright install: " + ex.Message); }
        }
    }
#else
    public sealed class BrowserAgentClient
    {
        public bool IsStarted { get { return false; } }
        public string CurrentUrl { get { return ""; } }

        public BrowserAgentClient(WebSearchClient validator = null) { }
        public Task<BrowserActionResult> StartAsync(bool headless = true, string viewport = "1280x800", string userAgent = null, string storageStatePath = null, CancellationToken token = default(CancellationToken))
        {
            return Task.FromResult(BrowserActionResult.Fail("Interactive browser control is not enabled in this raw-csc build. Compile with PLAYWRIGHT in an SDK/MSBuild environment."));
        }
        public Task StopAsync() { return Task.FromResult(0); }
        public Task<string> SnapshotTextAsync(CancellationToken token = default(CancellationToken)) { return Task.FromResult(""); }
        public Task<string> GetTitleAsync(CancellationToken token = default(CancellationToken)) { return Task.FromResult(""); }
        public Task<List<DomElement>> DomSnapshotAsync(CancellationToken token = default(CancellationToken)) { return Task.FromResult(new List<DomElement>()); }
        public Task<byte[]> ScreenshotAsync(string path = null, CancellationToken token = default(CancellationToken)) { return Task.FromResult(new byte[0]); }
        public Task<BrowserActionResult> SaveSessionAsync(string path, CancellationToken token = default(CancellationToken)) { return Task.FromResult(BrowserActionResult.Fail("Interactive browser control is not enabled.")); }
        public Task<BrowserActionResult> NavigateAsync(string url, int timeoutMs = 30000, CancellationToken token = default(CancellationToken)) { return Task.FromResult(BrowserActionResult.Fail("Interactive browser control is not enabled.")); }
        public Task<BrowserActionResult> ClickAsync(string selector, int timeoutMs = 10000, CancellationToken token = default(CancellationToken)) { return Task.FromResult(BrowserActionResult.Fail("Interactive browser control is not enabled.")); }
        public Task<BrowserActionResult> ClickTextAsync(string text, int timeoutMs = 10000, CancellationToken token = default(CancellationToken)) { return Task.FromResult(BrowserActionResult.Fail("Interactive browser control is not enabled.")); }
        public Task<BrowserActionResult> FillAsync(string selector, string value, int timeoutMs = 10000, CancellationToken token = default(CancellationToken)) { return Task.FromResult(BrowserActionResult.Fail("Interactive browser control is not enabled.")); }
        public Task<BrowserActionResult> TypeAsync(string selector, string text, int timeoutMs = 10000, CancellationToken token = default(CancellationToken)) { return Task.FromResult(BrowserActionResult.Fail("Interactive browser control is not enabled.")); }
        public Task<BrowserActionResult> PressKeyAsync(string key, CancellationToken token = default(CancellationToken)) { return Task.FromResult(BrowserActionResult.Fail("Interactive browser control is not enabled.")); }
        public Task<BrowserActionResult> SubmitAsync(string formSelector = "form", CancellationToken token = default(CancellationToken)) { return Task.FromResult(BrowserActionResult.Fail("Interactive browser control is not enabled.")); }
    }
#endif

    public sealed class BrowserActionResult
    {
        public bool Ok;
        public string Detail;
        public static BrowserActionResult Success(string detail) { return new BrowserActionResult { Ok = true, Detail = detail ?? "" }; }
        public static BrowserActionResult Fail(string detail) { return new BrowserActionResult { Ok = false, Detail = detail ?? "" }; }
    }

    public sealed class DomElement
    {
        public string Tag = "";
        public string Text = "";
        public string Selector = "";
        public int X, Y, W, H;
        public bool HasBox { get { return W > 0 && H > 0; } }
    }

    // One shared, long-lived browser client across pipeline runs, so a "login"
    // action and a later "savesession" / "loadsession" action operate on the same
    // interactive session (otherwise each pipeline.Run would get a brand-new client
    // and the cookies would be lost between commands).
    public static class BrowserSessionHub
    {
        static BrowserAgentClient _client;
        static readonly WebSearchClient _validator = new WebSearchClient();

        public static BrowserAgentClient Client
        {
            get
            {
                if (_client == null) _client = new BrowserAgentClient(_validator);
                return _client;
            }
        }

        public static void Reset()
        {
            if (_client != null)
            {
                try { _client.StopAsync().GetAwaiter().GetResult(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("BrowserSessionHub.Reset: " + ex.Message); }
                _client = null;
            }
        }
    }
}
