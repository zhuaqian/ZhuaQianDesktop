using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.IO;

namespace ZhuaQianDesktopApp
{
    public class RightPanel : UserControl
    {
        private Panel tabsHeaderPanel;
        private TabPage contextTab;
        private TabPage filesTab;
        private TabPage outputsTab;
        private TabPage knowledgeTab;
        private TabPage auditTab;
        private TabControl rightTabControl;
        private Label contextTitle;
        private Label filesTitle;
        private Label outputsTitle;
        private Label knowledgeTitle;
        private Label auditTitle;
        private PictureBox contextIcon;
        private PictureBox filesIcon;
        private PictureBox outputsIcon;
        private PictureBox knowledgeIcon;
        private PictureBox auditIcon;
        private StatusStrip statusStrip;
        private ToolStripLabel statusLabel;
        private ToolStripProgressBar progressBar;
        private Label panelTitleLabel;
        private List<RightPanelTab> availableTabs;
        private string activeTab = "context";

        public RightPanel()
        {
            InitializeComponent();
            SetupTabs();
            UpdateTabCounts();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(280, 500);
            this.Font = new Font("Microsoft YaHei UI", 9f);
            this.BackColor = Color.White;
            this.Padding = new Padding(0);

            var mainLayout = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                RowStyles = {
                    new TableRowStyle(SizeType.Absolute, 48),     // Tab header
                    new TableRowStyle(SizeType.Percent, 1),      // Tab content
                    new TableRowStyle(SizeType.Absolute, 40)     // Status bar
                },
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            this.Controls.Add(mainLayout);

            tabsHeaderPanel = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(0)
            };
            mainLayout.Controls.Add(tabsHeaderPanel, 0, 0);

            rightTabControl = new TabControl()
            {
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9f),
                ItemSize = new Size(60, 30),
                SizeMode = TabSizeMode.Fixed,
                Appearance = TabAppearance.Normal,
                Multiline = true
            };
            mainLayout.Controls.Add(rightTabControl, 0, 1);

