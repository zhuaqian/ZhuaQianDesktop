using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ZhuaQianDesktopApp.Tools
{
    public class SandboxProgressPanel : UserControl
    {
        readonly Label titleLabel;
        readonly ProgressBar progressBar;
        readonly Label progressLabel;
        readonly FlowLayoutPanel stepPanel;
        readonly List<StepRow> rows = new List<StepRow>();
        readonly Timer refreshTimer;
        DateTime startTime;

        class StepRow
        {
            public Panel Panel;
            public Label Dot;
            public Label Desc;
            public Label Status;
            public Label Duration;
            public DateTime? StartedAt;
        }

        public SandboxProgressPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            MinimumSize = new Size(200, 200);

            titleLabel = new Label
            {
                Text = "Sandbox Execution",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 30, 30),
                Location = new Point(8, 6),
                AutoSize = true
            };

            progressBar = new ProgressBar
            {
                Location = new Point(8, 28),
                Width = 180,
                Height = 14,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };

            progressLabel = new Label
            {
                Text = "0 / 0 steps",
                Font = new Font("Segoe UI", 7),
                ForeColor = Color.FromArgb(120, 120, 120),
                Location = new Point(8, 46),
                AutoSize = true
            };

            stepPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Location = new Point(4, 62),
                Width = 190,
                Height = Height - 66,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(250, 250, 252)
            };

            Controls.Add(titleLabel);
            Controls.Add(progressBar);
            Controls.Add(progressLabel);
            Controls.Add(stepPanel);

            refreshTimer = new Timer { Interval = 500 };
            refreshTimer.Tick += (s, e) => UpdateDurations();
        }

        public void BeginExecution(string title, List<string> steps)
        {
            titleLabel.Text = title;
            startTime = DateTime.Now;
            stepPanel.Controls.Clear();
            rows.Clear();

            foreach (string step in steps)
            {
                var row = CreateStepRow(rows.Count + 1, step);
                rows.Add(row);
                stepPanel.Controls.Add(row.Panel);
            }

            progressBar.Maximum = steps.Count * 100;
            progressBar.Value = 0;
            progressLabel.Text = "0 / " + steps.Count + " steps";
            Visible = true;
            refreshTimer.Start();
        }

        public void SetStepStatus(int index, StepStatus status, string detail = "")
        {
            if (index < 0 || index >= rows.Count) return;
            var row = rows[index];
            Color dotColor = Color.Gray;
            string statusText = status.ToString();

            switch (status)
            {
                case StepStatus.Running:
                    dotColor = Color.FromArgb(0, 120, 200);
                    statusText = "Running";
                    if (row.StartedAt == null) row.StartedAt = DateTime.Now;
                    break;
                case StepStatus.Success:
                    dotColor = Color.FromArgb(0, 160, 60);
                    statusText = "Done";
                    if (row.StartedAt != null)
                        row.Duration.Text = ElapsedString(DateTime.Now - row.StartedAt.Value);
                    break;
                case StepStatus.Failed:
                    dotColor = Color.FromArgb(200, 40, 40);
                    statusText = "Failed";
                    break;
                case StepStatus.Skipped:
                    dotColor = Color.FromArgb(180, 180, 180);
                    statusText = "Skipped";
                    break;
            }

            row.Dot.BackColor = dotColor;
            row.Status.Text = statusText;
            if (!string.IsNullOrEmpty(detail))
                row.Desc.Text = detail;

            int doneCount = 0;
            foreach (var r in rows)
            {
                if (r.Dot.BackColor == Color.FromArgb(0, 160, 60) ||
                    r.Dot.BackColor == Color.FromArgb(200, 40, 40) ||
                    r.Dot.BackColor == Color.FromArgb(180, 180, 180))
                    doneCount++;
            }
            progressBar.Value = Math.Min(doneCount * 100, progressBar.Maximum);
            progressLabel.Text = doneCount + " / " + rows.Count + " steps";
        }

        public void CompleteExecution(bool success, string summary)
        {
            refreshTimer.Stop();
            titleLabel.Text = "Completed: " + titleLabel.Text;
            if (!success) titleLabel.ForeColor = Color.FromArgb(200, 40, 40);
            else titleLabel.ForeColor = Color.FromArgb(0, 100, 50);

            progressLabel.Text = summary;

            var t = new Timer { Interval = 15000 };
            t.Tick += (s, e) =>
            {
                Visible = false;
                t.Stop();
                t.Dispose();
            };
            t.Start();
        }

        public void ClearProgress()
        {
            refreshTimer.Stop();
            stepPanel.Controls.Clear();
            rows.Clear();
            progressBar.Value = 0;
            progressLabel.Text = "";
            Visible = false;
        }

        void UpdateDurations()
        {
            foreach (var row in rows)
            {
                if (row.StartedAt != null)
                    row.Duration.Text = ElapsedString(DateTime.Now - row.StartedAt.Value);
            }
        }

        StepRow CreateStepRow(int number, string description)
        {
            var panel = new Panel
            {
                Width = 185,
                Height = 36,
                Margin = new Padding(0, 1, 0, 1),
                BackColor = Color.White
            };

            var dot = new Label
            {
                Text = "",
                Size = new Size(10, 10),
                Location = new Point(8, 13),
                BackColor = Color.FromArgb(200, 200, 200),
                BorderStyle = BorderStyle.None
            };

            var numLabel = new Label
            {
                Text = "#" + number,
                Font = new Font("Segoe UI", 6),
                ForeColor = Color.FromArgb(140, 140, 140),
                Location = new Point(22, 2),
                AutoSize = true
            };

            var desc = new Label
            {
                Text = description,
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.FromArgb(40, 40, 40),
                Location = new Point(22, 14),
                AutoSize = true,
                MaximumSize = new Size(100, 20)
            };

            var status = new Label
            {
                Text = "Pending",
                Font = new Font("Segoe UI", 7),
                ForeColor = Color.FromArgb(140, 140, 140),
                Location = new Point(130, 2),
                AutoSize = true
            };

            var duration = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 6),
                ForeColor = Color.FromArgb(160, 160, 160),
                Location = new Point(130, 16),
                AutoSize = true
            };

            panel.Controls.Add(dot);
            panel.Controls.Add(numLabel);
            panel.Controls.Add(desc);
            panel.Controls.Add(status);
            panel.Controls.Add(duration);

            return new StepRow { Panel = panel, Dot = dot, Desc = desc, Status = status, Duration = duration };
        }

        static string ElapsedString(TimeSpan ts)
        {
            if (ts.TotalHours >= 1) return ts.Hours + "h " + ts.Minutes + "m";
            if (ts.TotalMinutes >= 1) return ts.Minutes + "m " + ts.Seconds + "s";
            return ts.Seconds + "." + ts.Milliseconds / 100 + "s";
        }
    }
}
