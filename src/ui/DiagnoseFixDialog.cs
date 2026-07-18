using System;
using System.Windows.Forms;
using System.Drawing;

namespace ZhuaQianDesktopApp.UI
{
    // Viewer + interactive host for a coding-loop report (markdown). Side
    // effects (export patch / commit / apply-and-rerun) are delegated to
    // caller-supplied callbacks so permission and audit stay in the MainForm
    // partial.
    public sealed class DiagnoseFixDialog : Form
    {
        readonly TextBox reportBox;
        readonly TextBox patchBox;
        readonly TextBox commitMsgBox;

        public Action<string> ExportPatchCallback;
        public Action<string> CommitCallback;
        public Func<string, string> ApplyPatchCallback;

        public DiagnoseFixDialog(string reportMarkdown, string rootDirectory)
        {
            Text = "Diagnose & Fix — " + (string.IsNullOrWhiteSpace(rootDirectory) ? "(unknown)" : rootDirectory);
            Size = new Size(780, 620);
            StartPosition = FormStartPosition.CenterParent;
            Padding = new Padding(8);

            reportBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9.5f),
                Text = reportMarkdown ?? ""
            };

            patchBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9.5f),
                Text = ""
            };

            commitMsgBox = new TextBox
            {
                Dock = DockStyle.Top,
                Text = "chore: apply coding-agent fixes"
            };

            var applyBtn = new Button { Text = "Apply Patch & Rerun", AutoSize = true, Dock = DockStyle.Left };
            applyBtn.Click += (s, e) => DoApply();
            var exportBtn = new Button { Text = "Export Patch...", AutoSize = true, Dock = DockStyle.Left };
            exportBtn.Click += (s, e) => DoExport();
            var commitBtn = new Button { Text = "Commit", AutoSize = true, Dock = DockStyle.Left };
            commitBtn.Click += (s, e) => DoCommit();
            var copyBtn = new Button { Text = "Copy Report", AutoSize = true, Dock = DockStyle.Left };
            copyBtn.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(reportBox.Text))
                {
                    try { Clipboard.SetText(reportBox.Text); } catch (Exception) { }
                }
            };
            var closeBtn = new Button { Text = "Close", DialogResult = DialogResult.Cancel, AutoSize = true, Dock = DockStyle.Left };

            var btnFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            btnFlow.Controls.Add(applyBtn);
            btnFlow.Controls.Add(exportBtn);
            btnFlow.Controls.Add(commitBtn);
            btnFlow.Controls.Add(copyBtn);
            btnFlow.Controls.Add(closeBtn);

            var commitLabel = new Label { Text = "Commit message:", AutoSize = true, Dock = DockStyle.Top };

            var bottom = new Panel { Dock = DockStyle.Fill };
            bottom.Controls.Add(commitLabel);
            bottom.Controls.Add(commitMsgBox);
            bottom.Controls.Add(btnFlow);
            bottom.Controls.Add(patchBox);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 360
            };
            split.Panel1.Controls.Add(reportBox);
            split.Panel2.Controls.Add(bottom);

            Controls.Add(split);
            AcceptButton = closeBtn;
            CancelButton = closeBtn;
        }

        void DoApply()
        {
            string text = patchBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, "Paste a unified diff into the box first.", "Apply Patch");
                return;
            }
            if (ApplyPatchCallback == null)
            {
                MessageBox.Show(this, "Apply-and-rerun is not configured.", "Apply Patch");
                return;
            }
            string newMarkdown = ApplyPatchCallback(text);
            if (!string.IsNullOrEmpty(newMarkdown))
            {
                reportBox.Text = newMarkdown;
                Text = "Diagnose & Fix — rerun complete";
            }
        }

        void DoExport()
        {
            if (ExportPatchCallback == null) return;
            using (var dlg = new SaveFileDialog
            {
                Filter = "Patch files (*.patch)|*.patch|All files (*.*)|*.*",
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
            CommitCallback(commitMsgBox.Text ?? "chore: apply coding-agent fixes");
        }
    }
}
