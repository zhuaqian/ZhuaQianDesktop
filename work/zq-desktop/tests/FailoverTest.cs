// FailoverTest.cs
// Deterministic end-to-end test of ProviderManager.SendAsync failover using
// mock provider clients (no network). Proves that when the current model is
// dead, SendAsync actually retries the next candidate and switches the active
// model -- not just that the fallback chain is ordered.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZhuaQianDesktopApp;

namespace ZhuaQianDesktopApp.Tests
{
    internal class FailoverTest
    {
        static int passed = 0;
        static int failed = 0;

        // Mock provider client. Can be told to throw (simulating a dead model)
        // or to return a canned reply.
        class FakeClient : IProviderClient
        {
            public string ProviderId { get; set; }
            public bool SupportsVision { get { return false; } }
            public bool ShouldThrow;
            public string Reply = "OK";
            public string LastModelId;

            public Task<string> SendAsync(List<Dictionary<string, object>> nativeMessages, ModelInfo model, string apiKey, string endpoint)
            {
                LastModelId = model != null ? model.Id : "";
                if (ShouldThrow) throw new Exception("simulated dead model: " + (model != null ? model.Id : "?"));
                return Task.FromResult(Reply);
            }

            public Task<string> TestConnectionAsync(ModelInfo model, string apiKey, string endpoint)
            {
                return Task.FromResult(ShouldThrow ? "FAIL" : "PASS");
            }
        }

        static ModelInfo MkModel(string id, string endpoint)
        {
            return new ModelInfo
            {
                Id = id,
                DisplayName = id,
                ProviderId = endpoint,
                Endpoint = endpoint,
                IsFree = true,
                RequiresApiKey = false
            };
        }

        static void Check(string name, bool ok, string detail = null)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + (detail != null ? "  -> " + detail : "")); }
        }

        static void Main()
        {
            TestFailoverSwitchesModel();
            TestAllFailThrows();
            TestNoKeyModelSkipped();

            Console.WriteLine("");
            Console.WriteLine("=== FAILOVER RESULT: passed=" + passed + " failed=" + failed + " ===");
            Environment.Exit(failed == 0 ? 0 : 1);
        }

        // Current model dies -> SendAsync must return the next model's reply and
        // switch the active model to it.
        static void TestFailoverSwitchesModel()
        {
            Console.WriteLine("[Failover: dead current -> next model]");

            var mgr = new ProviderManager();
            var a = MkModel("fake-a", "FakeA");
            var b = MkModel("fake-b", "FakeB");

            // Isolate: clear the shared registry and keep only our two mock models
            // so no real provider (network/key) interferes.
            ModelRegistry.Free.Clear();
            ModelRegistry.Local.Clear();
            ModelRegistry.Paid.Clear();
            ModelRegistry.All.Clear();
            ModelRegistry.Free.Add(a);
            ModelRegistry.Free.Add(b);
            ModelRegistry.All.Add(a);
            ModelRegistry.All.Add(b);

            var clientA = new FakeClient { ProviderId = "FakeA", ShouldThrow = true };
            var clientB = new FakeClient { ProviderId = "FakeB", ShouldThrow = false, Reply = "REPLY-FROM-B" };
            mgr.RegisterClient(clientA);
            mgr.RegisterClient(clientB);

            mgr.SelectModel(a);

            string result = mgr.SendAsync(new List<Dictionary<string, object>>())
                .GetAwaiter().GetResult();

            Check("returns fallback model's reply", result == "REPLY-FROM-B", result);
            Check("active model switched to fallback",
                mgr.CurrentModel != null && mgr.CurrentModel.Id == "fake-b",
                mgr.CurrentModel != null ? mgr.CurrentModel.Id : "null");
            Check("dead model was actually attempted", clientA.LastModelId == "fake-a");
        }

        // If every candidate fails, SendAsync must throw (so work only breaks when
        // truly nothing works).
        static void TestAllFailThrows()
        {
            Console.WriteLine("[Failover: all fail -> throws]");

            var mgr = new ProviderManager();
            var a = MkModel("fake-a", "FakeA");
            var b = MkModel("fake-b", "FakeB");

            ModelRegistry.Free.Clear();
            ModelRegistry.Local.Clear();
            ModelRegistry.Paid.Clear();
            ModelRegistry.All.Clear();
            ModelRegistry.Free.Add(a);
            ModelRegistry.Free.Add(b);
            ModelRegistry.All.Add(a);
            ModelRegistry.All.Add(b);

            mgr.RegisterClient(new FakeClient { ProviderId = "FakeA", ShouldThrow = true });
            mgr.RegisterClient(new FakeClient { ProviderId = "FakeB", ShouldThrow = true });
            mgr.SelectModel(a);

            bool threw = false;
            string msg = "";
            try
            {
                mgr.SendAsync(new List<Dictionary<string, object>>()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                threw = true;
                msg = ex.Message;
            }
            Check("throws when all models fail", threw);
            Check("error mentions all models failed", threw && msg.IndexOf("All models failed", StringComparison.OrdinalIgnoreCase) >= 0, msg);
        }

        // A model that needs an API key the user hasn't set must be skipped (not
        // attempted and not counted as a failure that breaks the chain).
        static void TestNoKeyModelSkipped()
        {
            Console.WriteLine("[Failover: no-key model skipped]");

            var mgr = new ProviderManager();
            var nokey = MkModel("needs-key", "NoKeyProvider");
            nokey.RequiresApiKey = true;
            var b = MkModel("fake-b", "FakeB");

            ModelRegistry.Free.Clear();
            ModelRegistry.Local.Clear();
            ModelRegistry.Paid.Clear();
            ModelRegistry.All.Clear();
            ModelRegistry.Free.Add(nokey);
            ModelRegistry.Free.Add(b);
            ModelRegistry.All.Add(nokey);
            ModelRegistry.All.Add(b);

            // NoKeyProvider has no registered client, so it's dropped from the chain.
            var clientB = new FakeClient { ProviderId = "FakeB", Reply = "REPLY-FROM-B" };
            mgr.RegisterClient(clientB);
            mgr.SelectModel(nokey);

            string result = mgr.SendAsync(new List<Dictionary<string, object>>())
                .GetAwaiter().GetResult();

            Check("skips model with no client/key and uses next", result == "REPLY-FROM-B", result);
            Check("switched to the working model",
                mgr.CurrentModel != null && mgr.CurrentModel.Id == "fake-b",
                mgr.CurrentModel != null ? mgr.CurrentModel.Id : "null");
        }
    }
}
