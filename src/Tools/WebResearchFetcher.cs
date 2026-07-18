using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZhuaQianDesktopApp.Tools
{
    // Bridges the existing static web-research flow and the new browser renderer.
    // For each URL it tries the cheap static HTTP fetch first; only when that yields
    // nothing useful (empty / too short / failed) does it fall back to a full browser
    // render. The result is a WebPageFetchResult, so callers feed it straight into
    // WebPageReportBuilder without changing the report logic.
    public static class WebResearchFetcher
    {
        const int MinRenderedChars = 200;

        public static WebPageFetchResult FetchOne(string url, int maxChars, BrowserRenderClient render, bool allowBrowser)
        {
            try
            {
                return FetchOneAsync(url, maxChars, render, allowBrowser, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return new WebPageFetchResult { Url = url, ErrorMessage = ex.Message };
            }
        }

        public static async Task<WebPageFetchResult> FetchOneAsync(
            string url, int maxChars, BrowserRenderClient render, bool allowBrowser, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(url))
                return new WebPageFetchResult { Url = url, ErrorMessage = "empty url" };

            WebPageFetchResult staticResult = new WebSearchClient().FetchPage(url, maxChars);
            if (staticResult != null && staticResult.Success
                && !string.IsNullOrWhiteSpace(staticResult.Text)
                && staticResult.Text.Length >= MinRenderedChars)
            {
                return staticResult;
            }

            if (render != null && allowBrowser)
            {
                WebPageFetchResult rendered = await render.FetchRenderedPageAsync(url, new BrowserFetchOptions(), token).ConfigureAwait(false);
                if (rendered != null && rendered.Success && !string.IsNullOrWhiteSpace(rendered.Text))
                    return rendered;
                // Render failed but static gave partial text: keep what we have.
                if (staticResult != null && staticResult.Success && !string.IsNullOrWhiteSpace(staticResult.Text))
                    return staticResult;
                return rendered ?? staticResult ?? new WebPageFetchResult { Url = url, ErrorMessage = "both static and browser fetch failed" };
            }

            return staticResult ?? new WebPageFetchResult { Url = url, ErrorMessage = "static fetch failed" };
        }

        public static List<WebPageFetchResult> FetchMany(IEnumerable<string> urls, int maxChars, BrowserRenderClient render, bool allowBrowser)
        {
            var list = new List<WebPageFetchResult>();
            if (urls == null) return list;
            foreach (var u in urls) list.Add(FetchOne(u, maxChars, render, allowBrowser));
            return list;
        }
    }
}
