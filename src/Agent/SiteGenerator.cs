using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ZhuaQianDesktopApp.Agent
{
    // Generates a small static website (index.html + style.css + app.js) by asking
    // the LLM to emit fenced code blocks. Pure orchestration: the actual file writes
    // are performed by the caller (typically through WriteFileExecutor) so they stay
    // audited and gated. No LLM dependency lives here -- the host injects a chat func.
    public static class SiteGenerator
    {
        public sealed class SiteFile
        {
            public string Path;    // relative path, e.g. "index.html"
            public string Content;
        }

        const string SystemPrompt =
            "You are a senior front-end engineer. Given a website goal, produce a clean, " +
            "self-contained static site. Respond with fenced code blocks, one per file, using " +
            "the language tag to name the file: ```html for index.html, ```css for style.css, " +
            "```js for app.js, ```json for data.json. Make it responsive and production-quality. " +
            "Output ONLY the code blocks, no extra prose.";

        public static async Task<List<SiteFile>> GenerateAsync(string goal, Func<List<Dictionary<string, object>>, Task<string>> chat)
        {
            if (chat == null) throw new InvalidOperationException("SiteGenerator requires a chat function");
            string reply = await chat(LlmBridge.Conversation(SystemPrompt, "Goal: " + goal)).ConfigureAwait(false);
            return ParseFiles(reply);
        }

        // Visible for unit testing: splits a model reply into {filename, content} by
        // reading ```lang ... ``` fences and mapping the language tag to a file name.
        public static List<SiteFile> ParseFiles(string reply)
        {
            var files = new List<SiteFile>();
            if (string.IsNullOrEmpty(reply)) return files;
            var matches = Regex.Matches(reply, "```([a-zA-Z0-9]+)\\s*\\n(.*?)```", RegexOptions.Singleline);
            foreach (Match m in matches)
            {
                string lang = m.Groups[1].Value.ToLowerInvariant();
                string content = m.Groups[2].Value;
                string name = MapLanguageToFile(lang);
                if (name == null) continue;
                files.Add(new SiteFile { Path = name, Content = content });
            }
            // No fences -> treat the whole reply as index.html (last resort).
            if (files.Count == 0)
                files.Add(new SiteFile { Path = "index.html", Content = reply });
            return files;
        }

        static string MapLanguageToFile(string lang)
        {
            switch (lang)
            {
                case "html":
                case "htm": return "index.html";
                case "css": return "style.css";
                case "js":
                case "javascript":
                case "ts": return "app.js";
                case "json": return "data.json";
                case "md": return "README.md";
                default: return null;
            }
        }
    }
}
