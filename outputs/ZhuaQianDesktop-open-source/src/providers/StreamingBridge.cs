using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ZhuaQianDesktopApp
{
    // Streaming transport for provider chat completions. Wraps an OpenAI-compatible
    // or Ollama chat endpoint with `stream:true` and parses Server-Sent Events,
    // invoking a callback per text delta. This implements the missing
    // "Streaming provider responses" item from CODE_COMPLETION_ALIGNMENT.md.
    public class StreamingBridge
    {
        public int TimeoutMs { get; set; }

        public StreamingBridge()
        {
            TimeoutMs = 120000;
        }

        public async Task StreamAsync(string url, string apiKey, string jsonBody,
            Action<string> onDelta, Action onDone, Action<Exception> onError)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = TimeoutMs;
                req.ReadWriteTimeout = TimeoutMs;
                if (!string.IsNullOrEmpty(apiKey))
                    req.Headers["Authorization"] = "Bearer " + apiKey;

                byte[] body = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
                using (var rs = await req.GetRequestStreamAsync())
                    await rs.WriteAsync(body, 0, body.Length);

                using (var resp = (HttpWebResponse)await req.GetResponseAsync())
                using (var stream = resp.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        string data = line.StartsWith("data:") ? line.Substring(5).Trim() : line;
                        if (data == "[DONE]") break;
                        string delta = ExtractDelta(data);
                        if (!string.IsNullOrEmpty(delta) && onDelta != null)
                            onDelta(delta);
                    }
                }
                if (onDone != null) onDone();
            }
            catch (Exception ex)
            {
                if (onError != null) onError(ex);
                else throw;
            }
        }

        // JavaScriptSerializer.DeserializeObject returns object[] for JSON arrays in this
        // runtime, so normalize to an ArrayList the callers can index.
        static System.Collections.ArrayList AsList(object o)
        {
            var arr = o as object[];
            if (arr != null)
            {
                var r = new System.Collections.ArrayList();
                foreach (var x in arr) r.Add(x);
                return r;
            }
            return o as System.Collections.ArrayList;
        }

        public static string ExtractDelta(string dataLine)
        {
            if (string.IsNullOrWhiteSpace(dataLine)) return "";
            int open = dataLine.IndexOf('{');
            int close = dataLine.LastIndexOf('}');
            if (open < 0 || close <= open) return "";

            string json = dataLine.Substring(open, close - open + 1);
            var dict = SimpleJsonParse(json);
            if (dict == null) return "";

            if (dict.ContainsKey("choices"))
            {
                var choices = AsList(dict["choices"]);
                if (choices != null && choices.Count > 0)
                {
                    var first = choices[0] as Dictionary<string, object>;
                    if (first != null && first.ContainsKey("delta"))
                    {
                        var delta = first["delta"] as Dictionary<string, object>;
                        if (delta != null && delta.ContainsKey("content"))
                            return Convert.ToString(delta["content"]);
                    }
                }
            }

            if (dict.ContainsKey("candidates"))
            {
                var cands = AsList(dict["candidates"]);
                if (cands != null && cands.Count > 0)
                {
                    var first = cands[0] as Dictionary<string, object>;
                    if (first != null && first.ContainsKey("content"))
                    {
                        var content = first["content"] as Dictionary<string, object>;
                        if (content != null && content.ContainsKey("parts"))
                        {
                            var parts = AsList(content["parts"]);
                            if (parts != null)
                            {
                                var sb = new StringBuilder();
                                foreach (var p in parts)
                                {
                                    var pd = p as Dictionary<string, object>;
                                    if (pd != null && pd.ContainsKey("text"))
                                        sb.Append(Convert.ToString(pd["text"]));
                                }
                                return sb.ToString();
                            }
                        }
                    }
                }
            }
            return "";
        }

        static Dictionary<string, object> SimpleJsonParse(string s)
        {
            try
            {
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                return ser.DeserializeObject(s) as Dictionary<string, object>;
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("StreamingBridge.SimpleJsonParse: " + _ex.Message); return null; }
        }
    }
}
