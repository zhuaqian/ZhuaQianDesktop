using System;
using System.Threading.Tasks;
using ZhuaQianDesktopApp.Tools;

// Exercises the browser-render client without launching a real browser:
//   - invalid URLs are rejected at the validation gate (before any Playwright call)
//   - the static-first / browser-fallback fetcher degrades safely on bad input
//   - option defaults are sane
static class TestBrowserRenderClient
{
    public static int RunAll()
    {
        int failures = 0;
        Console.WriteLine("[BrowserRenderClient]");
        failures += TestInvalidUrlRejected();
        failures += TestFetchOneInvalidUrl();
        failures += TestOptionsDefaults();
        return failures;
    }

    static int TestInvalidUrlRejected()
    {
        var client = new BrowserRenderClient();
        // Validation runs before Playwright, so no browser binary is required here.
        WebPageFetchResult result = client.FetchRenderedPageAsync("not-a-real-url", null).GetAwaiter().GetResult();
        if (result.Success) { Console.WriteLine("  FAIL: invalid url should not be fetched"); return 1; }
        if (string.IsNullOrEmpty(result.ErrorMessage)) { Console.WriteLine("  FAIL: invalid url should report an error"); return 1; }
        Console.WriteLine("  invalid url rejected: " + result.ErrorMessage);
        return 0;
    }

    static int TestFetchOneInvalidUrl()
    {
        // WebResearchFetcher.FetchOne must not throw on garbage; it returns an error result.
        WebPageFetchResult result = WebResearchFetcher.FetchOne("::::bad", 1000, null, true);
        if (result == null) { Console.WriteLine("  FAIL: FetchOne returned null"); return 1; }
        if (result.Success) { Console.WriteLine("  FAIL: FetchOne succeeded on invalid url"); return 1; }
        Console.WriteLine("  FetchOne safe on invalid url");
        return 0;
    }

    static int TestOptionsDefaults()
    {
        var opts = new BrowserFetchOptions();
        if (!opts.Headless) { Console.WriteLine("  FAIL: default headless should be true"); return 1; }
        if (opts.TimeoutMs <= 0) { Console.WriteLine("  FAIL: default timeout should be positive"); return 1; }
        if (string.IsNullOrEmpty(opts.UserAgent)) { Console.WriteLine("  FAIL: default user agent missing"); return 1; }
        Console.WriteLine("  defaults ok (headless=" + opts.Headless + ", timeout=" + opts.TimeoutMs + ")");
        return 0;
    }
}
