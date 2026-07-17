using System.Collections.Generic;
using System.Text;
using ZhuaQianDesktopApp.Tools;

namespace ZhuaQianDesktopApp.Agent
{
    public sealed class WebSearchExecutor : ICommandExecutor
    {
        readonly WebSearchClient client;
        readonly int maxResults;

        public WebSearchExecutor(WebSearchClient client, int maxResults = 5)
        {
            this.client = client ?? new WebSearchClient();
            this.maxResults = maxResults <= 0 ? 5 : maxResults;
        }

        public string CommandType { get { return "WebSearch"; } }

        public CommandResult Execute(IAgentCommand command)
        {
            string query = command.Target ?? "";
            object value;
            if (string.IsNullOrWhiteSpace(query)
                && command.Parameters != null
                && command.Parameters.TryGetValue("query", out value)
                && value != null)
                query = value.ToString();

            if (string.IsNullOrWhiteSpace(query))
                return CommandResult.Failed("web search query is empty");

            WebSearchResponse response = client.SearchDetailed(query, maxResults);
            List<WebSearchResult> results = response.Results;
            var sb = new StringBuilder();
            sb.AppendLine("Web search results for: " + query);
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
                sb.AppendLine("Search warning: " + response.ErrorMessage);
            if (results.Count == 0)
            {
                sb.AppendLine("No results.");
            }
            else
            {
                for (int i = 0; i < results.Count; i++)
                {
                    var r = results[i];
                    sb.AppendLine((i + 1).ToString() + ". " + r.Title);
                    sb.AppendLine("URL: " + r.Url);
                    if (!string.IsNullOrWhiteSpace(r.Snippet)) sb.AppendLine("Snippet: " + r.Snippet);
                }
            }
            return CommandResult.Ok(null, false, null, "web-search", 0, sb.ToString().Trim());
        }
    }
}
