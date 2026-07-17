using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ZhuaQianDesktopApp.Core
{
    /// <summary>
    /// 构建 / 导入可移植项目包 .zqp（zip 容器: manifest.json + project/ + knowledge/）。
    /// 可选密码保护（交给 ShareCrypto 做 AES 加密）。包内不含任何 API Key。
    /// </summary>
    public class PackageBuilder
    {
        const string Schema = "zqp/1";
        static readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };

        public class ImportResult
        {
            public string TaskId;
            public string Title;
            public bool Encrypted;
            public List<string> Includes = new List<string>();
        }

        public static void Build(string outputPath, string title, string createdBy,
            byte[] taskJson, byte[] settingsJson, byte[] knowledgeJson,
            bool encrypted, string password)
        {
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentNullException("outputPath");
            if (taskJson == null) throw new ArgumentNullException("taskJson");
            if (encrypted && string.IsNullOrEmpty(password))
                throw new ArgumentException("加密分享必须设置密码。", "password");

            var files = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            files["project/task.json"] = taskJson;
            if (settingsJson != null && settingsJson.Length > 0) files["project/settings.json"] = settingsJson;
            if (knowledgeJson != null && knowledgeJson.Length > 0) files["knowledge/knowledge-index.json"] = knowledgeJson;

            string sha = ComputeHash(files);

            var manifest = new Dictionary<string, object>
            {
                { "schema", Schema },
                { "title", title ?? "ZhuaQian Project" },
                { "createdBy", createdBy ?? "" },
                { "createdAt", DateTime.Now.ToString("o") },
                { "kind", "project" },
                { "encrypted", encrypted },
                { "sha256", sha },
                { "includes", new List<object>(files.Keys) }
            };
            files["manifest.json"] = Encoding.UTF8.GetBytes(json.Serialize(manifest));

            byte[] payload = ZipBytes(files);
            byte[] final = encrypted ? ShareCrypto.Encrypt(payload, password) : payload;
            File.WriteAllBytes(outputPath, final);
        }

        /// <summary>与 Build 相同，但返回字节流（用于内存传输，如 LAN 分享）。</summary>
        public static byte[] BuildToBytes(string title, string createdBy,
            byte[] taskJson, byte[] settingsJson, byte[] knowledgeJson,
            bool encrypted, string password)
        {
            if (taskJson == null) throw new ArgumentNullException("taskJson");
            if (encrypted && string.IsNullOrEmpty(password))
                throw new ArgumentException("加密分享必须设置密码。", "password");

            var files = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            files["project/task.json"] = taskJson;
            if (settingsJson != null && settingsJson.Length > 0) files["project/settings.json"] = settingsJson;
            if (knowledgeJson != null && knowledgeJson.Length > 0) files["knowledge/knowledge-index.json"] = knowledgeJson;

            string sha = ComputeHash(files);
            var manifest = new Dictionary<string, object>
            {
                { "schema", Schema },
                { "title", title ?? "ZhuaQian Project" },
                { "createdBy", createdBy ?? "" },
                { "createdAt", DateTime.Now.ToString("o") },
                { "kind", "project" },
                { "encrypted", encrypted },
                { "sha256", sha },
                { "includes", new List<object>(files.Keys) }
            };
            files["manifest.json"] = Encoding.UTF8.GetBytes(json.Serialize(manifest));

            byte[] payload = ZipBytes(files);
            return encrypted ? ShareCrypto.Encrypt(payload, password) : payload;
        }

        public static ImportResult Import(string zqpPath, string tasksDir, string password)
        {
            if (!File.Exists(zqpPath)) throw new FileNotFoundException("包文件不存在。", zqpPath);
            return ImportBytes(File.ReadAllBytes(zqpPath), tasksDir, password);
        }

        /// <summary>从内存字节流导入（用于文件或 LAN/URL 下载结果）。</summary>
        public static ImportResult ImportBytes(byte[] raw, string tasksDir, string password)
        {
            bool encrypted;
            byte[] payload;
            if (TryOpenZip(raw))
            {
                payload = raw;
                encrypted = false;
            }
            else
            {
                if (string.IsNullOrEmpty(password))
                    throw new CryptographicException("该分享包已加密，需要密码才能导入。");
                payload = ShareCrypto.Decrypt(raw, password);
                encrypted = true;
            }

            var files = UnzipBytes(payload);

            if (!files.ContainsKey("manifest.json"))
                throw new Exception("无效的 .zqp 包：缺少 manifest.json。");

            var manifest = json.DeserializeObject(Encoding.UTF8.GetString(files["manifest.json"])) as Dictionary<string, object>;
            if (manifest == null) throw new Exception("无效的 manifest.json。");

            // 完整性校验：排除 manifest 后重新计算 sha256
            var verify = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in files)
                if (!string.Equals(kv.Key, "manifest.json", StringComparison.OrdinalIgnoreCase))
                    verify[kv.Key] = kv.Value;
            string calc = ComputeHash(verify);
            string expected = Convert.ToString(manifest.ContainsKey("sha256") ? manifest["sha256"] : "");
            if (!string.Equals(calc, expected, StringComparison.OrdinalIgnoreCase))
                throw new Exception("包内容校验失败（sha256 不匹配），可能已损坏或被篡改。");

            if (!files.ContainsKey("project/task.json"))
                throw new Exception("包内没有任务数据。");

            // 写入任务目录：换新 id 避免覆盖本地同名任务
            string newId = Guid.NewGuid().ToString("N");
            var taskData = json.DeserializeObject(Encoding.UTF8.GetString(files["project/task.json"])) as Dictionary<string, object>;
            if (taskData == null) throw new Exception("任务数据解析失败。");
            taskData["id"] = newId;
            if (taskData.ContainsKey("title")) taskData["title"] = Convert.ToString(taskData["title"]) + " (shared)";
            string outPath = Path.Combine(tasksDir, newId + ".json");
            File.WriteAllText(outPath, json.Serialize(taskData), Encoding.UTF8);

            return new ImportResult
            {
                TaskId = newId,
                Title = Convert.ToString(taskData.ContainsKey("title") ? taskData["title"] : "Imported task"),
                Encrypted = encrypted,
                Includes = new List<string>(files.Keys)
            };
        }

        static string ComputeHash(SortedDictionary<string, byte[]> files)
        {
            using (var sha = SHA256.Create())
            {
                foreach (var kv in files)
                {
                    byte[] nameBytes = Encoding.UTF8.GetBytes(kv.Key);
                    byte[] lenBytes = BitConverter.GetBytes(nameBytes.Length);
                    byte[] contentLen = BitConverter.GetBytes(kv.Value.Length);
                    sha.TransformBlock(lenBytes, 0, lenBytes.Length, null, 0);
                    sha.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);
                    sha.TransformBlock(contentLen, 0, contentLen.Length, null, 0);
                    sha.TransformBlock(kv.Value, 0, kv.Value.Length, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                var sb = new StringBuilder();
                foreach (byte b in sha.Hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static byte[] ZipBytes(SortedDictionary<string, byte[]> files)
        {
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (var kv in files)
                        AddZipEntry(zip, kv.Key, kv.Value);
                }
                return ms.ToArray();
            }
        }

        static Dictionary<string, byte[]> UnzipBytes(byte[] payload)
        {
            var outFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (var ms = new MemoryStream(payload))
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith("/")) continue;
                    using (var es = entry.Open())
                    using (var buf = new MemoryStream())
                    {
                        es.CopyTo(buf);
                        outFiles[entry.FullName] = buf.ToArray();
                    }
                }
            }
            return outFiles;
        }

        static bool TryOpenZip(byte[] data)
        {
            try
            {
                using (var ms = new MemoryStream(data))
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
                    return zip.Entries.Count >= 0;
            }
            catch
            {
                return false;
            }
        }

        static void AddZipEntry(ZipArchive zip, string name, byte[] content)
        {
            var entry = zip.CreateEntry(name);
            using (var es = entry.Open())
                es.Write(content, 0, content.Length);
        }
    }
}
