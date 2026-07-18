using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Documents;
using ZhuaQianDesktopApp.Tools;

namespace ZhuaQianDesktopApp.Ui
{
    /// <summary>
    /// 自然语言办公生成对话框（Epic F2 审查/编辑 + F3 网络研究）。用户选择模板类型、填写
    /// 内容、实时预览可编辑的文本骨架；可选"使用网络研究"把检索摘要注入要点；确认时通过
    /// 保存对话框显式选择落盘路径（即"写前审查"）。最终文本由 OfficeTemplateExecutor 导出。
    /// </summary>
    public class OfficeGenerateDialog : Form
    {
        readonly Func<string, string, string, string> tr;
        readonly string uiLanguage;
        readonly WebSearchClient webSearch = new WebSearchClient();

        ComboBox kindCombo;
        TextBox topicBox;
        TextBox titleBox, subtitleBox, authorBox, dateBox;
        Label bulletsLabel;
        TextBox bulletsBox;
        Label rowsLabel;
        TextBox rowsBox;
        TextBox previewBox;

        public OfficeTemplateKind ResultKind { get; private set; }
        public string ResultFormat { get; private set; }
        public string ResultText { get; private set; }
        public string ResultTarget { get; private set; }

        public OfficeGenerateDialog(string naturalText, Func<string, string, string, string> translator = null, string languageCode = "zh-Hans")
        {
            tr = translator ?? ((en, zhHans, zhHant) => en);
            uiLanguage = string.IsNullOrWhiteSpace(languageCode) ? "zh-Hans" : languageCode;
            Text = T("Generate Office File", "生成办公文件", "產生辦公檔案");
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1000, 720);
            MinimumSize = new Size(860, 560);
            Font = new Font(IsEnglish() ? "Segoe UI" : "Microsoft YaHei UI", 9);
            BuildUi(naturalText ?? "");
        }

        void BuildUi(string naturalText)
        {
            var cfg = new Panel { Dock = DockStyle.Top, Height = 250, Padding = new Padding(12), BackColor = Color.FromArgb(246, 248, 251) };
            Controls.Add(cfg);

            cfg.Controls.Add(new Label { Text = T("Type", "类型", "類型"), Left = 12, Top = 10, Width = 80, Height = 20, Font = new Font(Font, FontStyle.Bold) });
            kindCombo = new ComboBox { Left = 100, Top = 8, Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
            kindCombo.Items.AddRange(new object[] {
                T("Sales Pitch (PPT)", "销售演示 (PPT)", "銷售演示 (PPT)"),
                T("Meeting Minutes (Word)", "会议纪要 (Word)", "會議紀要 (Word)"),
                T("Report (Word)", "报告 (Word)", "報告 (Word)"),
                T("Data Table (Excel)", "数据表 (Excel)", "資料表 (Excel)"),
                T("Poster (PNG)", "海报 (PNG)", "海報 (PNG)")
            });
            kindCombo.SelectedIndex = 0;
            kindCombo.SelectedIndexChanged += (s, e) => { SyncTableVisibility(); RefreshPreview(); };
            cfg.Controls.Add(kindCombo);

            cfg.Controls.Add(new Label { Text = T("Topic / subject", "主题", "主題"), Left = 360, Top = 10, Width = 120, Height = 20, Font = new Font(Font, FontStyle.Bold) });
            topicBox = new TextBox { Left = 480, Top = 8, Width = 500, Height = 22, Text = ExtractTopic(naturalText) };
            cfg.Controls.Add(topicBox);

            cfg.Controls.Add(new Label { Text = T("Title", "标题", "標題"), Left = 12, Top = 40, Width = 80, Height = 20 });
            titleBox = new TextBox { Left = 100, Top = 38, Width = 360, Height = 22, Text = topicBox.Text };
            cfg.Controls.Add(titleBox);
            topicBox.TextChanged += (s, e) => { if (string.IsNullOrWhiteSpace(titleBox.Text)) titleBox.Text = topicBox.Text; };

            cfg.Controls.Add(new Label { Text = T("Subtitle", "副标题", "副標題"), Left = 480, Top = 40, Width = 80, Height = 20 });
            subtitleBox = new TextBox { Left = 560, Top = 38, Width = 420, Height = 22 };
            cfg.Controls.Add(subtitleBox);

            cfg.Controls.Add(new Label { Text = T("Author", "作者", "作者"), Left = 12, Top = 70, Width = 80, Height = 20 });
            authorBox = new TextBox { Left = 100, Top = 68, Width = 200, Height = 22 };
            cfg.Controls.Add(authorBox);

            cfg.Controls.Add(new Label { Text = T("Date", "日期", "日期"), Left = 320, Top = 70, Width = 60, Height = 20 });
            dateBox = new TextBox { Left = 380, Top = 68, Width = 160, Height = 22, Text = DateTime.Now.ToString("yyyy-MM-dd") };
            cfg.Controls.Add(dateBox);

            bulletsLabel = new Label { Text = T("Bullets (one per line)", "要点（每行一条）", "要點（每行一條）"), Left = 12, Top = 100, Width = 240, Height = 20 };
            cfg.Controls.Add(bulletsLabel);
            bulletsBox = new TextBox { Left = 12, Top = 122, Width = 560, Height = 110, Multiline = true, ScrollBars = ScrollBars.Vertical };
            cfg.Controls.Add(bulletsBox);

            rowsLabel = new Label { Text = T("Rows (pipe or CSV, first row = headers)", "行（竖线或 CSV，首行为表头）", "行（分隔線或 CSV，首列為表頭）"), Left = 590, Top = 100, Width = 390, Height = 20 };
            cfg.Controls.Add(rowsLabel);
            rowsBox = new TextBox { Left = 590, Top = 122, Width = 390, Height = 110, Multiline = true, ScrollBars = ScrollBars.Vertical };
            cfg.Controls.Add(rowsBox);

            var research = new Button { Text = T("Use Web Research", "使用网络研究", "使用網路研究"), Left = 12, Top = 208, Width = 190, Height = 30 };
            research.Click += (s, e) => UseWebResearch();
            cfg.Controls.Add(research);

            var refresh = new Button { Text = T("Refresh Preview", "刷新预览", "重新整理預覽"), Left = 212, Top = 208, Width = 160, Height = 30 };
            refresh.Click += (s, e) => RefreshPreview();
            cfg.Controls.Add(refresh);

            var prevPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            Controls.Add(prevPanel);
            previewBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), WordWrap = false };
            prevPanel.Controls.Add(previewBox);

