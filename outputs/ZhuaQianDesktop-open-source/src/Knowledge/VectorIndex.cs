using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace ZhuaQianDesktopApp.Knowledge
{
    public class VectorIndex
    {
        readonly string indexPath;
        readonly Dictionary<string, float[]> vectors = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

        public VectorIndex(string folder)
        {
            indexPath = Path.Combine(folder, "knowledge-vectors.jsonl");
        }

        public int Count { get { return vectors.Count; } }

        public void Load()
        {
            vectors.Clear();
            if (!File.Exists(indexPath)) return;
            foreach (string line in File.ReadAllLines(indexPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var parsed = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(line) as Dictionary<string, object>;
                    if (parsed == null) continue;
                    string chunkId = parsed.ContainsKey("chunkId") ? Convert.ToString(parsed["chunkId"]) : "";
                    if (string.IsNullOrWhiteSpace(chunkId)) continue;
                    var raw = parsed.ContainsKey("embedding") ? parsed["embedding"] as ArrayList : null;
                    if (raw == null || raw.Count == 0) continue;
                    var vec = new float[raw.Count];
                    for (int i = 0; i < raw.Count; i++)
                        vec[i] = Convert.ToSingle(raw[i]);
                    vectors[chunkId] = vec;
                }
                catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("VectorIndex.Load parse error: " + _ex.Message); }
            }
        }

        public void SaveAll(IEnumerable<ChunkedDoc> docs, string url, string model)
        {
            int batchSize = 5;
            var buffer = new List<ChunkedDoc>();
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            using (var writer = new StreamWriter(indexPath, false, Encoding.UTF8))
            {
                foreach (var doc in docs)
                {
                    if (vectors.ContainsKey(doc.ChunkId)) continue;
                    buffer.Add(doc);
                    if (buffer.Count >= batchSize)
                    {
                        ComputeBatch(buffer, url, model, serializer, writer);
                        buffer.Clear();
                    }
                }
                if (buffer.Count > 0)
                    ComputeBatch(buffer, url, model, serializer, writer);
            }
        }

        void ComputeBatch(List<ChunkedDoc> batch, string url, string model, System.Web.Script.Serialization.JavaScriptSerializer serializer, StreamWriter writer)
        {
            foreach (var doc in batch)
            {
                float[] vec = ComputeEmbedding(doc, url, model);
                if (vec == null) continue;
                vectors[doc.ChunkId] = vec;
                string line = serializer.Serialize(new Dictionary<string, object> {
                    { "chunkId", doc.ChunkId },
                    { "embedding", new ArrayList(vec) },
                    { "model", model }
                });
                writer.WriteLine(line);
            }
        }

        float[] ComputeEmbedding(ChunkedDoc doc, string url, string model)
        {
            string text = (doc.Heading + "\n" + doc.Summary + "\n" + (doc.Text ?? ""));
            if (text.Length > 1024) text = text.Substring(0, 1024);
            return CallEmbeddingApi(text, url, model);
        }

        public float[] GetVector(string chunkId)
        {
            if (vectors.ContainsKey(chunkId)) return vectors[chunkId];
            return null;
        }

        public float[] ComputeQueryEmbedding(string query, string url, string model)
        {
            return CallEmbeddingApi(query, url, model);
        }

        float[] CallEmbeddingApi(string input, string url, string model)
        {
            try
            {
                var payload = new Dictionary<string, object> {
                    { "model", string.IsNullOrWhiteSpace(model) ? "nomic-embed-text" : model },
                    { "prompt", input ?? "" }
                };
                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                string body = serializer.Serialize(payload);
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    string resp = wc.UploadString(url, "POST", body);
                    var data = serializer.DeserializeObject(resp) as Dictionary<string, object>;
                    if (data != null && data.ContainsKey("embedding"))
                    {
                        var raw = data["embedding"] as ArrayList;
                        if (raw != null && raw.Count > 0)
                        {
                            var vec = new float[raw.Count];
                            for (int i = 0; i < raw.Count; i++)
                                vec[i] = Convert.ToSingle(raw[i]);
                            return vec;
                        }
                    }
                }
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("VectorIndex.CallEmbeddingApi: " + _ex.Message); }
            return null;
        }

        public static float CosineSimilarity(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length == 0 || a.Length != b.Length) return 0;
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }
            if (na == 0 || nb == 0) return 0;
            return (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb)));
        }
    }
}
