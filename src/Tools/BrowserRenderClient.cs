using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace ZhuaQianDesktopApp.Tools
{
    // Options for a single browser-rendered page fetch.
    // Mirrors the controls a research analyst needs: wait for a selector to
    // appear (SPA hydration), extra settle time, headless toggle, and login
    // state persistence via Playwright storage state (cookies + localStorage).
    public sealed class BrowserFetchOptions
    {
        public bool Headless = true;
        public int TimeoutMs = 30000;
        public string WaitForSelector = "";
        public int WaitForTimeoutMs = 0;
        public bool ReturnHtml = false;
        public string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
        public string Viewport = "1280x800";
        public string Locale = "zh-CN";
        public Dictionary<string, string> ExtraHeaders = null;
        // Load a previously saved login session (cookies + localStorage JSON).
        public string UseStorageStatePath = "";
        // Persist the session after navigation so subsequent fetches stay logged in.
        public string SaveStorageStatePath = "";
        // Extra Chromium launch arguments, space separated.
        public string BrowserArgs = "";
    }

    // Thin, product-facing wrapper around a headless Chromium (Playwright for .NET).
    // Produces a WebPageFetchResult so its output drops straight into the existing
    // WebPageReportBuilder pipeline. Every public entry is async; the caller (executor
    // or research fetcher) owns the synchronization boundary.
    //
    // This is the FIRST external NuGet dependency in the project. The raw-csc build
    // (build.ps1 / run-tests.ps1) resolves Microsoft.Playwright.dll + transitive DLLs
    // from the restored packages folder; see docs/patches/BROWSER_RENDER_INTEGRATION.md.
    //
    // NOTE: kept to C# 7.3 (the project's LangVersion) -- disposal is explicit
    // (CloseAsync / DisposeAsync in try/finally) rather than `await using`.
    public sealed class BrowserRenderClient
    {
        readonly WebSearchClient validator;
        // Browsers are downloaded once per process via Playwright.InstallAsync().
        static bool installAttempted;

        public BrowserRenderClient(WebSearchClient validator = null)
        {
            this.validator = validator ?? new WebSearchClient();
        }

        public async Task<WebPageFetchResult> FetchRenderedPageAsync(
            string url,
            BrowserFetchOptions options = null,
            CancellationToken token = default(CancellationToken))
        {
            var result = new WebPageFetchResult { Url = url ?? "" };
            if (options == null) options = new BrowserFetchOptions();

            string validationError;
            if (!validator.ValidatePublicHttpUrl(result.Url, out validationError))
            {
                result.ErrorMessage = validationError;
                return result;
            }

            try
            {
                EnsureBrowsersInstalled();

                IPlaywright playwright = await Playwright.CreateAsync().ConfigureAwait(false);
                try
                {
                    var launchOpts = new BrowserTypeLaunchOptions
                    {
                        Headless = options.Headless,
                        Args = BuildArgs(options),
                    };

                    IBrowser browser = await playwright.Chromium.LaunchAsync(launchOpts).ConfigureAwait(false);
                    try
                    {
                        var ctxOpts = new BrowserNewContextOptions
                        {
                            UserAgent = options.UserAgent,
                            Locale = options.Locale,
                            ViewportSize = ParseViewport(options.Viewport),
                        };
                        if (options.ExtraHeaders != null) ctxOpts.ExtraHttpHeaders = options.ExtraHeaders;
                        if (!string.IsNullOrEmpty(options.UseStorageStatePath) && File.Exists(options.UseStorageStatePath))
                            ctxOpts.StorageStatePath = options.UseStorageStatePath;

                        IBrowserContext context = await browser.NewContextAsync(ctxOpts).ConfigureAwait(false);
                        try
                        {
                            IPage page = await context.NewPageAsync().ConfigureAwait(false);

                            var gotoOpts = new PageGotoOptions
                            {
                                WaitUntil = WaitUntilState.Networkidle,
                                Timeout = options.TimeoutMs,
                            };
                            await page.GotoAsync(result.Url, gotoOpts).ConfigureAwait(false);

                            if (!string.IsNullOrEmpty(options.WaitForSelector))
                                await page.WaitForSelectorAsync(options.WaitForSelector,
                                    new PageWaitForSelectorOptions { Timeout = options.TimeoutMs }).ConfigureAwait(false);

                            if (options.WaitForTimeoutMs > 0)
                                await page.WaitForTimeoutAsync(options.WaitForTimeoutMs).ConfigureAwait(false);

                            result.Title = await page.TitleAsync().ConfigureAwait(false);
                            result.Text = CleanRenderedText(await page.InnerTextAsync("body").ConfigureAwait(false));
                            if (options.ReturnHtml)
                                result.Html = await page.ContentAsync().ConfigureAwait(false);

                            if (!string.IsNullOrEmpty(options.SaveStorageStatePath))
                            {
                                try
                                {
                                    await context.StorageStateAsync(
                                        new BrowserContextStorageStateOptions { Path = options.SaveStorageStatePath })
                                        .ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    result.ErrorMessage = "saved storage state with warning: " + ex.Message;
                                }
                            }

                            result.Success = !string.IsNullOrWhiteSpace(result.Text);
                            return result;
                        }
                        finally
                        {
                            await context.CloseAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        await browser.CloseAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await playwright.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        // Playwright downloads the browser binaries on first use. We attempt it once
        // per process; if the network is unavailable the launch below surfaces a clear
        // "executable doesn't exist" error instead of a silent hang.
        void EnsureBrowsersInstalled()
        {
            if (installAttempted) return;
            installAttempted = true;
            try { Playwright.InstallAsync().GetAwaiter().GetResult(); }
            catch { /* best-effort; launch will report if browsers are still missing */ }
        }

        static IReadOnlyList<string> BuildArgs(BrowserFetchOptions o)
        {
            var args = new List<string> { "--disable-blink-features=AutomationControlled" };
            if (!string.IsNullOrWhiteSpace(o.BrowserArgs))
            {
                foreach (var a in o.BrowserArgs.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    args.Add(a);
            }
            return args;
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

        static string CleanRenderedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t\f\v]+", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\r?\n[ \t]*\r?\n[ \t]*\r?\n+", "\n\n");
            return text.Trim();
        }
    }
}
