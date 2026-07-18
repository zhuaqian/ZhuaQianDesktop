using System;
using System.Drawing;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Agent;

namespace ZhuaQianDesktopApp.Ui
{
    // Read-only viewer for a CodingAgentSessionReport (Epic D acceptance:
    // show Plan -> Command -> Diff -> Test -> Review without leaving the app).
    // The report is produced by CodingAgentSession.Run and already contains the
    // plan review, workspace scan, diff summary, and build/test results.
    public class CodingAgentReportDialog : Form
    {
        readonly Func<string, string, string, string> tr;
        readonly string languageCode;

        public CodingAgentReportDialog(CodingAgentSessionReport report, string title = null, Func<string, string, string, string> translator = null, string languageCode = "zh-Hans")
        {
            this.tr = translator ?? ((en, zh, zht) => en);
            this.languageCode = languageCode ?? "en";
            Text = string.IsNullOrWhiteSpace(title) ? T("Coding Agent Session Report", "编程 Agent 会话报告", "程式 Agent 工作階段報告") : title;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(940, 660);
            MinimumSize = new Size(720, 480);
            Font = new Font("Segoe UI", 9);

            var status = new Label
            {
                Dock = DockStyle.Top,
                Height = 32,
                Padding = new Padding(12, 8, 12, 0),
                Text = T("Status: ", "状态：", "狀態：") + (report == null ? T("unknown", "未知", "未知") : (report.Status ?? T("unknown", "未知", "未知")))
            };
            if (report != null)
            {
                string s = report.Status ?? "";
                if (s == "passed") status.ForeColor = Color.FromArgb(0, 130, 80);
                else if (s == "build-failed" || s == "test-failed") status.ForeColor = Color.FromArgb(192, 0, 0);
                else status.ForeColor = Color.FromArgb(150, 110, 0);
            }
            Controls.Add(status);

            var box = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                ReadOnly = true,
                WordWrap = false,
                Font = new Font("Consolas", 9),
                Text = report == null ? "" : (report.ToMarkdown() ?? "")
            };
            Controls.Add(box);

            var actions = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            Controls.Add(actions);

            var copy = new Button { Text = T("Copy", "复制", "複製"), Left = 12, Top = 8, Width = 90, Height = 30 };
            copy.Click += (s, e) => { if (!string.IsNullOrEmpty(box.Text)) Clipboard.SetText(box.Text); };
            actions.Controls.Add(copy);

            var close = new Button { Text = T("Close", "关闭", "關閉"), Left = 112, Top = 8, Width = 90, Height = 30 };
            close.Click += (s, e) => Close();
            actions.Controls.Add(close);
        }

        string T(string en, string zh, string zht)
        {
            return tr(en, zh, zht);
        }
    }
}
