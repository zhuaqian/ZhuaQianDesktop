using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ZhuaQianDesktopApp
{
    public class LocalClient : IProviderClient
    {
        public string ProviderId { get { return "Local"; } }
        public bool SupportsVision { get { return false; } }

        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };

        public string DefaultLocalApiUrl = "http://localhost:11434/api/chat";

        public Task<string> SendAsync(List<Dictionary<string, object>> nativeMessages, ModelInfo model,
            string apiKey, string endpoint)
        {
            return Task.Run(() => DoSend(nativeMessages, model, endpoint));
        }

        string DoSend(List<Dictionary<string, object>> nativeMessages, ModelInfo model, string endpoint)
        {
            string url = !string.IsNullOrWhiteSpace(endpoint) ? endpoint : DefaultLocalApiUrl;
            if (!url.EndsWith("/api/chat")) url = url.TrimEnd(new char[] { '/' }) + "/api/chat";

            var localMessages = new ArrayList();
            localMessages.Add(new Dictionary<string, object> {
                { "role", "system" },
                { "content", "You are ZhuaQian Desktop, a practical local AI work assistant. Be concise and useful. Never claim that you created, saved, exported, attached, moved, deleted, renamed, emailed, ran, clicked, or ended anything locally unless the desktop app explicitly reports a real saved path or execution result. If the user asks for TXT, Word, PowerPoint, or Excel output, provide clean content for that file; the desktop app will handle actual local saving." }
            });

            foreach (var msg in nativeMessages)
            {
                string role = Convert.ToString(msg["role"]);
                if (role == "model") role = "assistant";
                string content = PartsToText(msg.ContainsKey("parts") ? msg["parts"] : null);
                if (string.IsNullOrWhiteSpace(content)) continue;
                localMessages.Add(new Dictionary<string, object> { { "role", role }, { "content", content } });
            }

            var payload = new Dictionary<string, object> {
                { "model", model.Id },
                { "messages", localMessages },
                { "stream", false }
            };

            string body = json.Serialize(payload);
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                try
                {
                    string respText = wc.UploadString(url, "POST", body);
                    var resp = json.DeserializeObject(respText) as Dictionary<string, object>;
                    if (resp != null && resp.ContainsKey("message"))
                    {
                        var message = resp["message"] as Dictionary<string, object>;
                        if (message != null && message.ContainsKey("content"))
                        {
                            string content = Convert.ToString(message["content"]);
                            if (!string.IsNullOrWhiteSpace(content)) return content;
                        }
                    }
                    if (resp != null && resp.ContainsKey("response"))
                    {
                        string content = Convert.ToString(resp["response"]);
                        if (!string.IsNullOrWhiteSpace(content)) return content;
                    }
                    throw new Exception("Local API returned no text: " + respText);
                }
                catch (WebException ex)
                {
                    string detail = "";
                    if (ex.Response != null)
                    {
                        using (var stream = ex.Response.GetResponseStream())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                            detail = reader.ReadToEnd();
                    }
                    throw new Exception("Local API error for model '" + model.Id + "': " + ex.Message +
                        (detail.Length > 0 ? "\n" + detail : ""));
                }
            }
        }

        string PartsToText(object partsValue)
        {
            var parts = partsValue as ArrayList;
            if (parts == null) return "";
            var sb = new StringBuilder();
            foreach (var partObj in parts)
            {
                var part = partObj as Dictionary<string, object>;
                if (part == null) continue;
                if (part.ContainsKey("text")) sb.AppendLine(Convert.ToString(part["text"]));
            }
            return sb.ToString().Trim();
        }

        public Task<string> TestConnectionAsync(ModelInfo model, string apiKey, string endpoint)
        {
            return Task.Run(() => DoTest(model, endpoint));
        }

        string DoTest(ModelInfo model, string endpoint)
        {
            string url = !string.IsNullOrWhiteSpace(endpoint) ? endpoint : DefaultLocalApiUrl;
            if (!url.EndsWith("/api/chat")) url = url.TrimEnd(new char[] { '/' }) + "/api/chat";

            try
            {
                var payload = new Dictionary<string, object> {
                    { "model", model.Id },
                    { "messages", new ArrayList {
                        new Dictionary<string, object> { { "role", "user" }, { "content", "Say OK" } }
                    } },
                    { "stream", false }
                };

                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    string respText = wc.UploadString(url, "POST", json.Serialize(payload));
                    return "PASS: " + model.Id + " via " + url;
                }
            }
            catch (Exception ex)
            {
                return "FAIL: " + ex.Message;
            }
        }
    }
}
