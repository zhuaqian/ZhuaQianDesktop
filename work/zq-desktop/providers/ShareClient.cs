using System;
using System.Net;
using System.Text;

namespace ZhuaQianDesktopApp.Providers
{
    /// <summary>
    /// 自托管中继客户端：把 .zqp 字节上传到用户的 Relay，拿到分享链接。
    /// 中继只转发密文（零知识）：若包已加密，服务器无法读取内容。
    /// 协议（与 relay/relay.ps1 对应）：
    ///   POST {base}/upload  (body = 原始字节, Content-Type: application/octet-stream) -> 纯文本 id
    ///   GET  {base}/{id}    -> 返回字节（Import from URL 直接复用）
    /// </summary>
    public static class ShareClient
    {
        public static string Upload(byte[] data, string relayBaseUrl)
        {
            if (data == null || data.Length == 0) throw new ArgumentException("No data to upload.");
            if (string.IsNullOrWhiteSpace(relayBaseUrl)) throw new ArgumentException("Relay URL 未设置。");

            string baseUrl = relayBaseUrl.Trim();
            if (baseUrl.EndsWith("/")) baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);
            string url = baseUrl + "/upload";

            using (var wc = new WebClient())
            {
                wc.Headers["Content-Type"] = "application/octet-stream";
                byte[] resp = wc.UploadData(url, "POST", data);
                string id = Encoding.UTF8.GetString(resp).Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(id))
                    throw new Exception("Relay 未返回有效的分享 ID。");
                return baseUrl + "/" + id;
            }
        }

        public static bool IsRelayUrl(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && (text.IndexOf("/upload", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("/pull/", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ---- Live session (near-real-time polling over relay) ----

        public static void PublishSession(string relayBaseUrl, string sessionId, byte[] snapshot)
        {
            if (snapshot == null || snapshot.Length == 0) throw new ArgumentException("No snapshot to publish.");
            if (string.IsNullOrWhiteSpace(relayBaseUrl) || string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Relay URL / session id required.");
            string baseUrl = relayBaseUrl.Trim();
            if (baseUrl.EndsWith("/")) baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);
            string url = baseUrl + "/session/" + sessionId;
            using (var wc = new WebClient())
            {
                wc.Headers["Content-Type"] = "application/octet-stream";
                wc.UploadData(url, "POST", snapshot);
            }
        }

        public static byte[] FetchSession(string relayBaseUrl, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(relayBaseUrl) || string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Relay URL / session id required.");
            string baseUrl = relayBaseUrl.Trim();
            if (baseUrl.EndsWith("/")) baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);
            string url = baseUrl + "/session/" + sessionId;
            using (var wc = new WebClient())
                return wc.DownloadData(url);
        }

        public static string BuildSessionUrl(string relayBaseUrl, string sessionId)
        {
            string baseUrl = (relayBaseUrl ?? "").Trim();
            if (baseUrl.EndsWith("/")) baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);
            return baseUrl + "/session/" + sessionId;
        }

        public static bool TryParseSessionUrl(string url, out string relayBaseUrl, out string sessionId)
        {
            relayBaseUrl = "";
            sessionId = "";
            if (string.IsNullOrWhiteSpace(url)) return false;
            int idx = url.IndexOf("/session/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            relayBaseUrl = url.Substring(0, idx);
            sessionId = url.Substring(idx + "/session/".Length).Trim('/');
            return sessionId.Length > 0;
        }
    }
}
