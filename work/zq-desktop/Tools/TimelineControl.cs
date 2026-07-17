using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ZhuaQianDesktopApp
{
    public class TimelineControl : UserControl
    {
        private List<TaskTimelineItem> timelineItems;
        private int currentItemIndex = -1;
        private Timer timelineUpdateTimer;
        private Panel contentPanel;
        private Label timelineTitle;
        private SplitContainer timelineSplit;
        private Panel timelineListPanel;
        private PictureBox timelineIcon;
        private Label timelineStatus;
        private Label timelineTime;
        private Label timelineDescription;
        private FlowLayoutPanel timelineNavigation;

        public TimelineControl()
        {
            InitializeComponent();
            timelineItems = new List<TaskTimelineItem>();
            SetupTimeline();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(600, 400);
            this.Font = new Font("Microsoft YaHei UI", 9f);
            this.BackColor = Color.White;

            timelineSplit = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                Panel1MinSize = 60,
                Panel2MinSize = 200,
                SplitterWidth = 2,
                IsSplitterFixed = false
            };
            this.Controls.Add(timelineSplit);

            timelineListPanel = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.FixedSingle
            };
            timelineSplit.Panel1.Controls.Add(timelineListPanel);

            contentPanel = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(16)
            };
            timelineSplit.Panel2.Controls.Add(contentPanel);

            timelineTitle = new Label()
            {
                Text = "Execution Timeline",
                Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(16, 16),
                AutoSize = true
            };
            contentPanel.Controls.Add(timelineTitle);

            timelineStatus = new Label()
            {
                Text = "--",
                Font = new Font("Microsoft YaHei UI", 10f),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(16, 60),
                AutoSize = true
            };
            contentPanel.Controls.Add(timelineStatus);

            timelineTime = new Label()
            {
                Text = "",
                Font = new Font("Microsoft YaHei UI", 8f),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(16, 90),
                AutoSize = true
            };
            contentPanel.Controls.Add(timelineTime);

            timelineDescription = new Label()
            {
                Text = "Select an item from the timeline to view details",
                Font = new Font("Microsoft YaHei UI", 9f),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(16, 130),
                AutoSize = true,
                Width = 560
            };
            contentPanel.Controls.Add(timelineDescription);

            timelineNavigation = new FlowLayoutPanel()
            {
                Location = new Point(16, 170),
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(0)
            };
            contentPanel.Controls.Add(timelineNavigation);
        }

        private void SetupTimeline()
        {
            timelineListPanel.Controls.Clear();
            timelineNavigation.Controls.Clear();

            for (int i = 0; i < timelineItems.Count; i++)
            {
                var item = timelineItems[i];
                var itemPanel = new Panel()
                {
                    Size = new Size(200, 50),
                    Location = new Point(10, 10 + i * 60),
                    BackColor = item == currentItem ? Color.FromArgb(239, 246, 255) : Color.White,
                    BorderStyle = BorderStyle.FixedSingle,
                    Cursor = Cursors.Hand
                };

                var itemNumber = new Label()
                {
                    Text = (i + 1).ToString(),
                    Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
                    ForeColor = item == currentItem ? Color.FromArgb(37, 99, 235) : Color.FromArgb(100, 116, 139),
                    Location = new Point(8, 8),
                    AutoSize = true,
                    Width = 30,
                    Height = 30
                };
                itemPanel.Controls.Add(itemNumber);

                var itemStatus = new Label()
                {
                    Text = GetStatusEmoji(item.Status),
                    Font = new Font("Segoe UI Emoji", 12f),
                    Location = new Point(50, 8),
                    AutoSize = true
                };
                itemPanel.Controls.Add(itemStatus);

                var itemTitle = new Label()
                {
                    Text = item.Title?.Length > 20 ? item.Title.Substring(0, 20) + "..." : item.Title,
                    Font = new Font("Microsoft YaHei UI", 9f),
                    ForeColor = item == currentItem ? Color.FromArgb(30, 41, 59) : Color.FromArgb(71, 85, 105),
                    Location = new Point(70, 12),
                    AutoSize = true,
                    Width = 120
                };
                itemPanel.Controls.Add(itemTitle);

                var itemTime = new Label()
                {
                    Text = item.TimeStamp?.ToString("HH:mm:ss"),
                    Font = new Font("Microsoft YaHei UI", 7f),
                    ForeColor = Color.FromArgb(148, 163, 184),
                    Location = new Point(70, 28),
                    AutoSize = true
                };
                itemPanel.Controls.Add(itemTime);

                itemPanel.Click += (s, e) => { SelectTimelineItem(i); };
                ((Panel)s).MouseEnter += (s, e) => { itemPanel.BackColor = Color.FromArgb(239, 246, 255); };
                ((Panel)s).MouseLeave += (s, e) => { itemPanel.BackColor = (item == currentItem) ? Color.FromArgb(239, 246, 255) : Color.White; };

                timelineListPanel.Controls.Add(itemPanel);

                var navButton = new Button()
                {
                    Text = (i + 1).ToString(),
                    Size = new Size(28, 28),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = item == currentItem ? Color.FromArgb(37, 99, 235) : Color.FromArgb(226, 232, 240),
                    ForeColor = item == currentItem ? Color.White : Color.FromArgb(71, 85, 105),
                    Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold)
                };
                navButton.FlatAppearance.BorderSize = 0;
                navButton.Click += (s, e) => { SelectTimelineItem(i); };
                timelineNavigation.Controls.Add(navButton);
            }

            UpdateTimelineContent();
        }

        private void UpdateTimelineContent()
        {
            if (currentItemIndex >= 0 && currentItemIndex < timelineItems.Count)
            {
                var item = timelineItems[currentItemIndex];

                timelineStatus.Text = item.Title;
                timelineStatus.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
                timelineStatus.ForeColor = GetStatusColor(item.Status);

                timelineTime.Text = item.TimeStamp?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";

                timelineDescription.Text = $"Status: {item.Status}\nStep: {item.Step}" +
                                        (item.Details != null ? "\nDetails: " + item.Details : "") +
                                        (item.OutputFile != null ? "\nOutput: " + item.OutputFile : "");

                timelineDescription.ForeColor = Color.FromArgb(71, 85, 105);

                timelineNavigation.Controls.Clear();
                for (int i = 0; i < timelineItems.Count; i++)
                {
                    var itemNav = timelineItems[i];
                    var navButton = new Button()
                    {
                        Text = (i + 1).ToString(),
                        Size = new Size(30, 30),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = i == currentItemIndex ? GetStatusColor(itemNav.Status) : Color.FromArgb(241, 245, 249),
                        ForeColor = Color.White,
                        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
                        Margin = new Padding(2, 2, 2, 2)
                    };
                    navButton.FlatAppearance.BorderSize = 0;
                    navButton.Click += (s, e) => { SelectTimelineItem(i); };
                    timelineNavigation.Controls.Add(navButton);
                }
            }
        }

        private void SelectTimelineItem(int index)
        {
            if (index < 0 || index >= timelineItems.Count) return;

            currentItemIndex = index;
            SetupTimeline();
        }

        private string GetStatusEmoji(string status)
        {
            return status?.ToLower() switch
            {
                "pending" or "needs_input" => "⏳",
                "running" => "🔄",
                "ready_for_review" or "completed" => "✅",
                "failed" => "❌",
                "cancelled" => "⏸️",
                "editing" => "✏️",
                _ => "⚪"
            };
        }

        private Color GetStatusColor(string status)
        {
            return status?.ToLower() switch
            {
                "pending" or "needs_input" => Color.FromArgb(251, 146, 60),
                "running" => Color.FromArgb(59, 130, 246),
                "ready_for_review" or "completed" => Color.FromArgb(34, 197, 94),
                "failed" => Color.FromArgb(239, 68, 68),
                "cancelled" => Color.FromArgb(107, 114, 128),
                "editing" => Color.FromArgb(139, 92, 246),
                _ => Color.FromArgb(156, 163, 175)
            };
        }

        public void AddTimelineItem(string title, string status, DateTime? timeStamp, int step, string details = null, string outputFile = null)
        {
            timelineItems.Add(new TaskTimelineItem
            {
                Title = title,
                Status = status,
                TimeStamp = timeStamp ?? DateTime.Now,
                Step = step,
                Details = details,
                OutputFile = outputFile
            });

            if (currentItemIndex == -1)
                SelectTimelineItem(0);
            else
                SetupTimeline();
        }

        public void ClearTimeline()
        {
            timelineItems.Clear();
            currentItemIndex = -1;
            SetupTimeline();
        }
    }
}
