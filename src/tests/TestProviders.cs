using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZhuaQianDesktopApp;

// Offline unit tests for the provider layer (Epic A / Provider routing).
// Providers call out over the network at runtime, so these tests exercise the
// pure response parsing and the ProviderManager fallback/routing logic with
// fake clients -- no real HTTP is performed.
static class TestProviders
{
    public static int RunAll()
    {
        int failures = 0;
        failures += TestGeminiExtractText();
        failures += TestGeminiExtractWithGrounding();
        failures += TestGeminiExtractEmptyThrows();
        failures += TestManagerApiKeyRouting();
        failures += TestManagerModelLabels();
        failures += TestManagerHasUsableKey();
        failures += TestManagerFallbackToNextClient();
        failures += TestManagerNonRetryableRethrows();
        Console.WriteLine("[TestProviders] failures=" + failures);
        return failures;
    }

    // A controllable IProviderClient: either returns a fixed string or throws.
    sealed class FakeProviderClient : IProviderClient
    {
        public string ProviderId { get; private set; }
        public bool SupportsVision { get { return false; } }
        readonly string result;
        readonly string throwMessage;

        public FakeProviderClient(string providerId, string result = "ok", string throwMessage = null)
        {
            ProviderId = providerId;
            this.result = result;
            this.throwMessage = throwMessage;
        }

        public Task<string> SendAsync(List<Dictionary<string, object>> nativeMessages, ModelInfo model, string apiKey, string endpoint)
        {
            if (throwMessage != null) throw new Exception(throwMessage);
            return Task.FromResult(result);
        }

        public Task<string> TestConnectionAsync(ModelInfo model, string apiKey, string endpoint)
        {
            return Task.FromResult(throwMessage != null ? "FAIL: " + throwMessage : "PASS");
        }
    }

    static int TestGeminiExtractText()
    {
        int fails = 0;
        var resp = new Dictionary<string, object>
        {
            { "candidates", new ArrayList { new Dictionary<string, object>
            {
                { "content", new Dictionary<string, object>
                {
                    { "parts", new ArrayList { new Dictionary<string, object> { { "text", "hello world" } } } }
                }}
            }}}
        };
        string text = GeminiClient.ExtractReplyText(resp);
        Assert(text == "hello world", "Gemini text extracted, got=[" + text + "]");
        return fails;
    }

