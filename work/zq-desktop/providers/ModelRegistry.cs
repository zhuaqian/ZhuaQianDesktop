using System.Collections.Generic;
using System.Linq;

namespace ZhuaQianDesktopApp
{
    public class ModelInfo
    {
        public string Id;
        public string DisplayName;
        public string ProviderId;
        public bool IsFree;
        public bool SupportsVision;
        public int ContextLength;
        public bool RequiresApiKey;
        public string ApiKeyLabel;
        public string ApiKeyUrl;
        public string Endpoint;

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public static class ModelRegistry
    {
        public static List<ModelInfo> All { get; private set; }
        public static List<ModelInfo> Free { get; private set; }
        public static List<ModelInfo> Paid { get; private set; }
        public static List<ModelInfo> Local { get; private set; }

        static ModelRegistry()
        {
            All = new List<ModelInfo>();
            Free = new List<ModelInfo>();
            Paid = new List<ModelInfo>();
            Local = new List<ModelInfo>();
            RegisterFreeModels();
            RegisterPaidModels();
            RegisterLocalModels();
            RegisterEcosystemModels();
        }

        // 腾讯 / 阿里巴巴 / 智谱 生态办公模型
        static void RegisterEcosystemModels()
        {
            // 腾讯生态：TokenHub 聚合 + 混元
            AddFree("hunyuan-pro", "TencentWorkBuddy", "Tencent Hunyuan Pro (TokenHub)", true, 128000, true,
                "Tencent WorkBuddy API Key", "https://tokenhub.tencentcloud.com", "TencentWorkBuddy");
            AddFree("deepseek-v4-pro", "TencentWorkBuddy", "DeepSeek V4 Pro (Tencent TokenHub)", true, 128000, true,
                "Tencent WorkBuddy API Key", "https://tokenhub.tencentcloud.com", "TencentWorkBuddy");
            AddFree("kimi-v1-128k", "TencentWorkBuddy", "Kimi V1 128K (Tencent TokenHub)", false, 128000, true,
                "Tencent WorkBuddy API Key", "https://tokenhub.tencentcloud.com", "TencentWorkBuddy");

            // 阿里巴巴生态：通义千问 / DashScope
            AddFree("qwen-plus", "AlibabaQianwen", "Alibaba Qwen Plus (DashScope)", true, 128000, true,
                "Alibaba DashScope API Key", "https://dashscope.aliyuncs.com", "AlibabaQianwen");
            AddFree("qwen-max", "AlibabaQianwen", "Alibaba Qwen Max (DashScope)", true, 128000, true,
                "Alibaba DashScope API Key", "https://dashscope.aliyuncs.com", "AlibabaQianwen");
            AddFree("qwen2.5-72b-instruct", "AlibabaQianwen", "Qwen2.5 72B Instruct (DashScope)", false, 32768, true,
                "Alibaba DashScope API Key", "https://dashscope.aliyuncs.com", "AlibabaQianwen");
            AddFree("qwen-vl-max", "AlibabaQianwen", "Qwen VL Max Vision (DashScope)", true, 8000, true,
                "Alibaba DashScope API Key", "https://dashscope.aliyuncs.com", "AlibabaQianwen");

            // 智谱 AI 生态：ChatGLM
            AddFree("glm-4-plus", "ZhipuAI", "Zhipu GLM-4 Plus (ChatGLM)", false, 128000, true,
                "Zhipu AI API Key", "https://open.bigmodel.cn", "ZhipuAI");
            AddFree("glm-4-air", "ZhipuAI", "Zhipu GLM-4 Air (ChatGLM)", false, 128000, true,
                "Zhipu AI API Key", "https://open.bigmodel.cn", "ZhipuAI");
            AddFree("glm-4v", "ZhipuAI", "Zhipu GLM-4V Vision (ChatGLM)", true, 8000, true,
                "Zhipu AI API Key", "https://open.bigmodel.cn", "ZhipuAI");
        }

        static void RegisterFreeModels()
        {
            AddFree("gemini-3.5-flash", "Gemini", "Gemini 3.5 Flash (free key)", true, 1000000, true, "Gemini API Key", "https://aistudio.google.com/apikey", "Gemini");
            AddFree("gemini-3.1-flash-lite", "Gemini", "Gemini 3.1 Flash-Lite (free key)", true, 1000000, true, "Gemini API Key", "https://aistudio.google.com/apikey", "Gemini");
            AddFree("gemini-3-flash-preview", "Gemini", "Gemini 3 Flash Preview (free key)", true, 1000000, true, "Gemini API Key", "https://aistudio.google.com/apikey", "Gemini");
            AddFree("gemini-2.5-flash", "Gemini", "Gemini 2.5 Flash (free key)", true, 1000000, true, "Gemini API Key", "https://aistudio.google.com/apikey", "Gemini");
            AddFree("gemini-2.5-flash-lite", "Gemini", "Gemini 2.5 Flash-Lite (free key)", true, 1000000, true, "Gemini API Key", "https://aistudio.google.com/apikey", "Gemini");
            AddFree("gemini-2.5-pro", "Gemini", "Gemini 2.5 Pro (free tier)", true, 1000000, true, "Gemini API Key", "https://aistudio.google.com/apikey", "Gemini");
            AddFree("gemini-2.0-flash-lite", "Gemini", "Gemini 2.0 Flash Lite (free)", true, 1000000, true, "Gemini API Key", "https://aistudio.google.com/apikey", "Gemini");
            AddFree("gemini-2.0-flash", "Gemini", "Gemini 2.0 Flash (free)", true, 1000000, true, "Gemini API Key", "https://aistudio.google.com/apikey", "Gemini");
            AddFree("gemini-2.5-flash-preview", "Gemini", "Gemini 2.5 Flash Preview (free)", true, 1000000, true, "Gemini API Key", "https://aistudio.google.com/apikey", "Gemini");
            AddFree("gemini-2.5-pro-preview", "Gemini", "Gemini 2.5 Pro Preview (free tier)", true, 1000000, true, "Gemini API Key", "https://aistudio.google.com/apikey", "Gemini");
            AddFree("gemini-flash-lite-latest", "Gemini", "Gemini Flash Lite (latest, free)", true, 1000000, true, "Gemini API Key", "https://aistudio.google.com/apikey", "Gemini");
            AddFree("openrouter/auto", "OpenRouter", "OpenRouter Auto Router (auto)", true, 128000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddFree("deepseek/deepseek-r1-0528:free", "OpenRouter", "DeepSeek R1 0528 (OpenRouter free)", false, 128000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddFree("deepseek/deepseek-chat-v3-0324:free", "OpenRouter", "DeepSeek V3 0324 (OpenRouter free)", false, 128000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddFree("qwen/qwen3-coder:free", "OpenRouter", "Qwen3 Coder (OpenRouter free)", false, 128000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddFree("moonshotai/kimi-k2:free", "OpenRouter", "Kimi K2 (OpenRouter free)", false, 128000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddFree("z-ai/glm-4.5-air:free", "OpenRouter", "GLM-4.5 Air (OpenRouter free)", false, 128000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddFree("openai/gpt-oss-20b:free", "OpenRouter", "OpenAI GPT-OSS 20B (OpenRouter free)", false, 128000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddFree("meta-llama/llama-3-8b-instruct:free", "OpenRouter", "Meta Llama 3 8B (free)", true, 8192, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddFree("microsoft/phi-3-medium-128k-instruct:free", "OpenRouter", "Microsoft Phi-3 Medium 128K (free)", true, 128000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddFree("google/gemma-2-9b-it:free", "OpenRouter", "Google Gemma 2 9B (free)", true, 8192, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddFree("mistralai/mistral-7b-instruct:free", "OpenRouter", "Mistral 7B (free)", true, 8192, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddFree("huggingfaceh4/zephyr-7b-beta:free", "OpenRouter", "Zephyr 7B Beta (free)", true, 8192, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
        }

        static void AddFree(string id, string providerId, string displayName, bool vision, int ctx, bool needsKey, string keyLabel, string keyUrl, string endpoint)
        {
            var m = new ModelInfo { Id = id, ProviderId = providerId, DisplayName = displayName, IsFree = true, SupportsVision = vision, ContextLength = ctx, RequiresApiKey = needsKey, ApiKeyLabel = keyLabel, ApiKeyUrl = keyUrl, Endpoint = endpoint };
            All.Add(m);
            Free.Add(m);
        }

        static void RegisterPaidModels()
        {
            AddPaid("gemini-3.1-pro-preview", "Gemini", "Gemini 3.1 Pro Preview (paid)", true, 1000000, true, "Gemini API Key", "https://aistudio.google.com/apikey", "Gemini");
            AddPaid("gemini-2.5-pro-exp-03-25", "Gemini", "Gemini 2.5 Pro Exp (paid)", true, 1000000, true, "Gemini API Key", "https://aistudio.google.com/apikey", "Gemini");
            AddPaid("gemini-2.0-flash-thinking-exp-1219", "Gemini", "Gemini Flash Thinking Exp (paid)", true, 1000000, true, "Gemini API Key", "https://aistudio.google.com/apikey", "Gemini");
            AddPaid("anthropic/claude-opus-4.1", "OpenRouter", "Claude Opus 4.1 (OpenRouter paid)", true, 200000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddPaid("anthropic/claude-sonnet-4-20250514", "OpenRouter", "Claude Sonnet 4 (paid)", true, 200000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddPaid("anthropic/claude-3.5-sonnet", "OpenRouter", "Claude 3.5 Sonnet (paid)", true, 200000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddPaid("openai/gpt-5.1", "OpenRouter", "OpenAI GPT-5.1 (OpenRouter paid)", true, 400000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddPaid("openai/gpt-5.1-mini", "OpenRouter", "OpenAI GPT-5.1 Mini (OpenRouter paid)", true, 400000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddPaid("openai/gpt-4o", "OpenRouter", "OpenAI GPT-4o (paid)", true, 128000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddPaid("openai/gpt-4o-mini", "OpenRouter", "OpenAI GPT-4o Mini (paid)", true, 128000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddPaid("openai/o3-mini", "OpenRouter", "OpenAI o3-mini (paid)", false, 200000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddPaid("google/gemini-3.5-flash", "OpenRouter", "Gemini 3.5 Flash via OpenRouter (paid)", true, 1000000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddPaid("google/gemini-2.5-pro", "OpenRouter", "Gemini 2.5 Pro via OpenRouter (paid)", true, 1000000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddPaid("google/gemini-2.0-flash-001", "OpenRouter", "Gemini 2.0 Flash via OpenRouter (paid)", true, 1000000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddPaid("meta-llama/llama-3-70b-instruct", "OpenRouter", "Meta Llama 3 70B (paid)", false, 8192, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddPaid("mistralai/mistral-large", "OpenRouter", "Mistral Large (paid)", true, 128000, true, "OpenRouter API Key", "https://openrouter.ai/settings/keys", "OpenRouter");
            AddPaid("deepseek-chat", "OpenAICompatible", "DeepSeek Chat (custom endpoint)", false, 128000, true, "DeepSeek API Key", "https://platform.deepseek.com/api_keys", "OpenAICompatible");
            AddPaid("deepseek-reasoner", "OpenAICompatible", "DeepSeek Reasoner (custom endpoint)", false, 128000, true, "DeepSeek API Key", "https://platform.deepseek.com/api_keys", "OpenAICompatible");
            AddPaid("kimi-k2-0711-preview", "OpenAICompatible", "Moonshot Kimi K2 (custom endpoint)", false, 128000, true, "Moonshot API Key", "https://platform.moonshot.cn/console/api-keys", "OpenAICompatible");
            AddPaid("Qwen/Qwen3-Coder-480B-A35B-Instruct", "OpenAICompatible", "SiliconFlow Qwen3 Coder (custom endpoint)", false, 128000, true, "SiliconFlow API Key", "https://cloud.siliconflow.cn/account/ak", "OpenAICompatible");
            AddPaid("zai-org/GLM-4.5-Air", "OpenAICompatible", "SiliconFlow GLM-4.5 Air (custom endpoint)", false, 128000, true, "SiliconFlow API Key", "https://cloud.siliconflow.cn/account/ak", "OpenAICompatible");
            AddPaid("__custom_openai__", "OpenAICompatible", "Custom OpenAI-compatible API", false, 128000, true, "API Key", "", "OpenAICompatible");
        }

        static void AddPaid(string id, string providerId, string displayName, bool vision, int ctx, bool needsKey, string keyLabel, string keyUrl, string endpoint)
        {
            var m = new ModelInfo { Id = id, ProviderId = providerId, DisplayName = displayName, IsFree = false, SupportsVision = vision, ContextLength = ctx, RequiresApiKey = needsKey, ApiKeyLabel = keyLabel, ApiKeyUrl = keyUrl, Endpoint = endpoint };
            All.Add(m);
            Paid.Add(m);
        }

        static void RegisterLocalModels()
        {
            AddLocal("qwen3:8b", "Local", "Qwen3 8B (Ollama)", false, 128000, false, "", "", "Local");
            AddLocal("qwen3:14b", "Local", "Qwen3 14B (Ollama)", false, 128000, false, "", "", "Local");
            AddLocal("llama3.1:8b", "Local", "Llama 3.1 8B (Ollama)", false, 128000, false, "", "", "Local");
            AddLocal("llama3.2:3b", "Local", "Llama 3.2 3B (Ollama)", false, 128000, false, "", "", "Local");
            AddLocal("llama3.2-vision:11b", "Local", "Llama 3.2 Vision 11B (Ollama)", true, 128000, false, "", "", "Local");
            AddLocal("phi3:mini", "Local", "Phi-3 Mini (Ollama)", false, 128000, false, "", "", "Local");
            AddLocal("phi4:14b", "Local", "Phi-4 14B (Ollama)", false, 128000, false, "", "", "Local");
            AddLocal("mistral:7b", "Local", "Mistral 7B (Ollama)", false, 32768, false, "", "", "Local");
            AddLocal("mistral-nemo:12b", "Local", "Mistral Nemo 12B (Ollama)", false, 128000, false, "", "", "Local");
            AddLocal("gemma2:9b", "Local", "Gemma 2 9B (Ollama)", false, 8192, false, "", "", "Local");
            AddLocal("gemma3:4b", "Local", "Gemma 3 4B (Ollama)", false, 128000, false, "", "", "Local");
            AddLocal("gemma3:12b", "Local", "Gemma 3 12B (Ollama)", false, 128000, false, "", "", "Local");
            AddLocal("qwen2.5:7b", "Local", "Qwen 2.5 7B (Ollama)", false, 32768, false, "", "", "Local");
            AddLocal("qwen2.5-coder:7b", "Local", "Qwen 2.5 Coder 7B (Ollama)", false, 32768, false, "", "", "Local");
            AddLocal("deepseek-r1:7b", "Local", "DeepSeek R1 7B (Ollama)", false, 128000, false, "", "", "Local");
            AddLocal("deepseek-r1:14b", "Local", "DeepSeek R1 14B (Ollama)", false, 128000, false, "", "", "Local");
            AddLocal("nomic-embed-text", "Local", "Nomic Embed Text (Ollama)", false, 8192, false, "", "", "Local");
        }

        static void AddLocal(string id, string providerId, string displayName, bool vision, int ctx, bool needsKey, string keyLabel, string keyUrl, string endpoint)
        {
            var m = new ModelInfo { Id = id, ProviderId = providerId, DisplayName = displayName, IsFree = true, SupportsVision = vision, ContextLength = ctx, RequiresApiKey = false, ApiKeyLabel = "", ApiKeyUrl = "", Endpoint = endpoint };
            All.Add(m);
            Local.Add(m);
        }

        public static List<ModelInfo> GetFreeOrUsable()
        {
            var result = new List<ModelInfo>();
            result.AddRange(Free);
            result.AddRange(Local);
            return result;
        }

        public static List<ModelInfo> GetAllOrdered()
        {
            var result = new List<ModelInfo>();
            result.AddRange(Free);
            result.AddRange(Local);
            result.AddRange(Paid);
            return result;
        }

        public static List<ModelInfo> ByProvider(string providerId)
        {
            return All.FindAll(m => m.ProviderId == providerId);
        }
    }
}
