using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ZhuaQianDesktopApp
{
    public class GeminiClient : IProviderClient
    {
        public string ProviderId { get { return "Gemini"; } }
        public bool SupportsVision { get { return true; } }
        public bool UseGoogleSearchForNextRequest { get; set; }

        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };

        public Task<string> SendAsync(List<Dictionary<string, object>> nativeMessages, ModelInfo model,
            string apiKey, string endpoint)
        {
            return Task.Run(() => DoSend(nativeMessages, model, apiKey));
        }

        string DoSend(List<Dictionary<string, object>> nativeMessages, ModelInfo model, string apiKey)
        {
            var payload = new Dictionary<string, object>
            {
                { "systemInstruction", new Dictionary<string, object> {
                    { "parts", new ArrayList { new Dictionary<string, object> {
                        { "text", "You are ZhuaQian Desktop, a practical free Windows AI work assistant. Analyze files and answer concisely with actionable output. Never claim that you created, saved, exported, attached, moved, deleted, renamed, emailed, ran, clicked, or ended anything locally unless the desktop app explicitly reports a real saved path or execution result. If the user asks for TXT, Word, PowerPoint, Excel, PDF, or PNG output, provide clean content for that file; the desktop app will handle actual local saving. When live Google Search grounding is available, use current sources and prefer verifiable recent facts." }
                    } } }
                } },
                { "contents", nativeMessages },
                { "generationConfig", new Dictionary<string, object> {
                    { "temperature", 0.4 }, { "topP", 0.95 }, { "maxOutputTokens", 4096 }
                } }
            };

            if (UseGoogleSearchForNextRequest)
            {
                payload["tools"] = new ArrayList {
                    new Dictionary<string, object> {
                        { "google_search", new Dictionary<string, object>() }
                    }
                };
            }

            string body = json.Serialize(payload);
            try
            {
                return PostGemini(model.Id, body, apiKey);
            }
            catch (Exception ex)
            {
                if (!UseGoogleSearchForNextRequest) throw;
                payload.Remove("tools");
                string fallback = PostGemini(model.Id, json.Serialize(payload), apiKey);
                return "[Current info warning: Google Search grounding failed, so this answer may rely on model knowledge. " + ex.Message + "]\r\n\r\n" + fallback;
            }
            finally
            {
                UseGoogleSearchForNextRequest = false;
            }
        }

        string PostGemini(string modelId, string body, string apiKey)
        {
            string url = "https://generativelanguage.googleapis.com/v1beta/models/" +
                Uri.EscapeDataString(modelId) + ":generateContent?key=" + Uri.EscapeDataString(apiKey);
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                string respText = wc.UploadString(url, "POST", body);
                var resp = json.DeserializeObject(respText) as Dictionary<string, object>;
                return ExtractReplyText(resp);
            }
        }

        public static string ExtractReplyText(Dictionary<string, object> resp)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
            if (resp == null) throw new Exception("Gemini returned empty response.");
            if (!resp.ContainsKey("candidates")) throw new Exception("Gemini returned no candidates: " + ser.Serialize(resp));
            var candidates = resp["candidates"] as IList;
            if (candidates == null || candidates.Count == 0) throw new Exception("Gemini returned no candidates: " + ser.Serialize(resp));
            var cand = candidates[0] as Dictionary<string, object>;
            if (cand == null) throw new Exception("Gemini candidate is empty: " + ser.Serialize(resp));
            var content = cand.ContainsKey("content") ? cand["content"] as Dictionary<string, object> : null;
            var parts = content != null && content.ContainsKey("parts") ? content["parts"] as IList : null;
            if (parts == null || parts.Count == 0) throw new Exception("Gemini returned no text parts: " + ser.Serialize(resp));

            var sb = new StringBuilder();
            foreach (var item in parts)
            {
                var part = item as Dictionary<string, object>;
                if (part == null || !part.ContainsKey("text")) continue;
                string text = Convert.ToString(part["text"]);
                if (!string.IsNullOrWhiteSpace(text)) sb.Append(text);
            }

            string reply = sb.ToString().Trim();
            string sources = ExtractGroundingSources(cand);
            if (!string.IsNullOrWhiteSpace(reply))
                return string.IsNullOrWhiteSpace(sources) ? reply : reply + "\r\n\r\nSources:\r\n" + sources;
            throw new Exception("Gemini returned no text: " + ser.Serialize(resp));
        }

        static string ExtractGroundingSources(Dictionary<string, object> candidate)
        {
            if (candidate == null || !candidate.ContainsKey("groundingMetadata")) return "";
            var metadata = candidate["groundingMetadata"] as Dictionary<string, object>;
            if (metadata == null || !metadata.ContainsKey("groundingChunks")) return "";
            var chunks = metadata["groundingChunks"] as IList;
            if (chunks == null || chunks.Count == 0) return "";

            var sb = new StringBuilder();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in chunks)
            {
                var chunk = item as Dictionary<string, object>;
                if (chunk == null || !chunk.ContainsKey("web")) continue;
                var web = chunk["web"] as Dictionary<string, object>;
                if (web == null) continue;
                string uri = web.ContainsKey("uri") ? Convert.ToString(web["uri"]) : "";
                if (string.IsNullOrWhiteSpace(uri) || seen.Contains(uri)) continue;
                seen.Add(uri);
                string title = web.ContainsKey("title") ? Convert.ToString(web["title"]) : "";
                if (string.IsNullOrWhiteSpace(title)) title = uri;
                sb.Append("- ").Append(title).Append(" - ").Append(uri).Append("\r\n");
                if (seen.Count >= 6) break;
            }
            return sb.ToString().Trim();
        }

        public Task<string> TestConnectionAsync(ModelInfo model, string apiKey, string endpoint)
        {
            return Task.Run(() => DoTest(model, apiKey));
        }

        string DoTest(ModelInfo model, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return "FAIL: API key is empty. Get one at " + model.ApiKeyUrl;

            var payload = new Dictionary<string, object>
            {
                { "contents", new ArrayList {
                    new Dictionary<string, object> {
                        { "parts", new ArrayList {
                            new Dictionary<string, object> { { "text", "Say 'OK' if you can read this." } }
                        } },
                        { "role", "user" }
                    }
                } },
                { "generationConfig", new Dictionary<string, object> {
                    { "temperature", 0.1 }, { "maxOutputTokens", 10 }
                } }
            };

            try
            {
                string reply = PostGemini(model.Id, json.Serialize(payload), apiKey);
                if (!string.IsNullOrWhiteSpace(reply))
                    return "PASS: Gemini " + model.Id + " is reachable.";
                return "FAIL: Gemini returned no text.";
            }
            catch (Exception ex)
            {
                return "FAIL: " + ex.Message;
            }
        }
    }
}
