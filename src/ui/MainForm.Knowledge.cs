using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ZhuaQianDesktopApp
{
    partial class MainForm
    {
        void IndexFolder()
        {
            if (!EnsurePermission(Tr("Read local files", "读取本地文件", "讀取本機檔案"), permFileRead, false, "Index Folder")) return;
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Choose a folder to build local knowledge index";
                if (fbd.ShowDialog(this) != DialogResult.OK) return;
                Cursor = Cursors.WaitCursor;
                try
                {
                    var files = new List<string>();
                    CollectIndexFiles(fbd.SelectedPath, files, 300);
                    knowledgeIndex.Clear();
                    foreach (var file in files)
                    {
                        try
                        {
                            var info = new FileInfo(file);
                            if (info.Length > MaxDocBytes) continue;
                            string text = ApplyRedaction(TrimForPrompt(ExtractTextDocument(file), 64000));
                            if (string.IsNullOrWhiteSpace(text)) continue;
                            AddKnowledgeChunks(info, text);
                        }
                        catch (Exception _ex) { LogAction("Warning", "IndexFolder: " + _ex.Message); }
                    }
                    SaveKnowledgeIndex();
                    string embedUrl = EmbeddingUrlFromChatUrl(string.IsNullOrWhiteSpace(localApiUrl) ? DefaultLocalApiUrl : localApiUrl);
                    if (!string.IsNullOrWhiteSpace(embedUrl))
                        SaveVectorsAsync(embedUrl);
                    LogAction("IndexFolder", "Indexed " + files.Count + " files / " + knowledgeIndex.Count + " chunks from " + fbd.SelectedPath);
                    SetCurrentTaskStatus("ready_for_review", "Indexed " + knowledgeIndex.Count + " chunks", true);
                    RecordAction("IndexFolder", "success", "Indexed " + files.Count + " files / " + knowledgeIndex.Count + " chunks", indexPath);
                    AppendChat("ZhuaQian", "Indexed " + files.Count + " files / " + knowledgeIndex.Count + " chunks from:\r\n" + fbd.SelectedPath, ThemeManager.Success);
                }
                catch (Exception ex)
                {
                    SetCurrentTaskStatus("failed", "Index failed", true);
                    RecordAction("IndexFolder", "failed", ex.Message, fbd.SelectedPath);
                    MessageBox.Show(this, ex.Message, "Index Folder failed");
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
        }

        void CollectIndexFiles(string dir, List<string> files, int max)
        {
            if (files.Count >= max) return;
            try
            {
                foreach (var file in Directory.GetFiles(dir))
                {
                    if (files.Count >= max) return;
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (docExts.Contains(ext) && ext != ".pdf" && ext != ".doc" && ext != ".xls" && ext != ".ppt")
                        files.Add(file);
                }
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if (files.Count >= max) return;
                    CollectIndexFiles(sub, files, max);
                }
            }
            catch (Exception _ex) { LogAction("Warning", "CollectIndex: " + _ex.Message); }
        }

        void AddKnowledgeChunks(FileInfo info, string text)
        {
            string docId = StableDocId(info.FullName);
            string tags = InferKnowledgeTags(info.Name, text);
            string layer = InferKnowledgeLayer(info.FullName, info.LastWriteTime);
            var chunks = SplitKnowledgeChunks(text, 1800);
            for (int i = 0; i < chunks.Count; i++)
            {
                string chunkText = chunks[i];
                knowledgeIndex.Add(new IndexedDoc
                {
                    DocId = docId,
                    ChunkId = docId + "#" + (i + 1).ToString("000"),
                    Path = info.FullName,
                    Name = info.Name,
                    Heading = DetectHeading(chunkText, info.Name),
                    Text = chunkText,
                    Summary = BuildLocalSummary(chunkText, 220),
                    Tags = tags,
                    Layer = layer,
                    Offset = i,
                    SizeBytes = info.Length,
                    ModifiedAt = info.LastWriteTime
                });
            }
        }

        List<string> SplitKnowledgeChunks(string text, int maxChars)
        {
            return chunker.Split(text, maxChars);
        }

        string StableDocId(string path)
        {
            return chunker.StableDocId(path);
        }

        string DetectHeading(string text, string fallback)
        {
            return chunker.DetectHeading(text, fallback);
        }

        void LoadKnowledgeIndex()
        {
            knowledgeIndex.Clear();
            vectorIndex.Load();
            if (!File.Exists(indexPath)) return;
            try
            {
                var loaded = ToObjectList(json.DeserializeObject(File.ReadAllText(indexPath, Encoding.UTF8)));
                if (loaded == null) return;
                foreach (var item in loaded)
                {
                    var data = item as Dictionary<string, object>;
                    if (data == null) continue;
                    DateTime modified;
                    DateTime.TryParse(data.ContainsKey("modifiedAt") ? Convert.ToString(data["modifiedAt"]) : "", out modified);
                    knowledgeIndex.Add(new IndexedDoc
                    {
                        DocId = data.ContainsKey("docId") ? Convert.ToString(data["docId"]) : "",
                        ChunkId = data.ContainsKey("chunkId") ? Convert.ToString(data["chunkId"]) : "",
                        Path = data.ContainsKey("path") ? Convert.ToString(data["path"]) : "",
                        Name = data.ContainsKey("name") ? Convert.ToString(data["name"]) : "",
                        Heading = data.ContainsKey("heading") ? Convert.ToString(data["heading"]) : "",
                        Text = data.ContainsKey("text") ? Convert.ToString(data["text"]) : "",
                        Summary = data.ContainsKey("summary") ? Convert.ToString(data["summary"]) : "",
                        Tags = data.ContainsKey("tags") ? Convert.ToString(data["tags"]) : "",
                        Layer = data.ContainsKey("layer") ? Convert.ToString(data["layer"]) : "",
                        Offset = data.ContainsKey("offset") ? Convert.ToInt32(data["offset"]) : 0,
                        SizeBytes = data.ContainsKey("sizeBytes") ? Convert.ToInt64(data["sizeBytes"]) : 0,
                        ModifiedAt = modified
                    });
                }
            }
            catch (Exception _ex) { LogAction("Warning", "LoadKnowledgeIndex: " + _ex.Message); }
        }

        void SaveKnowledgeIndex()
        {
            var items = new ArrayList();
            foreach (var doc in knowledgeIndex)
            {
                items.Add(new Dictionary<string, object> {
                    { "docId", doc.DocId },
                    { "chunkId", doc.ChunkId },
                    { "path", doc.Path },
                    { "name", doc.Name },
                    { "heading", doc.Heading },
                    { "text", doc.Text },
                    { "summary", doc.Summary },
                    { "tags", doc.Tags },
                    { "layer", doc.Layer },
                    { "offset", doc.Offset },
                    { "sizeBytes", doc.SizeBytes },
                    { "modifiedAt", doc.ModifiedAt.ToString("o") }
                });
            }
            File.WriteAllText(indexPath, json.Serialize(items), Encoding.UTF8);
        }

        void SearchKnowledge()
        {
            if (knowledgeIndex.Count == 0)
            {
                MessageBox.Show(this, "No local knowledge index yet. Click Index Folder first.", "Knowledge base");
                return;
            }
            string query = PromptText("Search local knowledge", "Query:", input.Text.Trim());
            if (query == null) return;
            query = query.Trim();
            if (query.Length == 0) return;

            bool useHybrid = false;
            string embedUrl = EmbeddingUrlFromChatUrl(string.IsNullOrWhiteSpace(localApiUrl) ? DefaultLocalApiUrl : localApiUrl);
            if (!string.IsNullOrWhiteSpace(embeddingModel))
            {
                useHybrid = MessageBox.Show(this,
                    Tr("Use hybrid search (keyword + vector embedding)?\n\nYes = hybrid\nNo = keyword only",
                       "使用混合搜索（关键词 + 向量嵌入）？\n\n是 = 混合搜索\n否 = 仅关键词",
                       "使用混合搜尋（關鍵字 + 向量嵌入）？\n\n是 = 混合搜尋\n否 = 僅關鍵字"),
                    "Search mode", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            }

            string context;
            if (useHybrid)
                context = BuildKnowledgeContextHybrid(query, 8);
            else
                context = BuildKnowledgeContext(query, 8);

            if (string.IsNullOrWhiteSpace(context))
            {
                MessageBox.Show(this, "No matching documents found.", "Knowledge base");
                return;
            }
            pendingParts.Add(NewTextPart(context));
            pendingLabels.Add("Local KB search: " + query + (useHybrid ? " (hybrid)" : " (keyword)"));
            input.Text = "请基于本地知识库检索结果回答：\r\n" + query;
            RefreshAttachLabel();
            LogAction("SearchKnowledge", "Query: " + query + (useHybrid ? " (hybrid)" : " (keyword)"));
        }
    }
}
