using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ZhuaQianDesktopApp.Agent
{
    // A single, host-owned bridge to the LLM so executors/strategies that need
    // model help (SiteGenerator, ModelFixStrategy) never reach into the WinForms
    // composition root. The host (MainForm) assigns Chat at startup, exactly like
    // LlmTaskPolicy's chat-function contract. This keeps the model call behind one
    // injection point and leaves executors fully decoupled from any provider.
    public static class LlmBridge
    {
        // Signature matches ProviderManager.SendAsync: takes native messages and
        // returns the model's reply text.
        public static Func<List<Dictionary<string, object>>, Task<string>> Chat { get; set; }

        public static bool IsAvailable { get { return Chat != null; } }

        public static async Task<string> AskAsync(List<Dictionary<string, object>> messages)
        {
            if (Chat == null)
                throw new InvalidOperationException("LlmBridge.Chat is not configured. Open Settings and select a model.");
            return await Chat(messages).ConfigureAwait(false);
        }

        public static List<Dictionary<string, object>> Conversation(string system, string user)
        {
            var list = new List<Dictionary<string, object>>();
            if (!string.IsNullOrEmpty(system))
                list.Add(new Dictionary<string, object> { { "role", "system" }, { "content", system } });
            list.Add(new Dictionary<string, object> { { "role", "user" }, { "content", user } });
            return list;
        }
    }
}
