using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ZhuaQianDesktopApp.Core
{
    /// <summary>
    /// 跨机器密码加密层（用于 .zqp 分享包）。
    /// 不使用 DPAPI（仅本机当前用户可用），改用 PBKDF2 派生密钥 + AES-256-CBC + HMAC-SHA256（encrypt-then-MAC）。
    /// 注：.NET Framework 4.8 不含 AesGcm，故采用 CBC + HMAC 方案，机密性与完整性兼顾。
    /// </summary>
    public static class ShareCrypto
    {
        const int SaltSize = 16;
        const int IvSize = 16;
        const int KeySize = 32;       // AES-256
        const int MacSize = 32;       // HMAC-SHA256
        const int Iterations = 100000;

        public static byte[] Encrypt(byte[] payload, string password)
        {
            if (payload == null) throw new ArgumentNullException("payload");
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password required for encryption.");

            byte[] salt = RandomBytes(SaltSize);
            byte[] key = DeriveKey(password, salt);
            byte[] iv = RandomBytes(IvSize);

            byte[] cipher = AesCbc(key, iv, payload, encrypt: true);

            // encrypt-then-MAC：对 salt + iv + cipher 计算 HMAC
            byte[] mac = Hmac(key, Concat(salt, iv, cipher));

            return Concat(salt, iv, cipher, mac);
        }

        public static byte[] Decrypt(byte[] blob, string password)
        {
            if (blob == null) throw new ArgumentNullException("blob");
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password required for decryption.");
            if (blob.Length < SaltSize + IvSize + MacSize + 16)
                throw new CryptographicException("数据长度不足，可能不是有效的加密包。");

            int off = 0;
            byte[] salt = Slice(blob, ref off, SaltSize);
            byte[] iv = Slice(blob, ref off, IvSize);
            int cipherLen = blob.Length - off - MacSize;
            byte[] cipher = Slice(blob, ref off, cipherLen);
            byte[] macStored = Slice(blob, ref off, MacSize);

            byte[] key = DeriveKey(password, salt);
            byte[] macCalc = Hmac(key, Concat(salt, iv, cipher));

            if (!ConstantTimeEquals(macCalc, macStored))
                throw new CryptographicException("密码错误或数据已被篡改。");

            return AesCbc(key, iv, cipher, encrypt: false);
        }

        static byte[] DeriveKey(string password, byte[] salt)
        {
            using (var kdf = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
                return kdf.GetBytes(KeySize);
        }

        static byte[] AesCbc(byte[] key, byte[] iv, byte[] data, bool encrypt)
        {
            using (var aes = new AesCryptoServiceProvider())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;
                using (var ms = new MemoryStream())
                using (var cs = new CryptoStream(ms, encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(data, 0, data.Length);
                    cs.FlushFinalBlock();
                    return ms.ToArray();
                }
            }
        }

        static byte[] Hmac(byte[] key, byte[] data)
        {
            using (var hmac = new HMACSHA256(key))
                return hmac.ComputeHash(data);
        }

        static byte[] RandomBytes(int n)
        {
            var b = new byte[n];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(b);
            return b;
        }

        static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        static byte[] Concat(params byte[][] arrays)
        {
            int len = 0;
            foreach (var a in arrays) len += a.Length;
            var outBuf = new byte[len];
            int pos = 0;
            foreach (var a in arrays)
            {
                Buffer.BlockCopy(a, 0, outBuf, pos, a.Length);
                pos += a.Length;
            }
            return outBuf;
        }

        static byte[] Slice(byte[] src, ref int offset, int count)
        {
            var part = new byte[count];
            Buffer.BlockCopy(src, offset, part, 0, count);
            offset += count;
            return part;
        }
    }
}
