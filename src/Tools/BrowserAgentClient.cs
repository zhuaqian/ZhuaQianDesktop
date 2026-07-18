using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace ZhuaQianDesktopApp.Tools
{
    // A single interactive browser session the agent can drive to *complete work*
    // (not just fetch). Unlike BrowserRenderClient (one-shot read), this holds a
    // persistent IBrowserContext + IPage so multi-step tasks retain scroll
    // position, form state, and login cookies across actions.
    //
    // Every method is best-effort: failures return a BrowserActionResult with the
    // error instead of throwing, so the task loop can decide what to do next.
    //
    // C# 7.3: explicit disposal via CloseAsync in StopAsync (no `await using`).
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
            if (IsStarted) return BrowserActionResult.Ok("already started");
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
                return BrowserActionResult.Ok("browser started");
            }
            catch (Exception ex)
            {
                return BrowserActionResult.Fail("start failed: " + ex.Message);
            }
        }

        public async Task StopAsync()
        {
            if (page != null) { try { await page.CloseAsync().ConfigureAwait(false); } catch { } page = null; }
            if (context != null) { try { await context.CloseAsync().ConfigureAwait(false); } catch { } context = null; }
            if (browser != null) { try { await browser.CloseAsync().ConfigureAwait(false); } catch { } browser = null; }
            if (playwright != null) { try { playwright.Dispose(); } catch { } playwright = null; }
        }

        // ---- Observe ---------------------------------------------------------

        public async Task<string> SnapshotTextAsync(CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return "";
            try { return await page.InnerTextAsync("body").ConfigureAwait(false); }
            catch { return ""; }
        }

        public async Task<string> GetTitleAsync(CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return "";
            try { return await page.TitleAsync().ConfigureAwait(false); }
            catch { return ""; }
        }

        // Structured DOM snapshot: every interactive element with a stable
        // CSS selector + bounding box so a policy can ground "click the login
        // button" to a concrete action.
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
                        try { text = (await h.InnerTextAsync().ConfigureAwait(false)) ?? ""; } catch { }
                        string selector = BuildSelector(tag, id, name, type);
                        BoundingBox box = null;
                        try { box = await h.BoundingBoxAsync().ConfigureAwait(false); } catch { }
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
                    catch { }
                }
            }
            catch { }
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
            catch { return new byte[0]; }
        }

        // ---- Act -------------------------------------------------------------

        public async Task<BrowserActionResult> NavigateAsync(string url, int timeoutMs = 30000, CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return BrowserActionResult.Fail("not started");
            string err;
            if (!validator.ValidatePublicHttpUrl(url, out err))
                return BrowserActionResult.Fail(err);
            try
            {
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Networkidle, Timeout = timeoutMs }).ConfigureAwait(false);
                return BrowserActionResult.Ok("navigated to " + url);
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
                return BrowserActionResult.Ok("clicked " + selector);
            }
            catch (Exception ex) { return BrowserActionResult.Fail("click failed: " + ex.Message); }
        }

        // Click by visible text (e.g. "Sign in"). Uses Playwright's text locator.
        public async Task<BrowserActionResult> ClickTextAsync(string text, int timeoutMs = 10000, CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return BrowserActionResult.Fail("not started");
            try
            {
                var locator = page.GetByText(text);
                await locator.First.ClickAsync(new LocatorClickOptions { Timeout = timeoutMs }).ConfigureAwait(false);
                return BrowserActionResult.Ok("clicked text: " + text);
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
                return BrowserActionResult.Ok("filled " + selector);
            }
            catch (Exception ex) { return BrowserActionResult.Fail("fill failed: " + ex.Message); }
        }

        // Type into the focused element (or a selector) char-by-char; use for
        // fields that fire key handlers on input.
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
                return BrowserActionResult.Ok("typed " + (text ?? "").Length + " chars");
            }
            catch (Exception ex) { return BrowserActionResult.Fail("type failed: " + ex.Message); }
        }

        public async Task<BrowserActionResult> PressKeyAsync(string key, CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return BrowserActionResult.Fail("not started");
            try
            {
                await page.Keyboard.PressAsync(key ?? "Enter").ConfigureAwait(false);
                return BrowserActionResult.Ok("pressed " + key);
            }
            catch (Exception ex) { return BrowserActionResult.Fail("press failed: " + ex.Message); }
        }

        public async Task<BrowserActionResult> SubmitAsync(string formSelector = "form", CancellationToken token = default(CancellationToken))
        {
            if (!IsStarted) return BrowserActionResult.Fail("not started");
            try
            {
                await page.PressAsync(formSelector + " input", "Enter").ConfigureAwait(false);
                return BrowserActionResult.Ok("submitted " + formSelector);
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
            catch { /* launch will surface a clear error if still missing */ }
        }
    }

    public sealed class BrowserActionResult
    {
        public bool Ok;
        public string Detail;
        public static BrowserActionResult Ok(string detail) { return new BrowserActionResult { Ok = true, Detail = detail ?? "" }; }
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
}
