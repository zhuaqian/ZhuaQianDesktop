using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ZhuaQianDesktopApp.Core
{
    /// <summary>
    /// 局域网分享服务：基于 TcpListener 的最小 HTTP 服务，避免 HttpListener 的 URL ACL / 管理员权限问题。
    /// 仅服务当前持有的一个 .zqp 包，路径带随机 token；支持明文与已加密包（服务器零知识）。
    /// </summary>
    public class LanShareServer : IDisposable
    {
        TcpListener listener;
        Thread acceptThread;
        volatile bool running;
        byte[] payload;
        string token;
        int port;

        public string Url { get; private set; }
        public event Action<string> OnError;

        const int DefaultPort = 8801;

        public void Start(byte[] data, int port = DefaultPort)
        {
            if (data == null || data.Length == 0) throw new ArgumentException("No data to share.");
            this.payload = data;
            this.port = port;
            this.token = RandomToken(8);
            this.running = true;

            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Url = "http://" + GetLanIp() + ":" + port + "/" + token;

            acceptThread = new Thread(AcceptLoop) { IsBackground = true };
            acceptThread.Start();
        }

        public void Stop()
        {
            running = false;
            try { if (listener != null) listener.Stop(); } catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("LanShareServer.Stop: " + _ex.Message); }
        }

        void AcceptLoop()
        {
            while (running)
            {
                TcpClient client = null;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (Exception ex)
                {
                    if (!running) break;
                    System.Diagnostics.Debug.WriteLine("LanShareServer.AcceptLoop: " + ex.Message);
                    continue;
                }
                if (client == null) continue;
                var captured = client;
                ThreadPool.QueueUserWorkItem(_ => HandleClient(captured));
            }
        }

        void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var ns = client.GetStream())
                {
                    // 读取请求行（最小实现：仅取首行路径）
                    var buf = new byte[4096];
                    var request = new StringBuilder();
                    int total = 0;
                    // 先读首行
                    while (total < buf.Length)
                    {
                        int n = ns.Read(buf, total, buf.Length - total);
                        if (n <= 0) break;
                        total += n;
                        string chunk = Encoding.ASCII.GetString(buf, 0, total);
                        int lf = chunk.IndexOf('\n');
                        if (lf >= 0) { request.Append(chunk.Substring(0, lf)); break; }
                    }

                    string line = request.ToString().Trim();
                    string path = "";
                    var parts = line.Split(' ');
                    if (parts.Length >= 2) path = parts[1];

                    if (!string.IsNullOrEmpty(path) && path.Trim('/') == token)
                    {
                        SendFile(ns);
                    }
                    else
                    {
                        SendText(ns, 404, "Not Found");
                    }
                }
            }
            catch (Exception ex)
            {
                if (OnError != null) OnError(ex.Message);
            }
        }

        void SendFile(NetworkStream ns)
        {
            string header = "HTTP/1.1 200 OK\r\n"
                + "Content-Type: application/octet-stream\r\n"
                + "Content-Length: " + payload.Length + "\r\n"
                + "Content-Disposition: attachment; filename=\"zq-package.zqp\"\r\n"
                + "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            ns.Write(headerBytes, 0, headerBytes.Length);
            ns.Write(payload, 0, payload.Length);
            ns.Flush();
        }

        void SendText(NetworkStream ns, int code, string text)
        {
            string body = code + " " + text + "\r\n";
            string header = "HTTP/1.1 " + body + "Content-Type: text/plain\r\nContent-Length: "
                + Encoding.ASCII.GetByteCount(body) + "\r\nConnection: close\r\n\r\n";
            byte[] h = Encoding.ASCII.GetBytes(header);
            byte[] b = Encoding.ASCII.GetBytes(body);
            ns.Write(h, 0, h.Length);
            ns.Write(b, 0, b.Length);
            ns.Flush();
        }

        public static string GetLanIp()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
                }
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("LanShareServer.GetLanIp: " + _ex.Message); }
            return "127.0.0.1";
        }

        static string RandomToken(int len)
        {
            const string chars = "0123456789abcdef";
            var sb = new StringBuilder();
            using (var rng = new RNGCryptoServiceProvider())
            {
                var bytes = new byte[len];
                rng.GetBytes(bytes);
                foreach (byte b in bytes)
                    sb.Append(chars[b % chars.Length]);
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
