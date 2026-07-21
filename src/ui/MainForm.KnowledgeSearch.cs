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
    partial class MainForm
    {
        string BuildKnowledgeContext(string query, int limit)
        {
            var scored = new List<KeyValuePair<int, IndexedDoc>>();
            foreach (var doc in knowledgeIndex)
            {
                int score = ScoreDoc(doc, query);
                if (score > 0) scored.Add(new KeyValuePair<int, IndexedDoc>(score, doc));
            }
            scored.Sort((a, b) => b.Key.CompareTo(a.Key));
            var sb = new StringBuilder();
            sb.AppendLine("[Local knowledge base search]");
            sb.AppendLine("query: " + query);
            int count = 0;
            foreach (var item in scored)
            {
                if (count++ >= limit) break;
                sb.AppendLine();
                sb.AppendLine("[" + count + "] " + item.Value.Name);
                sb.AppendLine("chunkId: " + SafeMeta(item.Value.ChunkId));
                if (!string.IsNullOrWhiteSpace(item.Value.Heading)) sb.AppendLine("heading: " + item.Value.Heading);
                sb.AppendLine("layer: " + SafeMeta(item.Value.Layer));
                sb.AppendLine("tags: " + SafeMeta(item.Value.Tags));
                sb.AppendLine("modifiedAt: " + item.Value.ModifiedAt.ToString("yyyy-MM-dd HH:mm"));
                if (item.Value.SizeBytes > 0) sb.AppendLine("size: " + FormatSize(item.Value.SizeBytes));
                sb.AppendLine("path: " + item.Value.Path);
                if (!string.IsNullOrWhiteSpace(item.Value.Summary)) sb.AppendLine("summary: " + item.Value.Summary);
                sb.AppendLine("snippet:");
                sb.AppendLine(ExtractSnippet(item.Value.Text, query, 1400));
            }
            return count == 0 ? "" : sb.ToString();
        }

        int ScoreDoc(IndexedDoc doc, string query)
        {
            string hay = ((doc.Name ?? "") + "\n" + (doc.Heading ?? "") + "\n" + (doc.Tags ?? "") + "\n" + (doc.Summary ?? "") + "\n" + (doc.Text ?? "")).ToLowerInvariant();
            int score = 0;
            foreach (Match m in Regex.Matches(query.ToLowerInvariant(), "[\\p{L}\\p{N}_]+"))
            {
                string term = m.Value;
                if (term.Length == 0) continue;
                int idx = -1;
                while ((idx = hay.IndexOf(term, idx + 1, StringComparison.Ordinal)) >= 0) score++;
                if ((doc.Name ?? "").ToLowerInvariant().Contains(term)) score += 5;
                if ((doc.Heading ?? "").ToLowerInvariant().Contains(term)) score += 4;
                if ((doc.Tags ?? "").ToLowerInvariant().Contains(term)) score += 4;
                if ((doc.Summary ?? "").ToLowerInvariant().Contains(term)) score += 2;
            }
            if (string.Equals(doc.Layer, "hot", StringComparison.OrdinalIgnoreCase)) score += 2;
            return score;
        }

        float[] GetQueryEmbedding(string text)
        {
            string url = EmbeddingUrlFromChatUrl(string.IsNullOrWhiteSpace(localApiUrl) ? DefaultLocalApiUrl : localApiUrl);
            string model = string.IsNullOrWhiteSpace(embeddingModel) ? "nomic-embed-text" : embeddingModel;
            return vectorIndex.ComputeQueryEmbedding(text ?? "", url, model);
        }

        float CosineSimilarity(float[] a, float[] b) { return Knowledge.VectorIndex.CosineSimilarity(a, b); }

        string BuildKnowledgeContextHybrid(string query, int limit)
        {
            float[] queryVec = GetQueryEmbedding(query);
            var scored = new List<KeyValuePair<double, IndexedDoc>>();
            foreach (var doc in knowledgeIndex)
            {
                double combined = ScoreDoc(doc, query) * 0.5;
                if (queryVec != null)
                {
                    float[] docVec = GetDocEmbedding(doc);
                    if (docVec != null)
                        combined += CosineSimilarity(queryVec, docVec) * 0.5;
                }
                if (combined > 0) scored.Add(new KeyValuePair<double, IndexedDoc>(combined, doc));
            }
            scored.Sort((a, b) => b.Key.CompareTo(a.Key));
            var sb = new StringBuilder();
            sb.AppendLine("[Local knowledge base hybrid search]");
            sb.AppendLine("query: " + query);
            if (queryVec != null) sb.AppendLine("embedding: " + embeddingModel);
            sb.AppendLine("mode: hybrid (keyword 0.5 + vector 0.5)");
            int count = 0;
            foreach (var item in scored)
            {
                if (count++ >= limit) break;
                sb.AppendLine();
                sb.AppendLine("[" + count + "] " + item.Value.Name + "  score=" + item.Key.ToString("F3"));
                sb.AppendLine("chunkId: " + SafeMeta(item.Value.ChunkId));
                if (!string.IsNullOrWhiteSpace(item.Value.Heading)) sb.AppendLine("heading: " + item.Value.Heading);
                sb.AppendLine("layer: " + SafeMeta(item.Value.Layer));
                sb.AppendLine("tags: " + SafeMeta(item.Value.Tags));
                sb.AppendLine("modifiedAt: " + item.Value.ModifiedAt.ToString("yyyy-MM-dd HH:mm"));
                if (item.Value.SizeBytes > 0) sb.AppendLine("size: " + FormatSize(item.Value.SizeBytes));
                sb.AppendLine("path: " + item.Value.Path);
                if (!string.IsNullOrWhiteSpace(item.Value.Summary)) sb.AppendLine("summary: " + item.Value.Summary);
                sb.AppendLine("snippet:");
                sb.AppendLine(ExtractSnippet(item.Value.Text, query, 1400));
            }
            return count == 0 ? "" : sb.ToString();
        }

        float[] GetDocEmbedding(IndexedDoc doc)
        {
            float[] cached = vectorIndex.GetVector(doc.ChunkId);
            if (cached != null) return cached;
            string textForEmbedding = (doc.Heading + "\n" + doc.Summary + "\n" + ExtractSnippet(doc.Text, doc.Heading, 512));
            if (string.IsNullOrWhiteSpace(textForEmbedding))
                textForEmbedding = doc.Text;
            if (textForEmbedding.Length > 1024)
                textForEmbedding = textForEmbedding.Substring(0, 1024);
            string url = EmbeddingUrlFromChatUrl(string.IsNullOrWhiteSpace(localApiUrl) ? DefaultLocalApiUrl : localApiUrl);
            string model = string.IsNullOrWhiteSpace(embeddingModel) ? "nomic-embed-text" : embeddingModel;
            return vectorIndex.ComputeQueryEmbedding(textForEmbedding, url, model);
        }

        void SaveVectorsAsync(string embedUrl)
        {
            string embedModel = string.IsNullOrWhiteSpace(embeddingModel) ? "nomic-embed-text" : embeddingModel;
            var docs = new List<Knowledge.ChunkedDoc>();
            foreach (var d in knowledgeIndex)
            {
                docs.Add(new Knowledge.ChunkedDoc
                {
                    DocId = d.DocId,
                    ChunkId = d.ChunkId,
                    Path = d.Path,
                    Name = d.Name,
                    Heading = d.Heading,
                    Text = d.Text,
                    Summary = d.Summary,
                    Tags = d.Tags,
                    Layer = d.Layer,
                    Offset = d.Offset,
                    SizeBytes = d.SizeBytes,
                    ModifiedAt = d.ModifiedAt
                });
            }
            vectorIndex.SaveAll(docs, embedUrl, embedModel);
        }

        string SafeMeta(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        string ExtractSnippet(string text, string query, int max)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            int pos = 0;
            foreach (Match m in Regex.Matches(query.ToLowerInvariant(), "[\\p{L}\\p{N}_]+"))
            {
                int found = text.ToLowerInvariant().IndexOf(m.Value, StringComparison.Ordinal);
                if (found >= 0) { pos = Math.Max(0, found - 220); break; }
            }
            string snippet = text.Substring(pos, Math.Min(max, text.Length - pos));
            return snippet.Replace("\0", " ");
        }

        string BuildLocalSummary(string text, int max)
        {
            return chunker.BuildSummary(text, max);
        }

        string InferKnowledgeTags(string name, string text)
        {
            return chunker.InferTags(name, text);
        }

        string InferKnowledgeLayer(string path, DateTime modifiedAt)
        {
            return chunker.InferLayer(path, modifiedAt);
        }
    }
}
