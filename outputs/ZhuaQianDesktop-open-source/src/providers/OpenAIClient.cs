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
    public class OpenAIClient : IProviderClient
    {
        public string ProviderId { get { return "OpenAICompatible"; } }
        public bool SupportsVision { get { return false; } }

        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };

        public Task<string> SendAsync(List<Dictionary<string, object>> nativeMessages, ModelInfo model,
            string apiKey, string endpoint)
        {
            return Task.Run(() => DoSend(nativeMessages, model, apiKey, endpoint));
        }

        string DoSend(List<Dictionary<string, object>> nativeMessages, ModelInfo model, string apiKey, string endpoint)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new Exception("API Key is required for OpenAI-compatible endpoints.");

            string baseUrl = !string.IsNullOrWhiteSpace(endpoint) ? endpoint : "https://api.openai.com/v1";
            baseUrl = baseUrl.TrimEnd(new char[] { '/' });
            string url = baseUrl + "/chat/completions";

            var oaiMessages = new ArrayList();
            oaiMessages.Add(new Dictionary<string, object> {
                { "role", "system" },
                { "content", "You are ZhuaQian Desktop, a practical AI work assistant. Be concise and useful." }
            });

            foreach (var msg in nativeMessages)
            {
                string role = Convert.ToString(msg["role"]);
                if (role == "model") role = "assistant";
                string content = PartsToText(msg.ContainsKey("parts") ? msg["parts"] : null);
                if (string.IsNullOrWhiteSpace(content)) continue;
                oaiMessages.Add(new Dictionary<string, object> { { "role", role }, { "content", content } });
            }

            var payload = new Dictionary<string, object> {
                { "model", model.Id },
                { "messages", oaiMessages },
                { "temperature", 0.4 }
            };

            string body = json.Serialize(payload);
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey;
                try
                {
                    string respText = wc.UploadString(url, "POST", body);
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
                    throw new Exception("OpenAI API error for model '" + model.Id + "': " + ex.Message +
                        (detail.Length > 0 ? "\n" + detail : ""));
                }
            }
        }

        string ExtractReply(Dictionary<string, object> resp)
        {
            if (resp == null) throw new Exception("Empty response.");
            if (!resp.ContainsKey("choices")) throw new Exception("No choices: " + json.Serialize(resp));
            var choices = resp["choices"] as ArrayList;
            if (choices == null || choices.Count == 0) throw new Exception("No choices: " + json.Serialize(resp));
            var choice = choices[0] as Dictionary<string, object>;
            if (choice == null) throw new Exception("Choice is null: " + json.Serialize(resp));
            var messageObj = choice.ContainsKey("message") ? choice["message"] : null;
            var message = messageObj as Dictionary<string, object>;
            if (message != null && message.ContainsKey("content"))
            {
                string content = Convert.ToString(message["content"]);
                if (!string.IsNullOrWhiteSpace(content)) return content;
            }
            throw new Exception("No text: " + json.Serialize(resp));
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
            return Task.Run(() => DoTest(model, apiKey, endpoint));
        }

        string DoTest(ModelInfo model, string apiKey, string endpoint)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return "FAIL: API key is required.";

            string baseUrl = !string.IsNullOrWhiteSpace(endpoint) ? endpoint : "https://api.openai.com/v1";
            string url = baseUrl.TrimEnd(new char[] { '/' }) + "/chat/completions";

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
                    string respText = wc.UploadString(url, "POST", json.Serialize(payload));
                    return "PASS: " + model.Id + " via " + baseUrl;
                }
            }
            catch (Exception ex)
            {
                return "FAIL: " + ex.Message;
            }
        }
    }
}
