using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace ZhuaQianDesktopApp.Tools
{
    public class WebSearchResult
    {
        public string Title;
        public string Url;
        public string Snippet;
    }

    public class WebSearchResponse
    {
        public bool Success;
        public string Provider;
        public string ErrorMessage;
        public bool FallbackUsed;
        public List<WebSearchResult> Results = new List<WebSearchResult>();
    }

    public class WebPageFetchResult
    {
        public bool Success;
        public string Url;
        public string Title;
        public string Text;
        // Full rendered HTML, populated only when a browser-render fetch requests it.
        // Absent for static HTTP fetches; report builder ignores it when empty.
        public string Html;
        public string ErrorMessage;
    }

    public class WebSearchClient
    {
        public List<WebSearchResult> Search(string query, int maxResults)
        {
            return SearchDetailed(query, maxResults).Results;
        }

        public WebSearchResponse SearchDetailed(string query, int maxResults)
        {
            var response = new WebSearchResponse();
            if (string.IsNullOrWhiteSpace(query))
            {
                response.ErrorMessage = "Search query is empty.";
                return response;
            }
            string firstError = "";
            try
            {
                var results = SearchBingRss(query, maxResults);
                if (results.Count > 0)
                {
                    response.Success = true;
                    response.Provider = "Bing RSS";
                    response.Results = results;
                    return response;
                }
                firstError = "Bing RSS returned no results.";
            }
            catch (Exception ex)
            {
                firstError = "Bing RSS failed: " + ex.Message;
                // Fall through to the no-key HTML endpoint.
            }
            try
            {
                var results = SearchDuckDuckGoHtml(query, maxResults);
                response.Success = results.Count > 0;
                response.Provider = "DuckDuckGo HTML";
                response.FallbackUsed = true;
                response.Results = results;
                if (!response.Success) response.ErrorMessage = firstError + " DuckDuckGo HTML returned no results.";
                return response;
            }
            catch (Exception ex)
            {
                response.Provider = "DuckDuckGo HTML";
                response.FallbackUsed = true;
                response.ErrorMessage = firstError + " DuckDuckGo HTML failed: " + ex.Message;
                return response;
            }
        }

        public WebPageFetchResult FetchPage(string url, int maxChars)
        {
            var result = new WebPageFetchResult { Url = CleanUrl(url) };
            string validationError;
            if (!ValidatePublicHttpUrl(result.Url, out validationError))
            {
                result.ErrorMessage = validationError;
                return result;
            }

            try
            {
                string html = Download(result.Url);
                string title = Clean(FirstMatch(html, "<title[^>]*>(.*?)</title>"));
                string text = ExtractReadableText(html);
                if (string.IsNullOrWhiteSpace(text))
                {
                    result.ErrorMessage = "No readable text was extracted from the page.";
                    return result;
                }

                result.Success = true;
                result.Title = title;
                result.Text = TrimText(text, maxChars);
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public bool ValidatePublicHttpUrl(string url, out string error)
        {
            error = "";
            Uri uri;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                error = "Invalid URL.";
                return false;
            }
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                error = "Only http/https URLs are allowed.";
                return false;
            }
            if (!uri.IsDefaultPort && uri.Port != 80 && uri.Port != 443)
            {
                error = "Blocked URL: only default web ports 80/443 are allowed.";
                return false;
            }
            if (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                error = "Blocked URL: local host targets are not allowed.";
                return false;
            }

            IPAddress literal;
            if (IPAddress.TryParse(uri.Host, out literal))
                return ValidatePublicAddress(literal, out error);

            try
            {
                var addresses = Dns.GetHostAddresses(uri.Host);
                if (addresses == null || addresses.Length == 0)
                {
                    error = "DNS lookup returned no address.";
                    return false;
                }
                foreach (var address in addresses)
                {
                    if (!ValidatePublicAddress(address, out error))
                        return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "DNS lookup failed: " + ex.Message;
                return false;
            }
        }

        bool ValidatePublicAddress(IPAddress address, out string error)
        {
            error = "";
            if (address == null)
            {
                error = "DNS lookup returned an empty address.";
                return false;
            }
            if (IPAddress.IsLoopback(address))
            {
                error = "Blocked URL: loopback address is not allowed.";
                return false;
            }
            byte[] b = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                if (b[0] == 10
                    || b[0] == 127
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    || (b[0] == 192 && b[1] == 168)
                    || (b[0] == 169 && b[1] == 254)
                    || b[0] == 0)
                {
                    error = "Blocked URL: private, link-local, or reserved IPv4 address is not allowed.";
                    return false;
                }
            }
            else if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                    || address.Equals(IPAddress.IPv6Loopback)
                    || (b[0] & 0xFE) == 0xFC)
                {
                    error = "Blocked URL: private, link-local, or local IPv6 address is not allowed.";
                    return false;
                }
            }
            return true;
        }

        List<WebSearchResult> SearchBingRss(string query, int maxResults)
        {
            string url = "https://www.bing.com/search?format=rss&q=" + Uri.EscapeDataString(query);
            string xml = Download(url);
            var results = new List<WebSearchResult>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(xml, "<item>(.*?)</item>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string item = m.Groups[1].Value;
                string title = FirstMatch(item, "<title>(.*?)</title>");
                string link = FirstMatch(item, "<link>(.*?)</link>");
                string desc = FirstMatch(item, "<description>(.*?)</description>");
                AddResult(results, seen, title, link, desc, maxResults);
                if (results.Count >= maxResults) break;
            }
            return results;
        }

        List<WebSearchResult> SearchDuckDuckGoHtml(string query, int maxResults)
        {
            string url = "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query);
            string html = Download(url);
            var results = new List<WebSearchResult>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(html, "<a[^>]+class=\"result__a\"[^>]+href=\"([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string link = Decode(m.Groups[1].Value);
                string title = StripTags(m.Groups[2].Value);
                string tail = html.Substring(Math.Min(html.Length, m.Index), Math.Min(1200, html.Length - Math.Min(html.Length, m.Index)));
                string desc = StripTags(FirstMatch(tail, "<a[^>]+class=\"result__snippet\"[^>]*>(.*?)</a>"));
                AddResult(results, seen, title, link, desc, maxResults);
                if (results.Count >= maxResults) break;
            }
            return results;
        }

        string Download(string url)
        {
            try { ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("WebSearchClient TLS setup: " + ex.Message); }

            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ZhuaQianDesktop/0.1";
                wc.Headers[HttpRequestHeader.Accept] = "text/html,application/xhtml+xml,application/xml;q=0.9,text/plain;q=0.8,*/*;q=0.5";
                wc.Headers[HttpRequestHeader.AcceptLanguage] = "zh-CN,zh;q=0.9,en;q=0.8";
                return wc.DownloadString(url);
            }
        }

        void AddResult(List<WebSearchResult> results, HashSet<string> seen, string title, string url, string snippet, int maxResults)
        {
            title = Clean(title);
            url = CleanUrl(url);
            snippet = Clean(snippet);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url)) return;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            if (seen.Contains(url)) return;
            seen.Add(url);
            results.Add(new WebSearchResult { Title = title, Url = url, Snippet = snippet });
            while (results.Count > maxResults) results.RemoveAt(results.Count - 1);
        }

        string FirstMatch(string text, string pattern)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value : "";
        }

        string Clean(string value)
        {
            return Regex.Replace(Decode(StripTags(value ?? "")), "\\s+", " ").Trim();
        }

        public string CleanUrl(string value)
        {
            string url = Decode(value ?? "").Trim();
            int uddg = url.IndexOf("uddg=", StringComparison.OrdinalIgnoreCase);
            if (uddg >= 0)
            {
                string encoded = url.Substring(uddg + 5);
                int amp = encoded.IndexOf('&');
                if (amp >= 0) encoded = encoded.Substring(0, amp);
                url = Uri.UnescapeDataString(encoded);
            }
            if (Regex.IsMatch(url, @"^(?:www\.)?[a-z0-9][a-z0-9-]*(?:\.[a-z0-9][a-z0-9-]*)*\.(?:com|cn|net|org|io|ai|app|dev|top|shop|site|xyz|cc|co|info|biz|me|tv|edu|gov)(?::\d{1,5})?(?:/.*)?$", RegexOptions.IgnoreCase))
                url = "https://" + url;
            return url;
        }

        string StripTags(string value)
        {
            return Regex.Replace(value ?? "", "<.*?>", " ");
        }

        string Decode(string value)
        {
            return WebUtility.HtmlDecode(value ?? "");
        }

        string ExtractReadableText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";
            string text = html;
            text = Regex.Replace(text, "<script\\b[^>]*>.*?</script>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "<style\\b[^>]*>.*?</style>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "<noscript\\b[^>]*>.*?</noscript>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "<(br|p|div|section|article|header|footer|li|tr|h[1-6])\\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            text = StripTags(text);
            text = Decode(text);
            text = Regex.Replace(text, @"[ \t\f\v]+", " ");
            text = Regex.Replace(text, @"\n\s+", "\n");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
        }

        string TrimText(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (maxChars <= 0 || value.Length <= maxChars) return value;
            return value.Substring(0, maxChars).TrimEnd() + "\n[Truncated at " + maxChars.ToString() + " chars]";
        }
    }
}
