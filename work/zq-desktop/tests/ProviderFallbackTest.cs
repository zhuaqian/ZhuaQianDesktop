// ProviderFallbackTest.cs
// Real integration-style test of the auto model-switch (fallback) chain.
// Compiles against the actual ProviderManager + provider clients (no network needed
// just to inspect the fallback ordering).

using System;
using System.Collections.Generic;
using System.Linq;
using ZhuaQianDesktopApp;

namespace ZhuaQianDesktopApp.Tests
{
    internal class ProviderFallbackTest
    {
        static int passed = 0;
        static int failed = 0;

        static void Main()
        {
            TestFallbackIncludesPaidWhenFreeIsCurrent();
            TestFallbackIncludesPaidWhenLocalIsCurrent();
            TestFallbackIncludesFreeWhenPaidIsCurrent();
            TestCurrentModelFirst();
            TestNoClientModelsDropped();

            Console.WriteLine("");
            Console.WriteLine("=== FALLBACK RESULT: passed=" + passed + " failed=" + failed + " ===");
            Environment.Exit(failed == 0 ? 0 : 1);
        }

        static void Check(string name, bool ok, string detail = null)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + (detail != null ? "  -> " + detail : "")); }
        }

        static ProviderManager NewMgr()
        {
            var m = new ProviderManager();
            m.GeminiKey = "";
            m.OpenRouterKey = "";
            return m;
        }

        static void TestFallbackIncludesPaidWhenFreeIsCurrent()
        {
            Console.WriteLine("[Fallback: free current]");
            var mgr = NewMgr();
            // default current is the first free model
            var chain = mgr.GetFallbackChain();
            Check("chain has multiple candidates", chain.Count > 1, "count=" + chain.Count);
            Check("chain includes a paid model",
                chain.Any(c => c.Id == "gemini-2.5-pro-exp-03-25" || c.Id == "openai/gpt-4o"));
        }

        static void TestFallbackIncludesPaidWhenLocalIsCurrent()
        {
            Console.WriteLine("[Fallback: local current]");
            var mgr = NewMgr();
            var local = ModelRegistry.Local.FirstOrDefault();
            if (local != null)
            {
                mgr.SelectModel(local);
                var chain = mgr.GetFallbackChain();
                Check("chain built from local current", chain[0].Id == local.Id, chain.Count > 0 ? chain[0].Id : "empty");
                Check("chain still includes a paid model after local",
                    chain.Any(c => c.Id == "gemini-2.5-pro-exp-03-25" || c.Id == "openai/gpt-4o"));
            }
            else Check("local model exists", false);
        }

        static void TestFallbackIncludesFreeWhenPaidIsCurrent()
        {
            Console.WriteLine("[Fallback: paid current -> free/local]");
            var mgr = NewMgr();
            var paid = ModelRegistry.Paid.FirstOrDefault();
            if (paid != null)
            {
                mgr.SelectModel(paid);
                var chain = mgr.GetFallbackChain();
                Check("paid model is first in chain",
                    chain.Count > 0 && chain[0].Id == paid.Id, chain.Count > 0 ? chain[0].Id : "empty");
                Check("chain includes a free model (so paid can fail over to free)",
                    chain.Any(c => c.IsFree && c.RequiresApiKey));
                Check("chain includes a local model",
                    chain.Any(c => c.Endpoint == "Local"));
            }
            else Check("paid model exists", false);
        }

        static void TestCurrentModelFirst()
        {
            Console.WriteLine("[Fallback: current first]");
            var mgr = NewMgr();
            var chain = mgr.GetFallbackChain();
            Check("current model is first in chain",
                chain.Count > 0 && mgr.CurrentModel != null && chain[0].Id == mgr.CurrentModel.Id);
        }

        static void TestNoClientModelsDropped()
        {
            Console.WriteLine("[Fallback: all candidates have a client]");
            var mgr = NewMgr();
            var chain = mgr.GetFallbackChain();
            bool allHaveClient = true;
            foreach (var c in chain)
                if (mgr.GetClientForModel(c) == null) allHaveClient = false;
            Check("every fallback candidate has a provider client", allHaveClient);
        }
    }
}
