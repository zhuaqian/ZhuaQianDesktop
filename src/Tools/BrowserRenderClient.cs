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
        public string UseStorageStatePath = "";
        public string SaveStorageStatePath = "";
        public string BrowserArgs = "";
    }

    public sealed class BrowserRenderClient
    {
        readonly WebSearchClient validator;
#if PLAYWRIGHT
        static bool installAttempted;
#endif

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

#if PLAYWRIGHT
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
#else
            await Task.Yield();
            result.ErrorMessage = "Browser rendering is not enabled in this raw-csc build. Use the normal web fetch path or compile with PLAYWRIGHT in an SDK/MSBuild environment.";
            return result;
#endif
        }

#if PLAYWRIGHT
        void EnsureBrowsersInstalled()
        {
            if (installAttempted) return;
            installAttempted = true;
            try { Playwright.InstallAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Playwright install: " + ex.Message); }
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
#endif

        static string CleanRenderedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t\f\v]+", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\r?\n[ \t]*\r?\n[ \t]*\r?\n+", "\n\n");
            return text.Trim();
        }
    }
}
