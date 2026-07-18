using System;
using System.Windows.Forms;
using System.Drawing;

namespace ZhuaQianDesktopApp.UI
{
    public sealed class DiagnoseFixDialog : Form
    {
        readonly TextBox reportBox;
        readonly TextBox patchBox;
        readonly TextBox commitMsgBox;
        readonly Func<string, string, string, string> translator;
        readonly string languageCode;

        public Action<string> ExportPatchCallback;
        public Action<string> CommitCallback;
        public Func<string, string> ApplyPatchCallback;

        public DiagnoseFixDialog(string reportMarkdown, string rootDirectory, Func<string, string, string, string> translator = null, string languageCode = "zh-Hans")
        {
            this.translator = translator;
            this.languageCode = languageCode ?? "zh-Hans";

            Text = T("Diagnose and Fix", "诊断修复", "診斷修復") + " - " + (string.IsNullOrWhiteSpace(rootDirectory) ? "(unknown)" : rootDirectory);
            Size = new Size(860, 680);
            MinimumSize = new Size(720, 520);
            StartPosition = FormStartPosition.CenterParent;
            Padding = new Padding(10);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 410
            };

            reportBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9.5f),
                Text = reportMarkdown ?? ""
            };
            split.Panel1.Controls.Add(reportBox);

            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 4,
                ColumnCount = 1
            };
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            bottom.Controls.Add(new Label
            {
                Text = T("Optional: paste a unified diff below, then apply and rerun.",
                         "可选：在下方粘贴 unified diff，然后应用补丁并重新运行。",
                         "可選：在下方貼上 unified diff，然後套用補丁並重新執行。"),
                Dock = DockStyle.Fill,
                AutoSize = true
            }, 0, 0);

            commitMsgBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = "fix: apply coding-agent fixes"
            };
            bottom.Controls.Add(commitMsgBox, 0, 1);

            patchBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9.5f),
                Text = ""
            };
            bottom.Controls.Add(patchBox, 0, 2);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };

            var applyBtn = new Button { Text = T("Apply Patch && Rerun", "应用补丁并重跑", "套用補丁並重跑"), AutoSize = true, Height = 32 };
            applyBtn.Click += (s, e) => DoApply();
            var exportBtn = new Button { Text = T("Export Patch...", "导出补丁...", "匯出補丁..."), AutoSize = true, Height = 32 };
            exportBtn.Click += (s, e) => DoExport();
            var commitBtn = new Button { Text = T("Commit", "提交", "提交"), AutoSize = true, Height = 32 };
            commitBtn.Click += (s, e) => DoCommit();
            var copyBtn = new Button { Text = T("Copy Report", "复制报告", "複製報告"), AutoSize = true, Height = 32 };
            copyBtn.Click += (s, e) => CopyReport();
            var closeBtn = new Button { Text = T("Close", "关闭", "關閉"), DialogResult = DialogResult.Cancel, AutoSize = true, Height = 32 };

            buttons.Controls.Add(applyBtn);
            buttons.Controls.Add(exportBtn);
            buttons.Controls.Add(commitBtn);
            buttons.Controls.Add(copyBtn);
            buttons.Controls.Add(closeBtn);
            bottom.Controls.Add(buttons, 0, 3);

            split.Panel2.Controls.Add(bottom);
            Controls.Add(split);
            AcceptButton = closeBtn;
            CancelButton = closeBtn;
        }

        string T(string en, string zhHans, string zhHant)
        {
            if (translator != null) return translator(en, zhHans, zhHant);
            if (languageCode == "zh-Hant") return zhHant;
            if (languageCode == "en") return en;
            return zhHans;
        }

        void CopyReport()
        {
            if (string.IsNullOrEmpty(reportBox.Text)) return;
            try { Clipboard.SetText(reportBox.Text); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, T("Copy failed", "复制失败", "複製失敗")); }
        }

        void DoApply()
        {
            string text = patchBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this,
                    T("Paste a unified diff into the box first.", "请先在文本框里粘贴 unified diff。", "請先在文字框貼上 unified diff。"),
                    T("Apply Patch", "应用补丁", "套用補丁"));
                return;
            }
            if (ApplyPatchCallback == null)
            {
                MessageBox.Show(this,
                    T("Apply-and-rerun is not configured.", "应用补丁并重跑尚未配置。", "套用補丁並重跑尚未設定。"),
                    T("Apply Patch", "应用补丁", "套用補丁"));
                return;
            }
            string newMarkdown = ApplyPatchCallback(text);
            if (!string.IsNullOrEmpty(newMarkdown))
            {
                reportBox.Text = newMarkdown;
                Text = T("Diagnose and Fix - rerun complete", "诊断修复 - 已重跑", "診斷修復 - 已重跑");
            }
        }

        void DoExport()
        {
            if (ExportPatchCallback == null) return;
            using (var dlg = new SaveFileDialog
            {
                Filter = T("Patch files (*.patch)|*.patch|All files (*.*)|*.*", "补丁文件 (*.patch)|*.patch|所有文件 (*.*)|*.*", "補丁檔案 (*.patch)|*.patch|所有檔案 (*.*)|*.*"),
                FileName = "coding-fix.patch"
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    ExportPatchCallback(dlg.FileName);
            }
        }

        void DoCommit()
        {
            if (CommitCallback == null) return;
            CommitCallback(commitMsgBox.Text ?? "fix: apply coding-agent fixes");
        }
    }
}
