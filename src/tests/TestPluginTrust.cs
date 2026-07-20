using System;
using System.IO;
using ZhuaQianDesktopApp.Plugins;

// Validates plugin publisher signing/verification (roadmap 1.4) WITHOUT a real UI.
static class TestPluginTrust
{
    public static int RunAll()
    {
        int failures = 0;
        failures += TestSignAndVerifyRoundTrip();
        failures += TestTamperedManifestFails();
        failures += TestUnknownPublisherFails();
        failures += TestParserMarksTrusted();
        Console.WriteLine("[TestPluginTrust] failures=" + failures);
        return failures;
    }

    static string TempStore()
    {
        var p = Path.Combine(Path.GetTempPath(), "zq-trust-" + Guid.NewGuid().ToString("N") + ".json");
        if (File.Exists(p)) File.Delete(p);
        return p;
    }

    static int TestSignAndVerifyRoundTrip()
    {
        int fails = 0;
        var keys = PluginTrust.GenerateKeyPair();
        var m = new PluginManifest { Id = "p1", Name = "Demo", Publisher = "alice", Entry = "demo.py", EntryType = PluginEntryType.Py };
        m.RequiredPermissions.Add("permFileWrite");
        string json = m.ToJson();
        string sig = PluginTrust.SignManifestJson(json, keys[0]);
        Assert(PluginTrust.VerifyManifestJson(json, sig, keys[1]), "valid signature verifies");
        return fails;
    }

    static int TestTamperedManifestFails()
    {
        int fails = 0;
        var keys = PluginTrust.GenerateKeyPair();
        var m = new PluginManifest { Id = "p1", Name = "Demo", Publisher = "alice", Entry = "demo.py", EntryType = PluginEntryType.Py };
        string json = m.ToJson();
        string sig = PluginTrust.SignManifestJson(json, keys[0]);
        string tampered = json.Replace("\"Name\":\"Demo\"", "\"Name\":\"Evil\"");
        Assert(!PluginTrust.VerifyManifestJson(tampered, sig, keys[1]), "tampered manifest fails verification");
        return fails;
    }

    static int TestUnknownPublisherFails()
    {
        int fails = 0;
        string storePath = TempStore();
        var store = new PluginTrustStore(storePath); // empty: no publishers known
        var m = new PluginManifest { Id = "p1", Name = "Demo", Publisher = "alice", Entry = "demo.py", EntryType = PluginEntryType.Py };
        string json = m.ToJson();
        var keys = PluginTrust.GenerateKeyPair();
        string sig = PluginTrust.SignManifestJson(json, keys[0]);
        var signed = new PluginManifest { Id = "p1", Name = "Demo", Publisher = "alice", Entry = "demo.py", EntryType = PluginEntryType.Py, Signature = sig };
        var parser = new PluginManifestParser(store);
        var pres = parser.ParseFromString(signed.ToJson(), store);
        Assert(!pres.Success, "signed manifest from unknown publisher is rejected");
        Assert(pres.Errors.Exists(e => e.IndexOf("unknown publisher", StringComparison.OrdinalIgnoreCase) >= 0), "unknown publisher error reported");
        if (File.Exists(storePath)) File.Delete(storePath);
        return fails;
    }

    static int TestParserMarksTrusted()
    {
        int fails = 0;
        var keys = PluginTrust.GenerateKeyPair();
        string storePath = TempStore();
        var store = new PluginTrustStore(storePath);
        store.AddPublisher("alice", keys[1]);
        var m = new PluginManifest { Id = "p1", Name = "Demo", Publisher = "alice", Entry = "demo.py", EntryType = PluginEntryType.Py };
        string json = m.ToJson();
        string sig = PluginTrust.SignManifestJson(json, keys[0]);
        var signed = new PluginManifest { Id = "p1", Name = "Demo", Publisher = "alice", Entry = "demo.py", EntryType = PluginEntryType.Py, Signature = sig };
        var parser = new PluginManifestParser(store);
        var pres = parser.ParseFromString(signed.ToJson(), store);
        Assert(pres.Success, "signed manifest from known publisher parses ok");
        Assert(pres.Manifest.Trusted, "manifest marked trusted");
        if (File.Exists(storePath)) File.Delete(storePath);
        return fails;
    }

    static void Assert(bool cond, string msg) { if (!cond) Console.WriteLine("  FAIL: " + msg); }
}
