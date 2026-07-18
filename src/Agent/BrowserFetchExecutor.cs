using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZhuaQianDesktopApp.Tools;

namespace ZhuaQianDesktopApp.Agent
{
    // Agent command that reads a JavaScript-rendered, login-gated, or
    // anti-scraping web page through a real headless browser (Playwright).
    // Goes through the same permission pipeline as WebSearch (permNetworkUpload).
    //
    // Implements IAsyncCommandExecutor because the work is inherently async; a
    // synchronous shell is provided so the pipeline's sync Run() path also works.
    public sealed class BrowserFetchExecutor : IAsyncCommandExecutor
    {
        readonly BrowserRenderClient client;

        public BrowserFetchExecutor(BrowserRenderClient client)
        {
            this.client = client ?? new BrowserRenderClient();
        }

        public string CommandType { get { return "BrowserFetch"; } }

        public CommandResult Execute(IAgentCommand command)
        {
            try
            {
                return ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return CommandResult.Failed(ex.Message);
            }
        }

        public async Task<CommandResult> ExecuteAsync(IAgentCommand command, CancellationToken token)
        {
            string url = command.Target ?? "";
            object v;
            if (string.IsNullOrWhiteSpace(url)
                && command.Parameters != null
                && command.Parameters.TryGetValue("url", out v)
                && v != null)
            {
                url = v.ToString();
            }

            if (string.IsNullOrWhiteSpace(url))
                return CommandResult.Failed("browser fetch url is empty");

            var opts = new BrowserFetchOptions();
            if (command.Parameters != null)
            {
                if (command.Parameters.TryGetValue("timeoutMs", out v) && v != null) { int t; if (int.TryParse(v.ToString(), out t)) opts.TimeoutMs = t; }
                if (command.Parameters.TryGetValue("waitForSelector", out v) && v != null) opts.WaitForSelector = v.ToString();
                if (command.Parameters.TryGetValue("waitForTimeoutMs", out v) && v != null) { int w; if (int.TryParse(v.ToString(), out w)) opts.WaitForTimeoutMs = w; }
                if (command.Parameters.TryGetValue("returnHtml", out v) && v != null) opts.ReturnHtml = IsTruthy(v);
                if (command.Parameters.TryGetValue("headless", out v) && v != null) opts.Headless = IsTruthy(v);
                if (command.Parameters.TryGetValue("userAgent", out v) && v != null) opts.UserAgent = v.ToString();
                if (command.Parameters.TryGetValue("viewport", out v) && v != null) opts.Viewport = v.ToString();
                if (command.Parameters.TryGetValue("useStorageState", out v) && v != null) opts.UseStorageStatePath = v.ToString();
                if (command.Parameters.TryGetValue("saveStorageState", out v) && v != null) opts.SaveStorageStatePath = v.ToString();
            }

            WebPageFetchResult page = await client.FetchRenderedPageAsync(url, opts, token).ConfigureAwait(false);
            if (!page.Success)
                return CommandResult.Failed("browser fetch failed: " + (string.IsNullOrEmpty(page.ErrorMessage) ? "unknown error" : page.ErrorMessage));

            var sb = new StringBuilder();
            sb.AppendLine("Browser-rendered page: " + url);
            if (!string.IsNullOrWhiteSpace(page.Title)) sb.AppendLine("Title: " + page.Title);
            sb.AppendLine("Rendered text length: " + (page.Text ?? "").Length.ToString() + " chars");
            sb.AppendLine();
            sb.AppendLine(page.Text ?? "");
            return CommandResult.Ok(null, false, null, "browser-fetch", (page.Text ?? "").Length, sb.ToString().Trim());
        }

        static bool IsTruthy(object v)
        {
            string s = v.ToString().Trim().ToLowerInvariant();
            return s == "true" || s == "1" || s == "yes" || s == "y";
        }
    }
}
