using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Providers;

namespace ZhuaQianDesktopApp
{
    public partial class MainForm : Form
    {
        string ExtractRetryDelay(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail)) return "";
            var m = Regex.Match(detail, "\"retryDelay\"\\s*:\\s*\"([^\"]+)\"");
            if (m.Success) return "Retry after about " + m.Groups[1].Value + ". ";
            m = Regex.Match(detail, "Please retry in ([0-9.]+s)");
            if (m.Success) return "Retry after about " + m.Groups[1].Value + ". ";
            return "";
        }

        bool IsModelNotFoundError(string message)
        {
            if (message == null) return false;
            return message.Contains("(404)") || message.IndexOf("NOT_FOUND", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("no longer available", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        bool IsRetryableModelError(string message)
        {
            if (IsModelNotFoundError(message)) return true;
            if (message == null) return false;
            return message.Contains("(503)") || message.IndexOf("quota/rate limit", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("No candidates returned", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("No text response received", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        string ExtractReply(Dictionary<string, object> resp)
        {
            if (resp == null) throw new Exception("No candidates returned. Raw response is null.");
            if (!resp.ContainsKey("candidates")) throw new Exception("No candidates returned. Raw response: " + json.Serialize(resp));
            var candidates = ToObjectList(resp["candidates"]);
            if (candidates == null || candidates.Count == 0) throw new Exception("No candidates returned. Raw response: " + json.Serialize(resp));
            var cand = candidates[0] as Dictionary<string, object>;
            var content = cand != null && cand.ContainsKey("content") ? cand["content"] as Dictionary<string, object> : null;
            var parts = content != null && content.ContainsKey("parts") ? ToObjectList(content["parts"]) : null;
            if (parts == null) throw new Exception("No text response received. Raw response: " + json.Serialize(resp));
            var sb = new StringBuilder();
            foreach (var partObj in parts)
            {
                var part = partObj as Dictionary<string, object>;
                if (part != null && part.ContainsKey("text")) sb.Append(Convert.ToString(part["text"]));
            }
            if (sb.Length == 0) throw new Exception("No text response received. Raw response: " + json.Serialize(resp));
            return sb.ToString();
        }

        List<object> ToObjectList(object value)
        {
            if (value == null) return null;
            var list = new List<object>();
            var arrayList = value as ArrayList;
            if (arrayList != null)
            {
                foreach (var item in arrayList) list.Add(item);
                return list;
            }
            var objectArray = value as object[];
            if (objectArray != null)
            {
                foreach (var item in objectArray) list.Add(item);
                return list;
            }
            var enumerable = value as IEnumerable;
            if (enumerable != null && !(value is string))
            {
                foreach (var item in enumerable) list.Add(item);
                return list;
            }
            return null;
        }

        Dictionary<string, object> NewTextPart(string text)
        {
            return new Dictionary<string, object> { { "text", text } };
        }

        Dictionary<string, object> NewInlinePart(string path)
        {
            return new Dictionary<string, object>
            {
                {
                    "inlineData",
                    new Dictionary<string, object>
                    {
                        { "mimeType", GetMimeType(path) },
                        { "data", Convert.ToBase64String(File.ReadAllBytes(path)) }
                    }
                }
            };
        }

        string ExtractTextDocument(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".docx") return ExtractZipXml(path, "word/", null);
            if (ext == ".pptx") return ExtractPptx(path);
            if (ext == ".xlsx" || ext == ".xlsm") return ExtractXlsx(path);
            if (ext == ".doc") return "Old .doc is not supported yet. Save as .docx and upload again.";
            if (ext == ".xls") return "Old .xls is not supported yet. Save as .xlsx and upload again.";
            if (ext == ".ppt") return "Old .ppt is not supported yet. Save as .pptx and upload again.";
            return ReadText(path);
        }

        string ExtractZipXml(string path, string prefix, string contains)
        {
            var sb = new StringBuilder();
            using (var zip = ZipFile.OpenRead(path))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                    if (contains != null && entry.FullName.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    using (var sr = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        string text = ExtractXmlText(sr.ReadToEnd());
                        if (!string.IsNullOrWhiteSpace(text)) sb.AppendLine(text).AppendLine();
                    }
                }
            }
            return sb.ToString();
        }

        string ExtractPptx(string path)
        {
            var sb = new StringBuilder();
            int i = 1;
            using (var zip = ZipFile.OpenRead(path))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) || !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var sr = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        string text = ExtractXmlText(sr.ReadToEnd());
                        if (!string.IsNullOrWhiteSpace(text)) sb.AppendLine("[Slide " + i++ + "]").AppendLine(text).AppendLine();
                    }
                }
            }
            return sb.ToString();
        }

        string ExtractXlsx(string path)
        {
            var shared = new List<string>();
            var sb = new StringBuilder();
            using (var zip = ZipFile.OpenRead(path))
            {
                var sharedEntry = zip.GetEntry("xl/sharedStrings.xml");
                if (sharedEntry != null)
                {
                    using (var sr = new StreamReader(sharedEntry.Open(), Encoding.UTF8))
                    {
                        shared.AddRange(ExtractXmlText(sr.ReadToEnd()).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
                    }
                }

                int sheetNo = 1;
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) || !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var sr = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        string xml = sr.ReadToEnd();
                        sb.AppendLine("[Sheet " + sheetNo++ + "]");
                        int rowCount = 0;
                        foreach (Match rm in Regex.Matches(xml, "<row[^>]*>(.*?)</row>", RegexOptions.Singleline))
                        {
                            var cells = new List<string>();
                            foreach (Match cm in Regex.Matches(rm.Groups[1].Value, "<c([^>]*)>(.*?)</c>", RegexOptions.Singleline))
                            {
                                string attrs = cm.Groups[1].Value;
                                string body = cm.Groups[2].Value;
                                string v = "";
                                var vm = Regex.Match(body, "<v>(.*?)</v>", RegexOptions.Singleline);
                                if (vm.Success) v = WebUtility.HtmlDecode(vm.Groups[1].Value);
                                if (attrs.Contains("t=\"s\"") && Regex.IsMatch(v, "^\\d+$"))
                                {
                                    int idx = int.Parse(v);
                                    if (idx >= 0 && idx < shared.Count) v = shared[idx];
                                }
                                if (body.Contains("<is>")) v = ExtractXmlText(body);
                                cells.Add(v);
                            }
                            if (string.Join("", cells.ToArray()).Trim().Length > 0) sb.AppendLine(string.Join("\t", cells.ToArray()));
                            if (++rowCount >= 120) { sb.AppendLine("[sheet truncated]"); break; }
                        }
                        sb.AppendLine();
                    }
                }
            }
            return sb.ToString();
        }

        string ExtractXmlText(string xml)
        {
            var items = new List<string>();
            foreach (Match m in Regex.Matches(xml, "<[^>/]*:?t[^>]*>(.*?)</[^>]*:?t>", RegexOptions.Singleline))
            {
                string value = WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "")).Trim();
                if (value.Length > 0) items.Add(value);
            }
            return string.Join("\r\n", items.ToArray());
        }

        string ReadText(string path)
        {
            try { return File.ReadAllText(path, new UTF8Encoding(true)); }
            catch (DecoderFallbackException) { return File.ReadAllText(path, Encoding.Default); }
        }
    }
}