            statusStrip = new StatusStrip()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(241, 245, 249),
                Height = 40
            };
            mainLayout.Controls.Add(statusStrip, 0, 2);

            statusLabel = new ToolStripLabel()
            {
                Text = "Ready",
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Microsoft YaHei UI", 8f),
                AutoSize = true,
                Spring = true
            };
            statusStrip.Items.Add(statusLabel);

            progressBar = new ToolStripProgressBar()
            {
                Width = 100,
                Value = 0,
                Visible = false
            };
            statusStrip.Items.Add(progressBar);

            panelTitleLabel = new Label()
            {
                Text = "Project Details",
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(16, 12),
                AutoSize = true
            };
            tabsHeaderPanel.Controls.Add(panelTitleLabel);

            SetupTabIcons();
        }

        private void SetupTabIcons()
        {
            int iconY = 12;
            int iconX = 60;

            contextIcon = CreateTabIcon("📁", Color.FromArgb(59, 130, 246), iconX, iconY);
            tabsHeaderPanel.Controls.Add(contextIcon);

            contextTitle = CreateTabTitle("Context", "@file: src/", iconX + 40, iconY);
            tabsHeaderPanel.Controls.Add(contextTitle);

            filesIcon = CreateTabIcon("📄", Color.FromArgb(34, 197, 94), iconX + 180, iconY);
            tabsHeaderPanel.Controls.Add(filesIcon);

            filesTitle = CreateTabTitle("Files", "12 items", iconX + 220, iconY);
            tabsHeaderPanel.Controls.Add(filesTitle);

            outputsIcon = CreateTabIcon("📊", Color.FromArgb(139, 92, 246), iconX + 360, iconY);
            tabsHeaderPanel.Controls.Add(outputsIcon);

            outputsTitle = CreateTabTitle("Outputs", "28 files", iconX + 400, iconY);
            tabsHeaderPanel.Controls.Add(outputsTitle);

            knowledgeIcon = CreateTabIcon("🧠", Color.FromArgb(251, 146, 60), iconX + 540, iconY);
            tabsHeaderPanel.Controls.Add(knowledgeIcon);

            knowledgeTitle = CreateTabTitle("Knowledge", "156 chunks", iconX + 580, iconY);
            tabsHeaderPanel.Controls.Add(knowledgeTitle);

            auditIcon = CreateTabIcon("📋", Color.FromArgb(239, 68, 68), iconX + 740, iconY);
            tabsHeaderPanel.Controls.Add(auditIcon);

            auditTitle = CreateTabTitle("Audit", "4 entries", iconX + 780, iconY);
            tabsHeaderPanel.Controls.Add(auditTitle);
        }

        private PictureBox CreateTabIcon(string symbol, Color iconColor, int x, int y)
        {
            var icon = new PictureBox()
            {
                Image = CreateTextImage(symbol, 24, iconColor, Color.White),
                Size = new Size(24, 24),
                Location = new Point(x, y),
                SizeMode = PictureBoxSizeMode.StretchImage
            };
            return icon;
        }

        private Label CreateTabTitle(string title, string subtitle, int x, int y)
        {
            var titleLabel = new Label()
            {
                Text = title,
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(x, y),
                AutoSize = true
            };
            tabsHeaderPanel.Controls.Add(titleLabel);

            var subtitleLabel = new Label()
            {
                Text = subtitle,
                Font = new Font("Microsoft YaHei UI", 7f),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(x, y + 16),
                AutoSize = true
            };
            tabsHeaderPanel.Controls.Add(subtitleLabel);

            return titleLabel;
        }

        private System.Drawing.Bitmap CreateTextImage(string text, int width, Color backColor, Color foreColor)
        {
            var bitmap = new Bitmap(width, width);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(backColor);
                using (var font = new Font("Segoe UI Emoji", width * 0.6f, FontStyle.Regular))
                {
                    var stringFormat = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    graphics.DrawString(text, font, new SolidBrush(foreColor), width / 2, width / 2, stringFormat);
                }
            }
            return bitmap;
        }

        private void SetupTabs()
        {
            contextTab = new TabPage("context")
            {
                Text = "📂 Context",
                Padding = new Padding(12)
            };
            contextTab.Controls.Add(CreateContextTabContent());
            rightTabControl.TabPages.Add(contextTab);

            filesTab = new TabPage("files")
            {
                Text = "📝 Files",
                Padding = new Padding(12)
            };
            filesTab.Controls.Add(CreateFilesTabContent());
            rightTabControl.TabPages.Add(filesTab);

            outputsTab = new TabPage("outputs")
            {
                Text = "📈 Outputs",
                Padding = new Padding(12)
            };
            outputsTab.Controls.Add(CreateOutputsTabContent());
            rightTabControl.TabPages.Add(outputsTab);

            knowledgeTab = new TabPage("knowledge")
            {
                Text = "🧠 Knowledge",
                Padding = new Padding(12)
            };
            knowledgeTab.Controls.Add(CreateKnowledgeTabContent());
            rightTabControl.TabPages.Add(knowledgeTab);

            auditTab = new TabPage("audit")
            {
                Text = "📋 Audit",
                Padding = new Padding(12)
            };
            auditTab.Controls.Add(CreateAuditTabContent());
            rightTabControl.TabPages.Add(auditTab);

            rightTabControl.SelectedIndex = 0;
            UpdateTabStyles();
        }

        private Control CreateContextTabContent()
        {
            var panel = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var title = new Label()
            {
                Text = "📁 Context",
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(16, 16),
                AutoSize = true
            };
            panel.Controls.Add(title);

            var contextList = new ListBox()
            {
                Location = new Point(16, 60),
                Size = new Size(248, 200),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(249, 250, 251)
            };
            contextList.Items.AddRange(new object[] {
                "@folder: C:\Users\Current\project",
                "@file: src\index.ts",
                "@file: src\components\App.tsx",
                "@file: README.md",
                "@folder: docs",
                "@file: assets\logo.png"
            });
            panel.Controls.Add(contextList);

            var captureBtn = new Button()
            {
                Text = "📸 Capture Current Window",
                Location = new Point(16, 270),
                Size = new Size(248, 36),
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };
            captureBtn.FlatAppearance.BorderSize = 0;
            captureBtn.Click += (s, e) => CaptureContext();
            panel.Controls.Add(captureBtn);

            var refreshBtn = new Button()
            {
                Text = "🔄 Refresh",
                Location = new Point(16, 316),
                Size = new Size(248, 28),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(71, 85, 105),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };
            refreshBtn.FlatAppearance.BorderSize = 0;
            refreshBtn.Click += (s, e) => RefreshContext();
            panel.Controls.Add(refreshBtn);

            return panel;
        }

        private Control CreateFilesTabContent()
        {
            var panel = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var title = new Label()
            {
                Text = "📄 Project Files",
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(16, 16),
                AutoSize = true
            };
            panel.Controls.Add(title);

            var fileList = new ListBox()
            {
                Location = new Point(16, 60),
                Size = new Size(248, 200),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(249, 250, 251)
            };
            var projectFiles = new[] {
                "src\index.ts - Main application entry",
                "src\components\App.tsx - React component",
                "src\utils\api.ts - API client",
                "docs\README.md - Project documentation",
                "assets\logo.png - Application logo",
                "package.json - Node package configuration"
            };
            fileList.Items.AddRange(projectFiles);
            panel.Controls.Add(fileList);

            var refreshBtn = new Button()
            {
                Text = "🔄 Refresh File List",
                Location = new Point(16, 270),
                Size = new Size(248, 36),
                BackColor = Color.FromArgb(34, 197, 94),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };
            refreshBtn.FlatAppearance.BorderSize = 0;
            refreshBtn.Click += (s, e) => RefreshFiles();
            panel.Controls.Add(refreshBtn);

            return panel;
        }

        private Control CreateOutputsTabContent()
        {
            var panel = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var title = new Label()
            {
                Text = "📊 Generated Outputs",
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(16, 16),
                AutoSize = true
            };
            panel.Controls.Add(title);

            var outputList = new ListBox()
            {
                Location = new Point(16, 60),
                Size = new Size(248, 200),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(249, 250, 251)
            };
            var outputs = new[] {
                "report-2026-07-10.pdf (2.4MB) - Generated analysis",
                "chart-data.json (156KB) - Chart configuration",
                "api-documentation.md (45KB) - API docs",
                "style-guide.md (12KB) - Design guidelines",
                "scripts\build.bat (8KB) - Build script",
                "scripts\test.ps1 (12KB) - Test runner"
            };
            outputList.Items.AddRange(outputs);
            panel.Controls.Add(outputList);

            var openBtn = new Button()
            {
                Text = "📂 Open Output Folder",
                Location = new Point(16, 270),
                Size = new Size(248, 36),
                BackColor = Color.FromArgb(139, 92, 246),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };
            openBtn.FlatAppearance.BorderSize = 0;
            openBtn.Click += (s, e) => OpenOutputsFolder();
            panel.Controls.Add(openBtn);

            return panel;
        }

        private Control CreateKnowledgeTabContent()
        {
            var panel = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var title = new Label()
            {
                Text = "🧠 Knowledge Base",
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(16, 16),
                AutoSize = true
            };
            panel.Controls.Add(title);

            var knowledgeList = new ListBox()
            {
                Location = new Point(16, 60),
                Size = new Size(248, 200),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(249, 250, 251)
            };
            var knowledgeItems = new[] {
                " product-api.md (8.4KB) - API specification",
                " design-patterns.md (12.6KB) - Design guidelines",
                "code-quality-guidelines.md (15.2KB) - Code standards",
                " project-architecture.md (22.4KB) - System overview",
                " development-workflow.md (18.8KB) - Development process"
            };
            knowledgeList.Items.AddRange(knowledgeItems);
            panel.Controls.Add(knowledgeList);

            var refreshBtn = new Button()
            {
                Text = "🔄 Refresh Knowledge Base",
                Location = new Point(16, 270),
                Size = new Size(248, 36),
                BackColor = Color.FromArgb(251, 146, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };
            refreshBtn.FlatAppearance.BorderSize = 0;
            refreshBtn.Click += (s, e) => RefreshKnowledge();
            panel.Controls.Add(refreshBtn);

            return panel;
        }

        private Control CreateAuditTabContent()
        {
            var panel = new Panel()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var title = new Label()
            {
                Text = "📋 Audit Log",
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(16, 16),
                AutoSize = true
            };
            panel.Controls.Add(title);

            var auditList = new ListBox()
            {
                Location = new Point(16, 60),
                Size = new Size(248, 200),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(249, 250, 251)
            };
            var auditEntries = new[] {
                "2026-07-11 14:23:15 - User login - Success",
                "2026-07-11 14:22:45 - File read: src/index.ts - Allowed",
                "2026-07-11 14:21:30 - Plugin execution: build.bat - Completed",
                "2026-07-11 14:20:15 - Permission denied: attempt to modify system files"
            };
            auditList.Items.AddRange(auditEntries);
            panel.Controls.Add(auditList);

            var clearBtn = new Button()
            {
                Text = "🧹 Clear Audit Log",
                Location = new Point(16, 270),
                Size = new Size(248, 36),
                BackColor = Color.FromArgb(239, 68, 68),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };
            clearBtn.FlatAppearance.BorderSize = 0;
            clearBtn.Click += (s, e) => ClearAuditLog();
            panel.Controls.Add(clearBtn);

            return panel;
        }

        private void UpdateTabStyles()
        {
            contextIcon.Image = CreateTextImage("📁", 24, Color.FromArgb(37, 99, 235), Color.White);
            filesIcon.Image = CreateTextImage("📄", 24, Color.FromArgb(34, 197, 94), Color.White);
            outputsIcon.Image = CreateTextImage("📊", 24, Color.FromArgb(139, 92, 246), Color.White);
            knowledgeIcon.Image = CreateTextImage("🧠", 24, Color.FromArgb(251, 146, 60), Color.White);
            auditIcon.Image = CreateTextImage("📋", 24, Color.FromArgb(239, 68, 68), Color.White);
        }

        private void CaptureContext()
        {
            statusLabel.Text = "Capturing context...";
            progressBar.Visible = true;
            progressBar.Value = 30;

            System.Threading.ThreadPool.QueueUserWorkItem((state) =>
            {
                System.Windows.Forms.Application.Invoke((MethodInvoker)delegate
                {
                    statusLabel.Text = "Context captured successfully";
                    progressBar.Visible = false;
                    UpdateTabCounts();
                });
            });
        }

        private void RefreshContext()
        {
            statusLabel.Text = "Refreshing context...";
            progressBar.Visible = true;
            progressBar.Value = 50;

            System.Threading.ThreadPool.QueueUserWorkItem((state) =>
            {
                System.Windows.Forms.Application.Invoke((MethodInvoker)delegate
                {
                    statusLabel.Text = "Context refreshed";
                    progressBar.Visible = false;
                });
            });
        }

        private void RefreshFiles()
        {
            statusLabel.Text = "Refreshing files...";
            progressBar.Visible = true;
            progressBar.Value = 40;

            System.Threading.ThreadPool.QueueUserWorkItem((state) =>
            {
                System.Windows.Forms.Application.Invoke((MethodInvoker)delegate
                {
                    statusLabel.Text = "Files refreshed";
                    progressBar.Visible = false;
                });
            });
        }

        private void RefreshKnowledge()
        {
            statusLabel.Text = "Refreshing knowledge base...";
            progressBar.Visible = true;
            progressBar.Value = 60;

            System.Threading.ThreadPool.QueueUserWorkItem((state) =>
            {
                System.Windows.Forms.Application.Invoke((MethodInvoker)delegate
                {
                    statusLabel.Text = "Knowledge base refreshed";
                    progressBar.Visible = false;
                });
            });
        }

        private void OpenOutputsFolder()
        {
            statusLabel.Text = "Opening outputs folder...";
            progressBar.Visible = true;
            progressBar.Value = 70;

            System.Threading.ThreadPool.QueueUserWorkItem((state) =>
            {
                System.Windows.Forms.Application.Invoke((MethodInvoker)delegate
                {
                    statusLabel.Text = "Outputs folder opened";
                    progressBar.Visible = false;
                });
            });
        }

        private void ClearAuditLog()
        {
            statusLabel.Text = "Clearing audit log...";
            progressBar.Visible = true;
            progressBar.Value = 80;

            System.Threading.ThreadPool.QueueUserWorkItem((state) =>
            {
                System.Windows.Forms.Application.Invoke((MethodInvoker)delegate
                {
                    statusLabel.Text = "Audit log cleared";
                    progressBar.Visible = false;
                });
            });
        }

        private void UpdateTabCounts()
        {
            if (contextTitle != null)
                contextTitle.Text = "Context (@file: src/)";
            if (filesTitle != null)
                filesTitle.Text = "Files (12 items)";
            if (outputsTitle != null)
                outputsTitle.Text = "Outputs (28 files)";
            if (knowledgeTitle != null)
                knowledgeTitle.Text = "Knowledge (156 chunks)";
            if (auditTitle != null)
                auditTitle.Text = "Audit (4 entries)";
        }
    }
}