    static int TestGeminiExtractWithGrounding()
    {
        int fails = 0;
        var resp = new Dictionary<string, object>
        {
            { "candidates", new ArrayList { new Dictionary<string, object>
            {
                { "content", new Dictionary<string, object>
                {
                    { "parts", new ArrayList { new Dictionary<string, object> { { "text", "answer" } } } }
                }},
                { "groundingMetadata", new Dictionary<string, object>
                {
                    { "groundingChunks", new ArrayList { new Dictionary<string, object>
                    {
                        { "web", new Dictionary<string, object> { { "uri", "https://example.com/doc" }, { "title", "Example" } } }
                    }}
                }}
            }}}
        };
        string text = GeminiClient.ExtractReplyText(resp);
        Assert(text.IndexOf("answer") >= 0, "answer text present");
        Assert(text.IndexOf("Sources:") >= 0, "grounding sources appended");
        Assert(text.IndexOf("https://example.com/doc") >= 0, "source uri present");
        return fails;
    }

    static int TestGeminiExtractEmptyThrows()
    {
        int fails = 0;
        try
        {
            GeminiClient.ExtractReplyText(new Dictionary<string, object>());
            Assert(false, "empty response should throw");
        }
        catch (Exception)
        {
            Assert(true, "empty response throws");
        }
        return fails;
    }

    static int TestManagerApiKeyRouting()
    {
        int fails = 0;
        var pm = new ProviderManager();
        pm.GeminiKey = "gk";
        pm.OpenRouterKey = "ork";
        pm.TencentKey = "tkey";
        Assert(pm.GetApiKey(new ModelInfo { Endpoint = "Gemini", RequiresApiKey = true }) == "gk", "gemini key routed");
        Assert(pm.GetApiKey(new ModelInfo { Endpoint = "OpenRouter", RequiresApiKey = true }) == "ork", "openrouter key routed");
        Assert(pm.GetApiKey(new ModelInfo { Endpoint = "TencentWorkBuddy", RequiresApiKey = true }) == "tkey", "tencent key routed");
        Assert(pm.GetApiKey(new ModelInfo { Endpoint = "Local", RequiresApiKey = false }) == "", "local needs no key");
        return fails;
    }

    static int TestManagerModelLabels()
    {
        int fails = 0;
        var pm = new ProviderManager();
        pm.SelectModel(new ModelInfo { DisplayName = "Free Model", IsFree = true, RequiresApiKey = false });
        Assert(pm.CurrentModelLabel().IndexOf("FREE") >= 0, "free label");
        pm.SelectModel(new ModelInfo { DisplayName = "Local Model", RequiresApiKey = false, SupportsVision = false });
        Assert(pm.CurrentModelLabel().IndexOf("LOCAL") >= 0, "local label");
        pm.SelectModel(new ModelInfo { DisplayName = "Paid Model", IsFree = false, RequiresApiKey = true });
        Assert(pm.CurrentModelLabel().IndexOf("PAID") >= 0, "paid label");
        return fails;
    }

    static int TestManagerHasUsableKey()
    {
        int fails = 0;
        var pm = new ProviderManager();
        pm.SelectModel(new ModelInfo { Endpoint = "Local", RequiresApiKey = false });
        Assert(pm.HasUsableKey(), "local model usable without key");
        pm.SelectModel(new ModelInfo { Endpoint = "Gemini", RequiresApiKey = true });
        Assert(!pm.HasUsableKey(), "paid model unusable without key");
        pm.GeminiKey = "present";
        Assert(pm.HasUsableKey(), "paid model usable with key");
        return fails;
    }

    // Current client throws a retryable error -> manager falls back to the next
    // registered client and reports the fallback.
    static int TestManagerFallbackToNextClient()
    {
        int fails = 0;
        var pm = new ProviderManager();
        // Replace every built-in client with a success fake so the fallback target
        // is deterministic and never hits the network.
        foreach (var m in ModelRegistry.All)
            pm.RegisterClient(new FakeProviderClient(m.Endpoint, "fallback-ok"));
        pm.RegisterClient(new FakeProviderClient("FakeA", "ignored", "429 rate limited"));
        pm.SelectModel(new ModelInfo { Id = "fake-a", DisplayName = "FakeA", Endpoint = "FakeA", RequiresApiKey = false, IsFree = true });

        string r = pm.SendAsync(new List<Dictionary<string, object>>()).GetAwaiter().GetResult();
        Assert(r == "fallback-ok", "fallback returns next client result, got=[" + r + "]");
        Assert(!string.IsNullOrEmpty(pm.LastFallbackNotice) && pm.LastFallbackNotice.IndexOf("Fallback used") >= 0, "fallback notice set");
        return fails;
    }

    // A non-retryable error is rethrown instead of silently falling back.
    static int TestManagerNonRetryableRethrows()
    {
        int fails = 0;
        var pm = new ProviderManager();
        foreach (var m in ModelRegistry.All)
            pm.RegisterClient(new FakeProviderClient(m.Endpoint, "fallback-ok"));
        pm.RegisterClient(new FakeProviderClient("FakeA", "ignored", "auth failed: bad key"));
        pm.SelectModel(new ModelInfo { Id = "fake-a", DisplayName = "FakeA", Endpoint = "FakeA", RequiresApiKey = false, IsFree = true });

        bool threw = false;
        try { pm.SendAsync(new List<Dictionary<string, object>>()).GetAwaiter().GetResult(); }
        catch (Exception ex) { threw = ex.Message.IndexOf("auth failed") >= 0; }
        Assert(threw, "non-retryable error rethrown");
        return fails;
    }

    static void Assert(bool cond, string msg) { if (!cond) Console.WriteLine("  FAIL: " + msg); }
}
