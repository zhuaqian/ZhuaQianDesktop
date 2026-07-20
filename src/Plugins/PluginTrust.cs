using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ZhuaQianDesktopApp.Plugins
{
    // Trust result for a plugin manifest after signature verification.
    public class PluginTrustResult
    {
        public bool Trusted;
        public bool SignaturePresent;
        public bool PublisherKnown;
        public readonly List<string> Reasons = new List<string>();
    }

    // Stores trusted publisher PUBLIC keys (RSA, XML format -- native to .NET 4.8,
    // so no PEM/ASN.1 parsing is needed). Private keys never touch this store.
    public class PluginTrustStore
    {
        readonly string storePath;
        readonly Dictionary<string, string> publishers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public PluginTrustStore(string storePath)
        {
            this.storePath = storePath ?? "";
            Load();
        }

        void Load()
        {
            try
            {
                if (!File.Exists(storePath)) return;
                var ser = new JavaScriptSerializer();
                var obj = ser.DeserializeObject(File.ReadAllText(storePath, Encoding.UTF8)) as Dictionary<string, object>;
                if (obj == null) return;
                foreach (var kv in obj)
                    publishers[kv.Key] = Convert.ToString(kv.Value);
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("PluginTrustStore.Load: " + _ex.Message); }
        }

        void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(storePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var ser = new JavaScriptSerializer();
                File.WriteAllText(storePath, ser.Serialize(publishers), Encoding.UTF8);
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("PluginTrustStore.Save: " + _ex.Message); }
        }

        public void AddPublisher(string publisher, string rsaPublicKeyXml)
        {
            if (string.IsNullOrWhiteSpace(publisher)) return;
            publishers[publisher] = rsaPublicKeyXml ?? "";
            Save();
        }

        public string GetPublicKey(string publisher)
        {
            if (string.IsNullOrWhiteSpace(publisher)) return null;
            string k;
            return publishers.TryGetValue(publisher, out k) ? k : null;
        }

        public List<string> ListPublishers()
        {
            return new List<string>(publishers.Keys);
        }
    }

    // RSA sign/verify helpers over manifest JSON. Signs a canonical form (Signature
    // field cleared) so the signature is independent of the stored signature value.
    // Uses XML key format (ToXmlString/FromXmlString) which is fully native to
    // .NET Framework 4.8 -- avoids the PEM/ASN.1 import gap on that runtime.
    public static class PluginTrust
    {
        public static string SignManifestJson(string manifestJson, string privateKeyXml)
        {
            var canonical = Canonical(manifestJson);
            using (var rsa = RSA.Create())
            {
                rsa.FromXmlString(privateKeyXml);
                var sig = rsa.SignData(Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return Convert.ToBase64String(sig);
            }
        }

        public static bool VerifyManifestJson(string manifestJson, string signature, string publicKeyXml)
        {
            if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(publicKeyXml)) return false;
            var canonical = Canonical(manifestJson);
            try
            {
                using (var rsa = RSA.Create())
                {
                    rsa.FromXmlString(publicKeyXml);
                    var sig = Convert.FromBase64String(signature);
                    return rsa.VerifyData(Encoding.UTF8.GetBytes(canonical), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("PluginTrust.Verify: " + _ex.Message); return false; }
        }

        // Clear the Signature field so the signed bytes never include the signature.
        static string Canonical(string manifestJson)
        {
            var ser = new JavaScriptSerializer();
            var m = ser.Deserialize<PluginManifest>(manifestJson ?? "{}");
            if (m == null) m = new PluginManifest();
            m.Signature = "";
            return m.ToJson();
        }

        // Mint a demo key pair (for tooling/tests). Returns [privateXml, publicXml].
        public static string[] GenerateKeyPair()
        {
            using (var rsa = RSA.Create(2048))
            {
                return new[] { rsa.ToXmlString(true), rsa.ToXmlString(false) };
            }
        }
    }
}
