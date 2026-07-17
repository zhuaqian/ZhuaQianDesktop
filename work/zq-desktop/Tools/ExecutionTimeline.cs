using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ZhuaQianDesktopApp.Tools
{
    public enum StepStatus
    {
        Pending,
        Running,
        Success,
        Failed,
        Skipped
    }

    public class TimelineStep
    {
        public int Number { get; set; }
        public string Description { get; set; }
        public StepStatus Status { get; set; }
        public string Duration { get; set; }
        public string Detail { get; set; }
    }

    public class ExecutionTimeline : UserControl
    {
        readonly List<TimelineStep> steps = new List<TimelineStep>();
        readonly VScrollBar scrollBar;
        int hoverIndex = -1;

        public ExecutionTimeline()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            MinimumSize = new Size(200, 60);
            scrollBar = new VScrollBar { Dock = DockStyle.Right, SmallChange = 20, LargeChange = 80 };
            scrollBar.ValueChanged += (s, e) => Invalidate();
            Controls.Add(scrollBar);
            Resize += (s, e) => UpdateScrollBar();
        }

        public void AddStep(string description)
        {
            steps.Add(new TimelineStep
            {
                Number = steps.Count + 1,
                Description = description,
                Status = StepStatus.Pending,
                Duration = "",
                Detail = ""
            });
            UpdateScrollBar();
            Invalidate();
        }

        public void UpdateStep(int index, StepStatus status, string duration = "", string detail = "")
        {
            if (index < 0 || index >= steps.Count) return;
            steps[index].Status = status;
            if (!string.IsNullOrEmpty(duration)) steps[index].Duration = duration;
            if (!string.IsNullOrEmpty(detail)) steps[index].Detail = detail;
            Invalidate();
        }

        public void ClearSteps()
        {
            steps.Clear();
            UpdateScrollBar();
            Invalidate();
        }

        public int StepCount { get { return steps.Count; } }

        void UpdateScrollBar()
        {
            int contentH = steps.Count * 56 + 20;
            int visibleH = Height - 4;
            scrollBar.Maximum = Math.Max(0, contentH - visibleH);
            scrollBar.LargeChange = visibleH;
            scrollBar.Visible = contentH > visibleH;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);
            if (steps.Count == 0)
            {
                using (var b = new SolidBrush(Color.FromArgb(160, 160, 160)))
                    e.Graphics.DrawString("No steps yet", Font, b, 10, 10);
                return;
            }

            int scrollOffset = scrollBar.Value;
            int dotX = 24;
            int leftMargin = 48;

            for (int i = 0; i < steps.Count; i++)
            {
                int y = i * 56 + 10 - scrollOffset;
                if (y < -50 || y > Height + 10) continue;

                var step = steps[i];
                Color dotColor = Color.Gray;
                Color lineColor = Color.FromArgb(220, 220, 220);

                switch (step.Status)
                {
                    case StepStatus.Running: dotColor = Color.FromArgb(0, 120, 200); break;
                    case StepStatus.Success: dotColor = Color.FromArgb(0, 160, 60); break;
                    case StepStatus.Failed: dotColor = Color.FromArgb(200, 40, 40); break;
                    case StepStatus.Skipped: dotColor = Color.FromArgb(180, 180, 180); break;
                    default: dotColor = Color.FromArgb(200, 200, 200); break;
                }

                // Vertical line
                if (i < steps.Count - 1)
                {
                    int nextY = (i + 1) * 56 + 10 - scrollOffset;
                    using (var p = new Pen(lineColor, 2))
                        e.Graphics.DrawLine(p, dotX, y + 14, dotX, nextY);
                }

                // Dot
                bool hovered = (i == hoverIndex);
                int dotSize = hovered ? 12 : 10;
                using (var dotBrush = new SolidBrush(dotColor))
                    e.Graphics.FillEllipse(dotBrush, dotX - dotSize / 2, y + 14 - dotSize / 2, dotSize, dotSize);
                if (step.Status == StepStatus.Running)
                {
                    using (var pulsePen = new Pen(Color.FromArgb(100, 0, 120, 200), 2))
                        e.Graphics.DrawEllipse(pulsePen, dotX - 8, y + 6, 16, 16);
                }

                // Step number
                using (var numBrush = new SolidBrush(Color.FromArgb(120, 120, 120)))
                    e.Graphics.DrawString("#" + step.Number, new Font("Segoe UI", 7), numBrush, leftMargin, y);

                // Description
                using (var descBrush = new SolidBrush(Color.FromArgb(30, 30, 30)))
                    e.Graphics.DrawString(step.Description, new Font("Segoe UI", 9), descBrush, leftMargin, y + 14);

                // Status + Duration
                string statusText = step.Status.ToString();
                if (!string.IsNullOrEmpty(step.Duration)) statusText += " " + step.Duration;
                using (var statusBrush = new SolidBrush(Color.FromArgb(120, 120, 120)))
                    e.Graphics.DrawString(statusText, new Font("Segoe UI", 7), statusBrush, leftMargin, y + 32);

                // Detail on hover
                if (hovered && !string.IsNullOrEmpty(step.Detail))
                {
                    using (var detailBrush = new SolidBrush(Color.FromArgb(80, 80, 80)))
                        e.Graphics.DrawString(step.Detail, new Font("Segoe UI", 7), detailBrush, leftMargin + 80, y + 32);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int scrollOffset = scrollBar.Value;
            int newHover = -1;
            for (int i = 0; i < steps.Count; i++)
            {
                int y = i * 56 + 10 - scrollOffset;
                if (e.Y >= y && e.Y <= y + 50) { newHover = i; break; }
            }
            if (newHover != hoverIndex) { hoverIndex = newHover; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverIndex >= 0) { hoverIndex = -1; Invalidate(); }
        }
    }
}
