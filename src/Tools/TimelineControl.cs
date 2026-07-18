using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ZhuaQianDesktopApp
{
    public class TimelineControl : UserControl
    {
        readonly List<TaskTimelineItem> timelineItems = new List<TaskTimelineItem>();
        int currentItemIndex = -1;
        Panel timelineListPanel;
        Label timelineStatus;
        Label timelineTime;
        Label timelineDescription;
        FlowLayoutPanel timelineNavigation;

        public TimelineControl()
        {
            InitializeComponent();
            SetupTimeline();
        }

        void InitializeComponent()
        {
            Size = new Size(600, 400);
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.White;

            var split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.Panel1MinSize = 60;
            split.Panel2MinSize = 200;
            split.SplitterWidth = 2;
            Controls.Add(split);

            timelineListPanel = new Panel();
            timelineListPanel.Dock = DockStyle.Fill;
            timelineListPanel.BackColor = Color.FromArgb(248, 250, 252);
            timelineListPanel.BorderStyle = BorderStyle.FixedSingle;
            split.Panel1.Controls.Add(timelineListPanel);

            var contentPanel = new Panel();
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.BackColor = Color.White;
            contentPanel.Padding = new Padding(16);
            split.Panel2.Controls.Add(contentPanel);

            var title = new Label();
            title.Text = "Execution Timeline";
            title.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(30, 41, 59);
            title.Location = new Point(16, 16);
            title.AutoSize = true;
            contentPanel.Controls.Add(title);

            timelineStatus = new Label();
            timelineStatus.Text = "--";
            timelineStatus.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            timelineStatus.ForeColor = Color.FromArgb(30, 41, 59);
            timelineStatus.Location = new Point(16, 60);
            timelineStatus.AutoSize = true;
            contentPanel.Controls.Add(timelineStatus);

            timelineTime = new Label();
            timelineTime.Text = "";
            timelineTime.Font = new Font("Microsoft YaHei UI", 8f);
            timelineTime.ForeColor = Color.FromArgb(100, 116, 139);
            timelineTime.Location = new Point(16, 90);
            timelineTime.AutoSize = true;
            contentPanel.Controls.Add(timelineTime);

            timelineDescription = new Label();
            timelineDescription.Text = "Select an item from the timeline to view details";
            timelineDescription.Font = new Font("Microsoft YaHei UI", 9f);
            timelineDescription.ForeColor = Color.FromArgb(100, 116, 139);
            timelineDescription.Location = new Point(16, 130);
            timelineDescription.AutoSize = true;
            timelineDescription.MaximumSize = new Size(540, 0);
            contentPanel.Controls.Add(timelineDescription);

            timelineNavigation = new FlowLayoutPanel();
            timelineNavigation.Location = new Point(16, 190);
            timelineNavigation.AutoSize = true;
            timelineNavigation.WrapContents = true;
            contentPanel.Controls.Add(timelineNavigation);
        }

        void SetupTimeline()
        {
            timelineListPanel.Controls.Clear();
            timelineNavigation.Controls.Clear();

            for (int i = 0; i < timelineItems.Count; i++)
            {
                TaskTimelineItem item = timelineItems[i];
                bool selected = i == currentItemIndex;
                int index = i;

                var itemPanel = new Panel();
                itemPanel.Size = new Size(210, 50);
                itemPanel.Location = new Point(10, 10 + i * 60);
                itemPanel.BackColor = selected ? Color.FromArgb(239, 246, 255) : Color.White;
                itemPanel.BorderStyle = BorderStyle.FixedSingle;
                itemPanel.Cursor = Cursors.Hand;

                var itemNumber = new Label();
                itemNumber.Text = (i + 1).ToString();
                itemNumber.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
                itemNumber.ForeColor = selected ? Color.FromArgb(37, 99, 235) : Color.FromArgb(100, 116, 139);
                itemNumber.Location = new Point(8, 8);
                itemNumber.AutoSize = true;
                itemPanel.Controls.Add(itemNumber);

                var itemStatus = new Label();
                itemStatus.Text = GetStatusText(item.Status);
                itemStatus.Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold);
                itemStatus.Location = new Point(42, 10);
                itemStatus.AutoSize = true;
                itemPanel.Controls.Add(itemStatus);

                var itemTitle = new Label();
                itemTitle.Text = ShortText(item.Title, 22);
                itemTitle.Font = new Font("Microsoft YaHei UI", 9f);
                itemTitle.ForeColor = selected ? Color.FromArgb(30, 41, 59) : Color.FromArgb(71, 85, 105);
                itemTitle.Location = new Point(78, 10);
                itemTitle.AutoSize = true;
                itemPanel.Controls.Add(itemTitle);

                var itemTime = new Label();
                itemTime.Text = item.TimeStamp.HasValue ? item.TimeStamp.Value.ToString("HH:mm:ss") : "";
                itemTime.Font = new Font("Microsoft YaHei UI", 7f);
                itemTime.ForeColor = Color.FromArgb(148, 163, 184);
                itemTime.Location = new Point(78, 28);
                itemTime.AutoSize = true;
                itemPanel.Controls.Add(itemTime);

                itemPanel.Click += delegate { SelectTimelineItem(index); };
                timelineListPanel.Controls.Add(itemPanel);

                var navButton = new Button();
                navButton.Text = (i + 1).ToString();
                navButton.Size = new Size(30, 30);
                navButton.FlatStyle = FlatStyle.Flat;
                navButton.FlatAppearance.BorderSize = 0;
                navButton.BackColor = selected ? GetStatusColor(item.Status) : Color.FromArgb(241, 245, 249);
                navButton.ForeColor = selected ? Color.White : Color.FromArgb(71, 85, 105);
                navButton.Margin = new Padding(2);
                navButton.Click += delegate { SelectTimelineItem(index); };
                timelineNavigation.Controls.Add(navButton);
            }

            UpdateTimelineContent();
        }

        void UpdateTimelineContent()
        {
            if (currentItemIndex < 0 || currentItemIndex >= timelineItems.Count) return;

            TaskTimelineItem item = timelineItems[currentItemIndex];
            timelineStatus.Text = string.IsNullOrWhiteSpace(item.Title) ? "Untitled step" : item.Title;
            timelineStatus.ForeColor = GetStatusColor(item.Status);
            timelineTime.Text = item.TimeStamp.HasValue ? item.TimeStamp.Value.ToString("yyyy-MM-dd HH:mm:ss") : "";

            string text = "Status: " + (item.Status ?? "") + "\nStep: " + item.Step.ToString();
            if (!string.IsNullOrWhiteSpace(item.Details)) text += "\nDetails: " + item.Details;
            if (!string.IsNullOrWhiteSpace(item.OutputFile)) text += "\nOutput: " + item.OutputFile;
            timelineDescription.Text = text;
        }

        void SelectTimelineItem(int index)
        {
            if (index < 0 || index >= timelineItems.Count) return;
            currentItemIndex = index;
            SetupTimeline();
        }

        string GetStatusText(string status)
        {
            string value = status == null ? "" : status.ToLowerInvariant();
            if (value == "pending" || value == "needs_input") return "...";
            if (value == "running") return "RUN";
            if (value == "ready_for_review" || value == "completed") return "OK";
            if (value == "failed") return "ERR";
            if (value == "cancelled") return "X";
            if (value == "editing") return "EDIT";
            return "?";
        }

        Color GetStatusColor(string status)
        {
            string value = status == null ? "" : status.ToLowerInvariant();
            if (value == "pending" || value == "needs_input") return Color.FromArgb(251, 146, 60);
            if (value == "running") return Color.FromArgb(59, 130, 246);
            if (value == "ready_for_review" || value == "completed") return Color.FromArgb(34, 197, 94);
            if (value == "failed") return Color.FromArgb(239, 68, 68);
            if (value == "cancelled") return Color.FromArgb(107, 114, 128);
            if (value == "editing") return Color.FromArgb(139, 92, 246);
            return Color.FromArgb(156, 163, 175);
        }

        string ShortText(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Length <= max) return value;
            return value.Substring(0, max) + "...";
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

            if (currentItemIndex == -1) currentItemIndex = 0;
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
