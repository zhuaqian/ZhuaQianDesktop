using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ZhuaQianDesktopApp
{
    public class ProviderManager
    {
        readonly Dictionary<string, IProviderClient> clients = new Dictionary<string, IProviderClient>(StringComparer.OrdinalIgnoreCase);
        readonly List<ModelInfo> allModels;
        ModelInfo currentModel;

        public ModelInfo CurrentModel
        {
            get { return currentModel; }
        }

        public event Action<ModelInfo> OnModelChanged;

        public string GeminiKey;
        public string OpenRouterKey;
        public string LocalApiUrl;
        public string CustomApiUrl;
        public string CustomApiKey;
        public string TencentKey;
        public string AlibabaKey;
        public string ZhipuKey;
        public string EmbeddingModel;
        public bool UseGoogleSearchForNextRequest;

        public ProviderManager()
        {
            GeminiKey = "";
            OpenRouterKey = "";
            LocalApiUrl = "http://localhost:11434/api/chat";
            CustomApiUrl = "";
            CustomApiKey = "";
            TencentKey = "";
            AlibabaKey = "";
            ZhipuKey = "";
            EmbeddingModel = "nomic-embed-text";

            RegisterClient(new GeminiClient());
            RegisterClient(new OpenRouterClient());
            RegisterClient(new LocalClient());
            RegisterClient(new OpenAIClient());
            RegisterClient(new TencentWorkBuddyClient());
            RegisterClient(new AlibabaQianwenClient());
            RegisterClient(new ZhipuAIGLMClient());

            allModels = ModelRegistry.GetAllOrdered();
            if (allModels.Count > 0)
            {
                currentModel = allModels[0];
            }
        }

        public void RegisterClient(IProviderClient client)
        {
            clients[client.ProviderId] = client;
        }

        public IProviderClient GetClient(string providerId)
        {
            IProviderClient c;
            if (clients.TryGetValue(providerId, out c))
                return c;
            return null;
        }

        public IProviderClient GetClientForModel(ModelInfo model)
        {
            return GetClient(model.Endpoint);
        }

        public string GetApiKey(ModelInfo model)
        {
            if (!model.RequiresApiKey) return "";
            string ep = model.Endpoint;
            if (string.Equals(ep, "Gemini", StringComparison.OrdinalIgnoreCase)) return GeminiKey;
            if (string.Equals(ep, "OpenRouter", StringComparison.OrdinalIgnoreCase)) return OpenRouterKey;
            if (string.Equals(ep, "OpenAICompatible", StringComparison.OrdinalIgnoreCase)) return CustomApiKey;
            if (string.Equals(ep, "TencentWorkBuddy", StringComparison.OrdinalIgnoreCase)) return TencentKey;
            if (string.Equals(ep, "AlibabaQianwen", StringComparison.OrdinalIgnoreCase)) return AlibabaKey;
            if (string.Equals(ep, "ZhipuAI", StringComparison.OrdinalIgnoreCase)) return ZhipuKey;
            return "";
        }

        public string GetEndpoint(ModelInfo model)
        {
            if (string.Equals(model.Endpoint, "Local", StringComparison.OrdinalIgnoreCase)) return LocalApiUrl;
            if (string.Equals(model.Endpoint, "OpenAICompatible", StringComparison.OrdinalIgnoreCase)) return CustomApiUrl;
            return "";
        }

        public void SelectModel(ModelInfo model)
        {
            currentModel = model;
            if (OnModelChanged != null)
                OnModelChanged(model);
        }

        public string CurrentModelLabel()
        {
            if (currentModel == null) return "No model";
            string prefix = currentModel.IsFree ? "FREE" : "PAID";
            if (!currentModel.RequiresApiKey) prefix = "LOCAL";
            return "[" + prefix + "] " + currentModel.DisplayName;
        }

        public bool HasUsableKey()
        {
            if (currentModel == null) return false;
            if (!currentModel.RequiresApiKey) return true;
            string key = GetApiKey(currentModel);
            return !string.IsNullOrWhiteSpace(key);
        }

        public bool MayUseCloud()
        {
            if (currentModel == null) return false;
            string ep = currentModel.Endpoint;
            return string.Equals(ep, "Gemini", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ep, "OpenRouter", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ep, "OpenAICompatible", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ep, "TencentWorkBuddy", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ep, "AlibabaQianwen", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ep, "ZhipuAI", StringComparison.OrdinalIgnoreCase);
        }

        public bool HasInlineDataSupport()
        {
            if (currentModel == null) return false;
            return currentModel.SupportsVision && HasUsableKey();
        }

        public List<ModelInfo> GetFallbackChain()
        {
            var chain = new List<ModelInfo>();
            if (currentModel != null) chain.Add(currentModel);

            foreach (var m in ModelRegistry.Free)
            {
                if (!chain.Contains(m) && GetClientForModel(m) != null)
                    chain.Add(m);
            }
            foreach (var m in ModelRegistry.Local)
            {
                if (!chain.Contains(m) && GetClientForModel(m) != null)
                    chain.Add(m);
            }
            foreach (var m in ModelRegistry.Paid)
            {
                if (!chain.Contains(m) && GetClientForModel(m) != null)
                    chain.Add(m);
            }
            return chain;
        }

        public async Task<string> SendAsync(List<Dictionary<string, object>> nativeMessages)
        {
            if (currentModel == null)
                throw new Exception("No model selected. Open Settings and choose a model.");

            var errors = new List<string>();
            var chain = GetFallbackChain();

            foreach (var fallbackModel in chain)
            {
                try
                {
                    var fbClient = GetClientForModel(fallbackModel);
                    if (fbClient == null)
                    {
                        errors.Add(fallbackModel.DisplayName + ": no provider client");
                        continue;
                    }
                    string fbKey = GetApiKey(fallbackModel);
                    string fbEndpoint = GetEndpoint(fallbackModel);
                    var geminiClient = fbClient as GeminiClient;
                    if (geminiClient != null)
                        geminiClient.UseGoogleSearchForNextRequest = UseGoogleSearchForNextRequest;

                    if (fallbackModel.RequiresApiKey && string.IsNullOrWhiteSpace(fbKey))
                    {
                        errors.Add(fallbackModel.DisplayName + ": no API key");
                        continue;
                    }

                    string result = await fbClient.SendAsync(nativeMessages, fallbackModel, fbKey, fbEndpoint);
                    if (!string.Equals(fallbackModel.Id, currentModel.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectModel(fallbackModel);
                    }
                    UseGoogleSearchForNextRequest = false;
                    return result;
                }
                catch (Exception ex)
                {
                    errors.Add(fallbackModel.DisplayName + ": " + ex.Message);
                    if (!IsRetryable(ex.Message)) throw;
                }
            }

            UseGoogleSearchForNextRequest = false;
            throw new Exception("All models failed.\n\n" + string.Join("\n\n", errors.ToArray()));
        }

        bool IsRetryable(string msg)
        {
            string lower = msg.ToLowerInvariant();
            return lower.Contains("429") || lower.Contains("503") || lower.Contains("500") ||
                   lower.Contains("overloaded") || lower.Contains("quota") ||
                   lower.Contains("unavailable") || lower.Contains("timeout") ||
                   lower.Contains("not found") || lower.Contains("dead model");
        }

        public string CurrentApiKey()
        {
            return GetApiKey(currentModel);
        }

        public string StreamingUrl()
        {
            if (currentModel == null) return null;
            string ep = currentModel.Endpoint;
            if (string.Equals(ep, "Gemini", StringComparison.OrdinalIgnoreCase))
                return "https://generativelanguage.googleapis.com/v1beta/models/" + currentModel.Id + ":streamGenerateContent?alt=sse&key=" + CurrentApiKey();
            if (string.Equals(ep, "OpenRouter", StringComparison.OrdinalIgnoreCase))
                return "https://openrouter.ai/api/v1/chat/completions";
            if (string.Equals(ep, "OpenAICompatible", StringComparison.OrdinalIgnoreCase))
                return CustomApiUrl.TrimEnd('/') + "/chat/completions";
            if (string.Equals(ep, "Local", StringComparison.OrdinalIgnoreCase))
                return LocalApiUrl;
            return null;
        }

        public async Task<string> TestModelAsync(ModelInfo model)
        {
            var client = GetClientForModel(model);
            if (client == null) return "FAIL: No client for provider " + model.Endpoint;

            string apiKey = GetApiKey(model);
            string endpoint = GetEndpoint(model);

            if (model.RequiresApiKey && string.IsNullOrWhiteSpace(apiKey))
                return "NO KEY: Get one at " + (string.IsNullOrEmpty(model.ApiKeyUrl) ? "provider website" : model.ApiKeyUrl);

            return await client.TestConnectionAsync(model, apiKey, endpoint);
        }
    }
}
