using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp.Agent
{
    // Multi-host open-source publishing. A publish = create a (public) repo on a
    // git host via its REST API, then push the local project with git CLI using a
    // token-in-URL remote (so no credential helper / plaintext file is needed at
    // push time). All side effects run inside the AgentPipeline single gate.
    //
    // Supported hosts (IGitHost abstraction so more can be added later):
    //   github  -> https://api.github.com        (PAT in Authorization: token ...)
    //   gitee   -> https://gitee.com/api/v5       (PAT in access_token body field)
    //   gitlab  -> https://gitlab.com/api/v4      (PAT in PRIVATE-TOKEN header)

    public interface IGitHost
    {
        string Name { get; }
        // HTTPS push URL with the PAT embedded (git remote add origin <this>).
        string AuthenticatedPushUrl(string token, string userName, string repoName);
        // Resolve the authenticated user's login (used to build the push URL).
        Task<string> GetUserNameAsync(string token);
        // Create the repo via REST. CloneUrl is best-effort; push proceeds regardless.
        Task<HostCreateResult> CreateRepoAsync(string token, string userName, string repoName, string description, bool isPublic);
    }

    public sealed class HostCreateResult
    {
        public bool Ok;
        public string Message;
        public string CloneUrl;
        public static HostCreateResult Success(string cloneUrl) { return new HostCreateResult { Ok = true, CloneUrl = cloneUrl ?? "" }; }
        public static HostCreateResult Fail(string msg) { return new HostCreateResult { Ok = false, Message = msg ?? "" }; }
    }

    public sealed class GitHubHost : IGitHost
    {
        public string Name { get { return "github"; } }
        public string ApiBase { get { return "https://api.github.com"; } }

        public string AuthenticatedPushUrl(string token, string userName, string repoName)
        {
            return "https://" + Uri.EscapeDataString(token) + "@github.com/" + userName + "/" + repoName + ".git";
        }

        public async Task<string> GetUserNameAsync(string token)
        {
            var get = await GitHostHttp.GetJsonAsync(ApiBase + "/user",
                new Dictionary<string, string> { { "Authorization", "token " + token }, { "User-Agent", "ZhuaQianDesktop" } }).ConfigureAwait(false);
            if (get.Ok && get.Data != null && get.Data.ContainsKey("login"))
                return Convert.ToString(get.Data["login"]);
            return "";
        }

        public async Task<HostCreateResult> CreateRepoAsync(string token, string userName, string repoName, string description, bool isPublic)
        {
            var body = new Dictionary<string, object> { { "name", repoName }, { "private", !isPublic }, { "auto_init", false } };
            if (!string.IsNullOrEmpty(description)) body["description"] = description;
            var headers = new Dictionary<string, string> { { "Authorization", "token " + token }, { "Accept", "application/vnd.github+json" }, { "User-Agent", "ZhuaQianDesktop" } };
            return await GitHostHttp.PostJsonAsync(ApiBase + "/user/repos", headers,
                new JavaScriptSerializer().Serialize(body), new[] { "clone_url", "ssh_url", "html_url" }).ConfigureAwait(false);
        }
    }

    public sealed class GiteeHost : IGitHost
    {
        public string Name { get { return "gitee"; } }
        public string ApiBase { get { return "https://gitee.com/api/v5"; } }

        public string AuthenticatedPushUrl(string token, string userName, string repoName)
        {
            return "https://" + Uri.EscapeDataString(token) + "@gitee.com/" + userName + "/" + repoName + ".git";
        }

        public async Task<string> GetUserNameAsync(string token)
        {
            var get = await GitHostHttp.GetJsonAsync(ApiBase + "/user?access_token=" + Uri.EscapeDataString(token),
                new Dictionary<string, string> { { "User-Agent", "ZhuaQianDesktop" } }).ConfigureAwait(false);
            if (get.Ok && get.Data != null && get.Data.ContainsKey("login"))
                return Convert.ToString(get.Data["login"]);
            return "";
        }

        public async Task<HostCreateResult> CreateRepoAsync(string token, string userName, string repoName, string description, bool isPublic)
        {
            var body = new Dictionary<string, object> { { "name", repoName }, { "private", !isPublic }, { "access_token", token } };
            if (!string.IsNullOrEmpty(description)) body["description"] = description;
            var headers = new Dictionary<string, string> { { "User-Agent", "ZhuaQianDesktop" } };
            return await GitHostHttp.PostJsonAsync(ApiBase + "/user/repos", headers,
                new JavaScriptSerializer().Serialize(body), new[] { "clone_url", "git_url", "html_url" }).ConfigureAwait(false);
        }
    }

    public sealed class GitLabHost : IGitHost
    {
        public string Name { get { return "gitlab"; } }
        public string ApiBase { get { return "https://gitlab.com/api/v4"; } }

        // GitLab HTTPS push uses the special user "oauth2" with the PAT as password.
        public string AuthenticatedPushUrl(string token, string userName, string repoName)
        {
            return "https://oauth2:" + Uri.EscapeDataString(token) + "@gitlab.com/" + userName + "/" + repoName + ".git";
        }

        // Not needed for the push URL (hardcoded oauth2); keep for symmetry.
        public Task<string> GetUserNameAsync(string token) { return Task.FromResult("oauth2"); }

        public async Task<HostCreateResult> CreateRepoAsync(string token, string userName, string repoName, string description, bool isPublic)
        {
            var body = new Dictionary<string, object> { { "name", repoName }, { "visibility", isPublic ? "public" : "private" } };
            if (!string.IsNullOrEmpty(description)) body["description"] = description;
            var headers = new Dictionary<string, string> { { "PRIVATE-TOKEN", token }, { "User-Agent", "ZhuaQianDesktop" } };
            return await GitHostHttp.PostJsonAsync(ApiBase + "/projects", headers,
                new JavaScriptSerializer().Serialize(body), new[] { "http_url_to_repo", "ssh_url_to_repo", "web_url" }).ConfigureAwait(false);
        }
    }

    // Small HTTP helpers shared by the hosts. Uses JavaScriptSerializer (csc-safe,
    // no System.Text.Json dependency) and HttpClient (.NET 4.8 built-in).
    internal static class GitHostHttp
    {
        static GitHostHttp()
        {
            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls; }
            catch (Exception ex) { Debug.WriteLine("GitHostHttp TLS: " + ex.Message); }
        }

        public sealed class JsonResult
        {
            public bool Ok;
            public Dictionary<string, object> Data;
            public string Raw;
            public string Error;
        }

        public static async Task<JsonResult> GetJsonAsync(string url, Dictionary<string, string> headers)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.TryParseAdd("User-Agent", "ZhuaQianDesktop");
                    if (headers != null) foreach (var h in headers) client.DefaultRequestHeaders.TryParseAdd(h.Key, h.Value);
                    string text = await client.GetStringAsync(url).ConfigureAwait(false);
                    return new JsonResult { Ok = true, Raw = text, Data = Parse(text) };
                }
            }
            catch (Exception ex) { return new JsonResult { Ok = false, Error = ex.Message }; }
        }

        public static async Task<HostCreateResult> PostJsonAsync(string url, Dictionary<string, string> headers, string jsonBody, string[] cloneUrlFields)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.TryParseAdd("User-Agent", "ZhuaQianDesktop");
                    if (headers != null) foreach (var h in headers) client.DefaultRequestHeaders.TryParseAdd(h.Key, h.Value);
                    var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    var resp = await client.PostAsync(url, content).ConfigureAwait(false);
                    string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        return HostCreateResult.Fail("create repo failed (" + (int)resp.StatusCode + "): " + Truncate(text));
                    string clone = ExtractField(text, cloneUrlFields) ?? ExtractField(text, new[] { "html_url", "web_url" });
                    return HostCreateResult.Success(clone);
                }
            }
            catch (Exception ex) { return HostCreateResult.Fail("create repo error: " + ex.Message); }
        }

        static Dictionary<string, object> Parse(string text)
        {
            try { return new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>; }
            catch (Exception ex) { Debug.WriteLine("GitHostHttp.Parse: " + ex.Message); return null; }
        }

        static string ExtractField(string text, string[] fields)
        {
            var d = Parse(text);
            if (d == null) return null;
            foreach (var f in fields)
                if (d.ContainsKey(f) && d[f] != null) return Convert.ToString(d[f]);
            return null;
        }

        static string Truncate(string s, int n = 300)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "...";
        }
    }

    // Executor that performs the publish. Reads the PAT from ConfigStore
    // (<host>_token) so the token is never passed through command parameters
    // (which may be written to the audit log). Everything runs after the
    // PermissionGate approval, satisfying the single-pipeline rule.
    public sealed class GitHostPublisherExecutor : IAsyncCommandExecutor
    {
        readonly string configDir;

        public GitHostPublisherExecutor(string configDir = null)
        {
            this.configDir = configDir ?? "";
        }

        public string CommandType { get { return "PublishRepo"; } }

        public CommandResult Execute(IAgentCommand command)
        {
            try { return ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex) { return CommandResult.Failed(ex.Message); }
        }

        public async Task<CommandResult> ExecuteAsync(IAgentCommand command, CancellationToken token)
        {
            string hostName = GetStr(command, "host").ToLowerInvariant();
            if (string.IsNullOrEmpty(hostName)) hostName = "github";
            IGitHost host = ResolveHost(hostName);
            if (host == null) return CommandResult.Failed("unsupported host: " + hostName);

            string localPath = GetStr(command, "localPath");
            string repoName = GetStr(command, "repoName");
            string userName = GetStr(command, "userName");
            bool isPublic = !"false".Equals(GetStr(command, "isPublic"), StringComparison.OrdinalIgnoreCase);
            string description = GetStr(command, "description");

            string tokenVal = LoadToken(hostName);
            if (string.IsNullOrEmpty(tokenVal))
                return CommandResult.Failed("No PAT found for " + hostName + ". Store it once via: /settoken host=" + hostName + " token=YOUR_TOKEN  (kept in local config only)");

            if (string.IsNullOrEmpty(localPath) || !Directory.Exists(localPath))
                return CommandResult.Failed("local project folder not found: " + localPath);
            if (string.IsNullOrEmpty(repoName))
                repoName = Path.GetFileName(localPath.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(repoName))
                return CommandResult.Failed("repoName is required");

            var sb = new StringBuilder();
            try
            {
                if (string.IsNullOrEmpty(userName))
                {
                    try { userName = await host.GetUserNameAsync(tokenVal).ConfigureAwait(false); } catch (Exception ex) { Debug.WriteLine("GetUserName: " + ex.Message); }
                }
                if (string.IsNullOrEmpty(userName)) userName = "me";

                var created = await host.CreateRepoAsync(tokenVal, userName, repoName, description, isPublic).ConfigureAwait(false);
                string pushUrl = host.AuthenticatedPushUrl(tokenVal, userName, repoName);
                sb.AppendLine("Repo: " + userName + "/" + repoName + " (" + (isPublic ? "public" : "private") + ") on " + hostName);
                sb.AppendLine("Create: " + (created.Ok ? "ok" : "skipped (" + created.Message + ")"));
                sb.AppendLine("Push URL: " + MaskUrl(pushUrl));

                string outMsg;
                if (!RunGit(localPath, "rev-parse --is-inside-work-tree", out outMsg))
                {
                    if (!RunGit(localPath, "init", out outMsg)) sb.AppendLine("git init failed: " + outMsg);
                    RunGit(localPath, "config user.email \"zhuaqian@local\"", out outMsg);
                    RunGit(localPath, "config user.name \"ZhuaQian\"", out outMsg);
                }
                EnsureGitignore(localPath);
                RunGit(localPath, "add -A", out outMsg);
                string status; RunGit(localPath, "status --porcelain", out status);
                if (!string.IsNullOrWhiteSpace(status.Trim()))
                    RunGit(localPath, "commit -m \"Initial open-source publish by ZhuaQian\"", out outMsg);
                else if (!RunGit(localPath, "rev-parse --verify HEAD", out outMsg))
                    RunGit(localPath, "commit --allow-empty -m \"Initial open-source publish by ZhuaQian\"", out outMsg);

                RunGit(localPath, "branch -M main", out outMsg);
                RunGit(localPath, "remote remove origin", out outMsg);
                if (!RunGit(localPath, "remote add origin " + pushUrl, out outMsg))
                    sb.AppendLine("remote add failed: " + outMsg);
                if (!RunGit(localPath, "push -u origin main", out outMsg))
                    RunGit(localPath, "push -u origin HEAD", out outMsg);
                sb.AppendLine("Push: " + outMsg);

                return CommandResult.Ok(null, false, null, "PublishRepo", 0, sb.ToString().Trim());
            }
            catch (Exception ex)
            {
                sb.AppendLine("publish failed: " + ex.Message);
                return CommandResult.Failed(sb.ToString().Trim());
            }
        }

        string LoadToken(string host)
        {
            try
            {
                var store = new ConfigStore(configDir);
                store.Load();
                return store.Get(host + "_token", "");
            }
            catch (Exception ex) { Debug.WriteLine("GitHostPublisher.LoadToken: " + ex.Message); return ""; }
        }

        static bool RunGit(string workDir, string args, out string output)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = workDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    output = (o + e).Trim();
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex) { output = ex.Message; return false; }
        }

        static IGitHost ResolveHost(string name)
        {
            if (name.Contains("gitee")) return new GiteeHost();
            if (name.Contains("gitlab")) return new GitLabHost();
            return new GitHubHost();
        }

        static string MaskUrl(string url)
        {
            // Hide the embedded token: https://****@host/...
            int at = url.IndexOf('@');
            int slash = url.IndexOf("://");
            if (at > 0 && slash >= 0)
                return url.Substring(0, slash + 3) + "****" + url.Substring(at);
            return url;
        }

        // Before staging everything, make sure the published repo does not carry
        // build artifacts. If the user's project has no .gitignore, write a sane
        // default so bin/obj/packages/node_modules/etc. are not pushed upstream.
        static void EnsureGitignore(string localPath)
        {
            try
            {
                string gi = Path.Combine(localPath, ".gitignore");
                if (File.Exists(gi)) return;
                var sb = new StringBuilder();
                sb.AppendLine("# Auto-generated by ZhuaQianDesktop open-source publish");
                sb.AppendLine("bin/");
                sb.AppendLine("obj/");
                sb.AppendLine("dist/");
                sb.AppendLine("generated/");
                sb.AppendLine("build/");
                sb.AppendLine("packages/");
                sb.AppendLine("node_modules/");
                sb.AppendLine("*.user");
                sb.AppendLine("*.suo");
                sb.AppendLine("Thumbs.db");
                sb.AppendLine("*.exe");
                sb.AppendLine("*.dll");
                sb.AppendLine("*.pdb");
                File.WriteAllText(gi, sb.ToString());
            }
            catch (Exception ex) { Debug.WriteLine("GitHostPublisher.EnsureGitignore: " + ex.Message); }
        }

        static string GetStr(IAgentCommand c, string key)
        {
            object v;
            if (c.Parameters != null && c.Parameters.TryGetValue(key, out v) && v != null) return v.ToString();
            return "";
        }
    }
}
