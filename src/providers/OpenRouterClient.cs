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
    public class OpenRouterClient : IProviderClient
    {
        public string ProviderId { get { return "OpenRouter"; } }
        public bool SupportsVision { get { return true; } }

        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };

        public Task<string> SendAsync(List<Dictionary<string, object>> nativeMessages, ModelInfo model,
            string apiKey, string endpoint)
        {
            return Task.Run(() => DoSend(nativeMessages, model, apiKey));
        }

        string DoSend(List<Dictionary<string, object>> nativeMessages, ModelInfo model, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new Exception("OpenRouter API Key is empty. Get one at " + model.ApiKeyUrl);

            var orMessages = new ArrayList();
            orMessages.Add(new Dictionary<string, object> {
                { "role", "system" },
                { "content", "You are ZhuaQian Desktop, a practical AI work assistant. Be concise, useful, and focused on making work faster. Never claim that you created, saved, exported, attached, moved, deleted, renamed, emailed, ran, clicked, or ended anything locally unless the desktop app explicitly reports a real saved path or execution result. If the user asks for TXT, Word, PowerPoint, or Excel output, provide clean content for that file; the desktop app will handle actual local saving." }
            });

            foreach (var msg in nativeMessages)
            {
                string role = Convert.ToString(msg["role"]);
                if (role == "model") role = "assistant";
                var partsObj = msg.ContainsKey("parts") ? msg["parts"] : null;
                string content = "";
                var partsList = partsObj as ArrayList;
                if (partsList != null)
                {
                    var sb = new StringBuilder();
                    foreach (var partObj in partsList)
                    {
                        var part = partObj as Dictionary<string, object>;
                        if (part == null) continue;
                        if (part.ContainsKey("text")) sb.AppendLine(Convert.ToString(part["text"]));
                        if (part.ContainsKey("inlineData"))
                            sb.AppendLine("[Binary omitted for OpenRouter text model]");
                    }
                    content = sb.ToString().Trim();
                }
                if (string.IsNullOrWhiteSpace(content)) continue;
                orMessages.Add(new Dictionary<string, object> { { "role", role }, { "content", content } });
            }

            var payload = new Dictionary<string, object> {
                { "model", model.Id },
                { "messages", orMessages },
                { "temperature", 0.4 }
            };

            string body = json.Serialize(payload);
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey;
                wc.Headers["HTTP-Referer"] = "https://zhuaqian.local";
                wc.Headers["X-Title"] = "ZhuaQian Desktop";
                try
                {
                    string respText = wc.UploadString("https://openrouter.ai/api/v1/chat/completions", "POST", body);
                    var resp = json.DeserializeObject(respText) as Dictionary<string, object>;
                    return ExtractReply(resp);
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
                    throw new Exception("OpenRouter API error for model '" + model.Id + "': " + ex.Message +
                        (detail.Length > 0 ? "\n" + detail : ""));
                }
            }
        }

        string ExtractReply(Dictionary<string, object> resp)
        {
            if (resp == null) throw new Exception("OpenRouter returned empty response.");
            if (!resp.ContainsKey("choices")) throw new Exception("OpenRouter returned no choices: " + json.Serialize(resp));
            var choices = resp["choices"] as ArrayList;
            if (choices == null || choices.Count == 0) throw new Exception("OpenRouter returned no choices: " + json.Serialize(resp));
            var choice = choices[0] as Dictionary<string, object>;
            if (choice == null) throw new Exception("OpenRouter choice is null: " + json.Serialize(resp));
            var messageObj = choice.ContainsKey("message") ? choice["message"] : null;
            var message = messageObj as Dictionary<string, object>;
            if (message != null && message.ContainsKey("content"))
            {
                string content = Convert.ToString(message["content"]);
                if (!string.IsNullOrWhiteSpace(content)) return content;
            }
            throw new Exception("OpenRouter returned no text: " + json.Serialize(resp));
        }

        public Task<string> TestConnectionAsync(ModelInfo model, string apiKey, string endpoint)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(apiKey))
                    return "FAIL: API key is empty. Get one at " + model.ApiKeyUrl;

                try
                {
                    var payload = new Dictionary<string, object> {
                        { "model", model.Id },
                        { "messages", new ArrayList {
                            new Dictionary<string, object> { { "role", "user" }, { "content", "Say OK" } }
                        } },
                        { "max_tokens", 10 }
                    };

                    using (var wc = new WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                        wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey;
                        wc.Headers["HTTP-Referer"] = "https://zhuaqian.local";
                        wc.Headers["X-Title"] = "ZhuaQian Desktop";
                        string respText = wc.UploadString("https://openrouter.ai/api/v1/chat/completions", "POST", json.Serialize(payload));
                        return "PASS: " + model.Id + " is reachable.";
                    }
                }
                catch (Exception ex)
                {
                    return "FAIL: " + ex.Message;
                }
            });
        }
    }
}