            var actions = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            prevPanel.Controls.Add(actions);
            var copy = new Button { Text = T("Copy", "复制", "複製"), Left = 0, Top = 8, Width = 90, Height = 30 };
            copy.Click += (s, e) => { if (!string.IsNullOrEmpty(previewBox.Text)) Clipboard.SetText(previewBox.Text); };
            actions.Controls.Add(copy);
            var generate = new Button { Text = T("Generate", "生成", "產生"), Left = 100, Top = 8, Width = 120, Height = 30 };
            generate.Click += (s, e) => Generate();
            actions.Controls.Add(generate);
            var cancel = new Button { Text = T("Cancel", "取消", "取消"), Left = 230, Top = 8, Width = 90, Height = 30 };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            actions.Controls.Add(cancel);

            SyncTableVisibility();
            RefreshPreview();
        }

        void SyncTableVisibility()
        {
            bool isTable = CurrentKind() == OfficeTemplateKind.DataTable;
            bulletsLabel.Visible = !isTable;
            bulletsBox.Visible = !isTable;
            rowsLabel.Visible = isTable;
            rowsBox.Visible = isTable;
        }

        OfficeTemplateKind CurrentKind()
        {
            switch (kindCombo.SelectedIndex)
            {
                case 0: return OfficeTemplateKind.SalesPitch;
                case 1: return OfficeTemplateKind.MeetingMinutes;
                case 2: return OfficeTemplateKind.Report;
                case 3: return OfficeTemplateKind.DataTable;
                case 4: return OfficeTemplateKind.Poster;
                default: return OfficeTemplateKind.Report;
            }
        }

        TemplateContext BuildContext()
        {
            var ctx = new TemplateContext();
            ctx.Title = string.IsNullOrWhiteSpace(titleBox.Text) ? topicBox.Text : titleBox.Text;
            ctx.Subtitle = subtitleBox.Text;
            ctx.Author = authorBox.Text;
            ctx.Date = dateBox.Text;
            if (CurrentKind() == OfficeTemplateKind.DataTable)
                ParseTable(ctx, rowsBox.Text);
            else
            {
                var list = new List<string>();
                foreach (var line in bulletsBox.Text.Replace("\r", "\n").Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line.Trim())) list.Add(line.Trim());
                if (list.Count > 0) ctx.Bullets["要点"] = list;
            }
            return ctx;
        }

        static void ParseTable(TemplateContext ctx, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var lines = text.Replace("\r", "\n").Split('\n');
            int headerIdx = -1;
            for (int i = 0; i < lines.Length; i++)
                if (!string.IsNullOrWhiteSpace(lines[i])) { headerIdx = i; break; }
            if (headerIdx < 0) return;
            var headers = SplitRow(lines[headerIdx]);
            foreach (var h in headers) ctx.Columns.Add(new TableColumn(h));
            for (int i = headerIdx + 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                ctx.Rows.Add(SplitRow(line));
            }
        }

        static List<string> SplitRow(string line)
        {
            var cells = new List<string>();
            string[] parts = line.Contains("|") ? line.Split('|') : line.Split(',');
            foreach (var p in parts) cells.Add(p.Trim());
            return cells;
        }

        void RefreshPreview()
        {
            try { previewBox.Text = OfficeTemplateLibrary.Render(CurrentKind(), BuildContext()).Text; }
            catch (Exception ex) { previewBox.Text = "Preview error: " + ex.Message; }
        }

        void UseWebResearch()
        {
            string q = string.IsNullOrWhiteSpace(topicBox.Text) ? titleBox.Text : topicBox.Text;
            if (string.IsNullOrWhiteSpace(q))
            {
                MessageBox.Show(this, T("Enter a topic first.", "请先输入主题。", "請先輸入主題。"), T("Web Research", "网络研究", "網路研究"));
                return;
            }
            var resp = webSearch.SearchDetailed(q, 5);
            if (!resp.Success || resp.Results.Count == 0)
            {
                MessageBox.Show(this, T("No web results.", "没有网络结果。", "沒有網路結果。") + " " + resp.ErrorMessage, T("Web Research", "网络研究", "網路研究"));
                return;
            }
            var sb = new StringBuilder();
            foreach (var r in resp.Results)
                sb.AppendLine(r.Title + " — " + r.Snippet);
            bulletsBox.AppendText((bulletsBox.Text.Length > 0 ? "\r\n" : "") + sb.ToString().Trim());
            RefreshPreview();
        }

        void Generate()
        {
            ResultKind = CurrentKind();
            ResultFormat = OfficeTemplateLibrary.SuggestedExtension(ResultKind);
            ResultText = previewBox.Text ?? "";
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = FilterFor(ResultKind);
                sfd.FileName = SanitizeFileName(string.IsNullOrWhiteSpace(titleBox.Text) ? topicBox.Text : titleBox.Text) + "." + ResultFormat;
                sfd.Title = T("Save office file", "保存办公文件", "儲存辦公檔案");
                sfd.AddExtension = true;
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                ResultTarget = sfd.FileName;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        static string FilterFor(OfficeTemplateKind kind)
        {
            switch (kind)
            {
                case OfficeTemplateKind.SalesPitch: return "PowerPoint (*.pptx)|*.pptx|All files (*.*)|*.*";
                case OfficeTemplateKind.MeetingMinutes:
                case OfficeTemplateKind.Report: return "Word (*.docx)|*.docx|All files (*.*)|*.*";
                case OfficeTemplateKind.DataTable: return "Excel (*.xlsx)|*.xlsx|All files (*.*)|*.*";
                case OfficeTemplateKind.Poster: return "PNG (*.png)|*.png|All files (*.*)|*.*";
                default: return "All files (*.*)|*.*";
            }
        }

        static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "office-file";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
            name = name.Trim().Trim('.').Trim();
            if (name.Length > 60) name = name.Substring(0, 60).Trim('-');
            return string.IsNullOrEmpty(name) ? "office-file" : name;
        }

        static string ExtractTopic(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? "" : text.Trim();
        }

        bool IsEnglish()
        {
            return string.Equals(uiLanguage, "en", StringComparison.OrdinalIgnoreCase);
        }

        string T(string en, string zhHans, string zhHant)
        {
            return tr(en, zhHans, zhHant);
        }
    }
}
