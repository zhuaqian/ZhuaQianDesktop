using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ZhuaQianDesktopApp.Core
{
    // Local secret protector backed by Windows DPAPI (CurrentUser scope) — the
    // SAME mechanism MainForm.Settings.cs uses for API keys (ProtectedData.Protect
    // / Unprotect with DataProtectionScope.CurrentUser). Centralizing it here lets
    // every sensitive local artifact (browser session cookies & localStorage,
    // API keys) share ONE trusted, machine+user-scoped store instead of each
    // module rolling its own plaintext or weak crypto.
    //
    // Security model: DPAPI ciphertext is only decryptable by the same Windows
    // user on the same machine. That is exactly right for a browser login session
    // or an API key that this app uses locally — it is NOT a transport or sharing
    // format, and it must never be embedded in anything that outlives the op
    // (e.g. a git remote URL).
    public static class SecretProtector
    {
        // Encrypt a UTF-8 string to DPAPI ciphertext bytes (CurrentUser scope).
        public static byte[] ProtectString(string plain)
        {
            if (plain == null) plain = "";
            byte[] raw = Encoding.UTF8.GetBytes(plain);
            return ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
        }

        // Decrypt DPAPI ciphertext bytes back to a UTF-8 string.
        public static string UnprotectString(byte[] cipher)
        {
            if (cipher == null || cipher.Length == 0) return "";
            byte[] raw = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(raw);
        }

        // Encrypt `plain` and write the ciphertext to `path` (no plaintext on disk).
        public static void ProtectToFile(string plain, string path)
        {
            byte[] cipher = ProtectString(plain);
            File.WriteAllBytes(path, cipher);
        }

        // Read and decrypt a file written by ProtectToFile. Returns null if the
        // ciphertext cannot be unprotected (wrong user/machine, tampered file).
        // Also accepts legacy plaintext session files (pre-encryption builds) so an
        // existing saved session still loads after upgrading.
        public static string ReadProtectedFile(string path)
        {
            if (!File.Exists(path)) return null;
            try { return UnprotectString(File.ReadAllBytes(path)); }
            catch (Exception ex)
            {
                try { return File.ReadAllText(path); } catch { }
                System.Diagnostics.Debug.WriteLine("SecretProtector.ReadProtectedFile: " + ex.Message);
                return null;
            }
        }
    }
}
