using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Providers;

namespace ZhuaQianDesktopApp
{
    public class TaskInfo
    {
        public string Id;
        public string Title;
        public string Status;
        public string LastAction;
        public DateTime UpdatedAt;

        public override string ToString()
        {
            string title = string.IsNullOrWhiteSpace(Title) ? "Untitled task" : Title;
            return "[" + MainForm.TaskStatusLabel(Status) + "] " + title;
        }
    }

    public class IndexedDoc
    {
        public string DocId;
        public string ChunkId;
        public string Path;
        public string Name;
        public string Heading;
        public string Text;
        public string Summary;
        public string Tags;
        public string Layer;
        public int Offset;
        public long SizeBytes;
        public DateTime ModifiedAt;
    }

    public partial class MainForm : Form
    {
        const string DefaultModel = "gemini-flash-lite-latest";
        const string DefaultProvider = "Gemini";
        const string DefaultOpenRouterModel = "meta-llama/llama-3-8b-instruct:free";
        const string DefaultLocalApiUrl = "http://localhost:11434/api/chat";
        const string DefaultLocalModel = "llama3.1:8b";
        const int MaxExtractedChars = 24000;
        const long MaxInlineBytes = 20L * 1024 * 1024;
        const long MaxDocBytes = 50L * 1024 * 1024;
        const int HotkeyId = 9201;
        const uint ModAlt = 0x0001;
        const int WmHotkey = 0x0312;

        readonly string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZhuaQianDesktop");
        readonly string configPath;
        readonly string tasksDir;
        readonly string screenDir;
        readonly string indexPath;
        readonly string auditLogPath;
        readonly string actionLogPath;
        readonly string exportHistoryPath;
        readonly string outputsPath;
        readonly string rollbackDir;
        readonly Core.OutputsHub outputsHub;
        readonly Documents.OfficeExporter officeExporter = new Documents.OfficeExporter();
        readonly Documents.Redactor redactor = new Documents.Redactor();
        readonly Knowledge.Chunker chunker = new Knowledge.Chunker();
        readonly Tools.WebSearchClient webSearchClient = new Tools.WebSearchClient();
        readonly Tools.SystemDiagnostics systemDiagnostics = new Tools.SystemDiagnostics();
        readonly AgentPipelineFactory agentPipelineFactory;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        // Legacy fields (backward compat)
        string apiKey = "";
        string model = DefaultModel;
        string provider = DefaultProvider;
        string openRouterApiKey = "";
        string openRouterModel = DefaultOpenRouterModel;
        string localApiUrl = DefaultLocalApiUrl;
        string localModel = DefaultLocalModel;
        string embeddingModel = "nomic-embed-text";
        string relayUrl = "";
        string lastGeneratedFilePath = "";
        string lastExportNameHint = "";

        readonly ProviderManager providerManager = new ProviderManager();
        string pluginDir = "";
        string uiLanguage = "zh-Hans";
        string workMode = "Ask";
        bool enableHotkey = true;
        bool computerControlEnabled = false;
        bool allowAdvancedPlugins = false;
        bool permFileRead = true;
        bool permFileWrite = true;
        bool permFileMoveDelete = false;
        bool permProcessManage = false;
        bool permPluginRun = false;
        bool permScreenshot = true;
        bool permClipboard = false;
        bool permNetworkUpload = true;
        PermissionGate permGate = new PermissionGate();
        bool autoMode = false;
        List<string> allowedDirs = new List<string>();
        bool permAutomationInput = false;
        bool useStreaming = false;
        bool currentInfoMode = true;
        bool redactSensitive = true;
        bool clipboardMonitorEnabled = false;
        bool clipboardBusy = false;
        string lastClipboardText = "";
        string lastContextTitle = "";
        string lastContextProcess = "";
        DateTime lastContextAt = DateTime.MinValue;
        readonly ArrayList messages = new ArrayList();
        readonly ArrayList pendingParts = new ArrayList();
        readonly List<string> pendingLabels = new List<string>();
        readonly List<TaskInfo> tasks = new List<TaskInfo>();
        readonly List<IndexedDoc> knowledgeIndex = new List<IndexedDoc>();
        readonly Knowledge.VectorIndex vectorIndex;
        readonly Tools.UndoRedoManager undoRedo = new Tools.UndoRedoManager();
        string currentTaskId = "";
        string currentTaskTitle = "New task";
        string currentTaskStatus = "draft";
        string currentTaskLastAction = "";

        RichTextBox chat;
        TextBox input;
        Label attachLabel;
        Label modelLabel;
        Label currentTaskLabel;
        Action layoutTop;
        ListBox taskList;
        TextBox taskSearchBox;
        Button sendButton;
        Button uploadButton;
        Button saveTxtButton;
        Button openFolderButton;
        Button clipboardButton;
        Button powerButton;
        ComboBox modeCombo;
        Button sidebarToggleButton;
        SplitContainer mainSplit;
        Panel sidePanel;
        Panel rightPanel;
        Panel bottomPanel;
        Timer clipboardTimer;
        Timer liveTimer;
        string liveSessionId = "";
        string liveRelay = "";
        string liveLastHash = "";
        bool liveActive = false;
        bool suppressTaskSelection = false;
        bool hotkeyRegistered = false;
        bool sidebarExpanded = true;

        readonly HashSet<string> imageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
        readonly HashSet<string> textExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".txt", ".md", ".markdown", ".csv", ".json", ".jsonl", ".xml", ".html", ".htm", ".log", ".ini", ".cfg", ".yaml", ".yml", ".py", ".js", ".ts", ".css", ".sql"
        };
        readonly HashSet<string> docExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".docx", ".xlsx", ".xlsm", ".pptx", ".pdf", ".doc", ".xls", ".ppt",
            ".txt", ".md", ".markdown", ".csv", ".json", ".jsonl", ".xml", ".html", ".htm", ".log", ".ini", ".cfg", ".yaml", ".yml", ".py", ".js", ".ts", ".css", ".sql"
        };

        enum ZqButtonRole
        {
            Primary,
            Secondary,
            Ghost,
            Success,
            Warning,
            Danger
        }

        readonly Color zqWindowBg = Color.FromArgb(244, 244, 242);
        readonly Color zqPanelBg = Color.FromArgb(250, 250, 248);
        readonly Color zqSurface = Color.FromArgb(255, 255, 253);
        readonly Color zqSideBg = Color.FromArgb(239, 239, 235);
        readonly Color zqBorder = Color.FromArgb(218, 218, 212);
        readonly Color zqInk = Color.FromArgb(31, 35, 40);
        readonly Color zqMuted = Color.FromArgb(93, 99, 106);
        readonly Color zqAccent = Color.FromArgb(35, 91, 255);
        readonly Color zqSuccess = Color.FromArgb(16, 125, 84);
        readonly Color zqWarning = Color.FromArgb(181, 112, 24);
        readonly Color zqDanger = Color.FromArgb(181, 54, 43);

        void StyleButton(Button btn, ZqButtonRole role)
        {
            if (btn == null) return;
            btn.FlatStyle = FlatStyle.Flat;
            btn.UseVisualStyleBackColor = false;
            btn.Cursor = Cursors.Hand;
            btn.TextAlign = ContentAlignment.MiddleCenter;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 238, 242);
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(224, 228, 234);
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = zqBorder;
            btn.BackColor = zqSurface;
            btn.ForeColor = zqInk;

            if (role == ZqButtonRole.Primary)
            {
                btn.BackColor = zqInk;
                btn.ForeColor = Color.White;
                btn.FlatAppearance.BorderColor = zqInk;
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 54, 61);
                btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(20, 24, 28);
            }
            else if (role == ZqButtonRole.Ghost)
            {
                btn.BackColor = Color.Transparent;
                btn.ForeColor = zqMuted;
                btn.FlatAppearance.BorderColor = zqPanelBg;
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 241, 244);
            }
            else if (role == ZqButtonRole.Success)
            {
                btn.BackColor = Color.FromArgb(229, 246, 238);
                btn.ForeColor = zqSuccess;
                btn.FlatAppearance.BorderColor = Color.FromArgb(174, 219, 198);
            }
            else if (role == ZqButtonRole.Warning)
            {
                btn.BackColor = Color.FromArgb(255, 246, 224);
                btn.ForeColor = zqWarning;
                btn.FlatAppearance.BorderColor = Color.FromArgb(232, 205, 143);
            }
            else if (role == ZqButtonRole.Danger)
            {
                btn.BackColor = Color.FromArgb(255, 238, 236);
                btn.ForeColor = zqDanger;
                btn.FlatAppearance.BorderColor = Color.FromArgb(232, 184, 178);
            }
        }

        void StyleInput(Control control)
        {
            if (control == null) return;
            control.BackColor = zqSurface;
            control.ForeColor = zqInk;
            control.Font = new Font("Microsoft YaHei UI", 10);
        }

        void StyleList(ListBox list)
        {
            if (list == null) return;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.BackColor = zqSurface;
            list.ForeColor = zqInk;
            list.Font = new Font("Microsoft YaHei UI", 10);
        }

        void ApplyPowerButtonStyle()
        {
            if (powerButton == null) return;
            StyleButton(powerButton, computerControlEnabled ? ZqButtonRole.Success : ZqButtonRole.Warning);
        }

        public MainForm()
        {
            configPath = Path.Combine(configDir, "config.json");
            tasksDir = Path.Combine(configDir, "tasks");
            screenDir = Path.Combine(configDir, "screenshots");
            indexPath = Path.Combine(configDir, "knowledge-index.json");
            auditLogPath = Path.Combine(configDir, "audit.log");
            actionLogPath = Path.Combine(configDir, "actions.jsonl");
            exportHistoryPath = Path.Combine(configDir, "export-history.jsonl");
            outputsPath = Path.Combine(configDir, "outputs.jsonl");
            rollbackDir = Path.Combine(configDir, "rollback");
            outputsHub = new Core.OutputsHub(configDir);
            vectorIndex = new Knowledge.VectorIndex(configDir);
            agentPipelineFactory = new AgentPipelineFactory(auditLogPath, configDir, outputsHub, officeExporter, webSearchClient);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            LoadConfig();
            NormalizeModel();
            BuildUi();
            LoadKnowledgeIndex();
            LoadTasks();
            RefreshAttachLabel();
            Shown += (s, e) =>
            {
                ApplySidebarState();
                if (!HasUsableProviderKey()) ShowSettings();
            };
            FormClosing += (s, e) =>
            {
                SaveCurrentTask();
                if (hotkeyRegistered)
                {
                    UnregisterHotKey(Handle, HotkeyId);
                    hotkeyRegistered = false;
                }
            };
        }

        void BuildUi()
        {
            Text = "ZhuaQian Desktop v0.1";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1180, 740);
            MinimumSize = new Size(900, 560);
            Font = new Font("Microsoft YaHei UI", 10);
            BackColor = zqWindowBg;

            mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                SplitterWidth = 4,
                Panel1MinSize = 44,
                IsSplitterFixed = false
            };
            Controls.Add(mainSplit);

            sidePanel = new Panel { Dock = DockStyle.Fill, BackColor = zqSideBg };
            rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = zqSurface };
            mainSplit.Panel1.Controls.Add(sidePanel);
            mainSplit.Panel2.Controls.Add(rightPanel);

            var sideTitle = new Label
            {
                Text = "ZhuaQian",
                Font = new Font("Microsoft YaHei UI", 15, FontStyle.Bold),
                Location = new Point(14, 14),
                AutoSize = true,
                ForeColor = zqInk
            };
            sidePanel.Controls.Add(sideTitle);

            var sideSubtitle = new Label
            {
                Text = Tr("Agent workspace", "智能体工作台", "智能體工作台"),
                Location = new Point(16, 40),
                Size = new Size(190, 18),
                ForeColor = zqMuted,
                Font = new Font("Microsoft YaHei UI", 8)
            };
            sidePanel.Controls.Add(sideSubtitle);

            sidebarToggleButton = new Button
            {
                Text = "<",
                Location = new Point(210, 14),
                Size = new Size(28, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            sidebarToggleButton.Click += (s, e) => ToggleSidebar();
            StyleButton(sidebarToggleButton, ZqButtonRole.Ghost);
            sidePanel.Controls.Add(sidebarToggleButton);
            sidebarToggleButton.BringToFront();

            var newTaskButton = new Button
            {
                Text = Tr("+ New Task", "+ 新任务", "+ 新任務"),
                Location = new Point(14, 64),
                Size = new Size(220, 34),
                Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold)
            };
            newTaskButton.Click += (s, e) => CreateNewTask();
            StyleButton(newTaskButton, ZqButtonRole.Primary);
            sidePanel.Controls.Add(newTaskButton);

            taskSearchBox = new TextBox
            {
                Location = new Point(14, 108),
                Size = new Size(220, 28)
            };
            taskSearchBox.TextChanged += (s, e) => RefreshTaskList();
            StyleInput(taskSearchBox);
            sidePanel.Controls.Add(taskSearchBox);

            taskList = new ListBox
            {
                Location = new Point(14, 146),
                Size = new Size(220, 260),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BorderStyle = BorderStyle.None,
                BackColor = zqSurface,
                Font = new Font("Microsoft YaHei UI", 10)
            };
            taskList.SelectedIndexChanged += (s, e) =>
            {
                if (suppressTaskSelection) return;
                var item = taskList.SelectedItem as TaskInfo;
                if (item != null && item.Id != currentTaskId) LoadTask(item.Id);
            };
            StyleList(taskList);
            sidePanel.Controls.Add(taskList);

            var screenButton = new Button
            {
                Text = Tr("Screenshot OCR", "截图识别", "截圖識別"),
                Location = new Point(14, 410),
                Size = new Size(220, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            screenButton.Click += (s, e) => CaptureScreenForOcr();
            StyleButton(screenButton, ZqButtonRole.Secondary);
            sidePanel.Controls.Add(screenButton);

            clipboardButton = new Button
            {
                Text = Tr("Clipboard: Off", "剪贴板：关", "剪貼簿：關"),
                Location = new Point(14, 446),
                Size = new Size(220, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            clipboardButton.Click += (s, e) => ToggleClipboardMonitor();
            StyleButton(clipboardButton, ZqButtonRole.Secondary);
            sidePanel.Controls.Add(clipboardButton);

            var indexButton = new Button
            {
                Text = Tr("Index Folder", "索引文件夹", "索引資料夾"),
                Location = new Point(14, 482),
                Size = new Size(105, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            indexButton.Click += (s, e) => IndexFolder();
            StyleButton(indexButton, ZqButtonRole.Secondary);
            sidePanel.Controls.Add(indexButton);

            var searchButton = new Button
            {
                Text = Tr("Search KB", "搜索知识库", "搜尋知識庫"),
                Location = new Point(129, 482),
                Size = new Size(105, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            searchButton.Click += (s, e) => SearchKnowledge();
            StyleButton(searchButton, ZqButtonRole.Secondary);
            sidePanel.Controls.Add(searchButton);

            var batchButton = new Button
            {
                Text = Tr("Batch Report", "批量报告", "批次報告"),
                Location = new Point(14, 518),
                Size = new Size(105, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            batchButton.Click += async (s, e) => await RunBatchFiles();
            StyleButton(batchButton, ZqButtonRole.Secondary);
            sidePanel.Controls.Add(batchButton);

            var pluginButton = new Button
            {
                Text = Tr("Run Plugin", "运行插件", "執行外掛"),
                Location = new Point(129, 518),
                Size = new Size(105, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            pluginButton.Click += (s, e) => RunPlugin();
            StyleButton(pluginButton, ZqButtonRole.Secondary);
            sidePanel.Controls.Add(pluginButton);

            var sideSettingsButton = new Button
            {
                Text = Tr("Settings", "设置", "設定"),
                Location = new Point(14, 590),
                Size = new Size(105, 32),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            sideSettingsButton.Click += (s, e) => ShowSettings();
            StyleButton(sideSettingsButton, ZqButtonRole.Ghost);
            sidePanel.Controls.Add(sideSettingsButton);

            var aboutButton = new Button
            {
                Text = Tr("About", "关于", "關於"),
                Location = new Point(129, 590),
                Size = new Size(105, 32),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            aboutButton.Click += (s, e) => MessageBox.Show(this,
                Tr("ZhuaQian Desktop\n\nOpen-source Windows AI work assistant.\n\nGemini: https://aistudio.google.com/apikey\nOpenRouter: https://openrouter.ai/settings/keys",
                   "抓钱桌面端\n\n开源 Windows AI 办公助手。\n\nGemini: https://aistudio.google.com/apikey\nOpenRouter: https://openrouter.ai/settings/keys",
                   "抓錢桌面端\n\n開源 Windows AI 辦公助手。\n\nGemini: https://aistudio.google.com/apikey\nOpenRouter: https://openrouter.ai/settings/keys"),
                Tr("About ZhuaQian", "关于抓钱", "關於抓錢"));
            StyleButton(aboutButton, ZqButtonRole.Ghost);
            sidePanel.Controls.Add(aboutButton);

            sidePanel.Resize += (s, e) =>
            {
                if (!sidebarExpanded)
                {
                    sidebarToggleButton.SetBounds(8, 14, 28, 28);
                    return;
                }
                int panelW = Math.Max(180, sidePanel.ClientSize.Width);
                int fullW = Math.Max(150, panelW - 28);
                int halfW = Math.Max(72, (fullW - 12) / 2);
                sidebarToggleButton.SetBounds(panelW - 40, 14, 28, 28);
                newTaskButton.Width = fullW;
                taskSearchBox.Width = fullW;
                taskList.Width = fullW;
                screenButton.Width = fullW;
                clipboardButton.Width = fullW;
                indexButton.Width = halfW;
                searchButton.Left = 14 + halfW + 12;
                searchButton.Width = halfW;
                batchButton.Width = halfW;
                pluginButton.Left = 14 + halfW + 12;
                pluginButton.Width = halfW;
                sideSettingsButton.Width = halfW;
                aboutButton.Left = 14 + halfW + 12;
                aboutButton.Width = halfW;
                taskList.Height = Math.Max(130, sidePanel.ClientSize.Height - 410);
                int actionTop = sidePanel.ClientSize.Height - 214;
                screenButton.Top = actionTop;
                clipboardButton.Top = actionTop + 36;
                indexButton.Top = actionTop + 72;
                searchButton.Top = actionTop + 72;
                batchButton.Top = actionTop + 108;
                pluginButton.Top = actionTop + 108;
                sideSettingsButton.Top = sidePanel.ClientSize.Height - 46;
                aboutButton.Top = sidePanel.ClientSize.Height - 46;
            };

            var topPanel = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = zqPanelBg };
            rightPanel.Controls.Add(topPanel);

            var title = new Label
            {
                Text = Tr("Task", "任务", "任務"),
                Font = new Font("Microsoft YaHei UI", 14, FontStyle.Bold),
                Location = new Point(14, 10),
                AutoSize = true,
                ForeColor = zqInk
            };
            topPanel.Controls.Add(title);

            currentTaskLabel = new Label
            {
                Text = currentTaskTitle,
                Location = new Point(70, 15),
                AutoSize = false,
                AutoEllipsis = true,
                Size = new Size(200, 22),
                Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold),
                ForeColor = zqInk
            };
            topPanel.Controls.Add(currentTaskLabel);

            modelLabel = new Label
            {
                Text = CurrentModelLabel(),
                Location = new Point(290, 17),
                AutoSize = false,
                AutoEllipsis = true,
                Size = new Size(280, 22),
                ForeColor = zqMuted
            };
            topPanel.Controls.Add(modelLabel);

            modeCombo = new ComboBox { Width = 96, Height = 28, DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            PopulateModeCombo();
            modeCombo.BackColor = zqSurface;
            modeCombo.ForeColor = zqInk;
            modeCombo.SelectedIndexChanged += (s, e) =>
            {
                workMode = ModeValueFromLabel(Convert.ToString(modeCombo.SelectedItem));
                SaveConfig();
                RefreshAttachLabel();
            };
            topPanel.Controls.Add(modeCombo);

            powerButton = new Button { Text = PowerButtonText(), Width = 96, Height = 28, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            powerButton.Location = new Point(496, 12);
            powerButton.Click += (s, e) => ToggleComputerControlPower();
            ApplyPowerButtonStyle();
            topPanel.Controls.Add(powerButton);

            var commandButton = new Button { Text = "Cmd", Width = 58, Height = 28, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            commandButton.Location = new Point(540, 12);
            commandButton.Click += (s, e) => ShowCommandPalette();
            StyleButton(commandButton, ZqButtonRole.Secondary);
            topPanel.Controls.Add(commandButton);

            var outputsButton = new Button { Text = Tr("Outputs", "产物", "產物"), Width = 72, Height = 28, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            outputsButton.Location = new Point(530, 12);
            outputsButton.Click += (s, e) => ShowOutputsPanel();
            StyleButton(outputsButton, ZqButtonRole.Secondary);
            topPanel.Controls.Add(outputsButton);

            var toolsButton = new Button { Text = Tr("Tools", "工具", "工具"), Width = 72, Height = 28, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            toolsButton.Location = new Point(608, 12);
            toolsButton.Click += (s, e) => ShowTools();
            StyleButton(toolsButton, ZqButtonRole.Secondary);
            topPanel.Controls.Add(toolsButton);

            var shareButton = new Button { Text = Tr("Share", "分享", "分享"), Width = 72, Height = 28, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            shareButton.Location = new Point(608, 12);
            shareButton.Click += (s, e) => ShareCurrentTask();
            StyleButton(shareButton, ZqButtonRole.Ghost);
            topPanel.Controls.Add(shareButton);

            var settingsButton = new Button { Text = Tr("Settings", "设置", "設定"), Width = 92, Height = 28, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            settingsButton.Location = new Point(680, 12);
            settingsButton.Click += (s, e) => ShowSettings();
            StyleButton(settingsButton, ZqButtonRole.Ghost);
            topPanel.Controls.Add(settingsButton);

            var renameButton = new Button { Text = Tr("Rename", "重命名", "重新命名"), Width = 82, Height = 28, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            renameButton.Location = new Point(780, 12);
            renameButton.Click += (s, e) => RenameCurrentTask();
            StyleButton(renameButton, ZqButtonRole.Ghost);
            topPanel.Controls.Add(renameButton);

            var clearButton = new Button { Text = Tr("Clear", "清空", "清空"), Width = 72, Height = 28, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            clearButton.Location = new Point(870, 12);
            StyleButton(clearButton, ZqButtonRole.Ghost);
            clearButton.Click += (s, e) =>
            {
                messages.Clear();
                pendingParts.Clear();
                pendingLabels.Clear();
                chat.Clear();
                RefreshAttachLabel();
                SaveCurrentTask();
            };
            topPanel.Controls.Add(clearButton);

            layoutTop = () =>
            {
                clearButton.Left = topPanel.ClientSize.Width - clearButton.Width - 14;
                renameButton.Left = clearButton.Left - renameButton.Width - 8;
                settingsButton.Left = renameButton.Left - settingsButton.Width - 8;
                toolsButton.Left = settingsButton.Left - toolsButton.Width - 8;
                shareButton.Left = toolsButton.Left - shareButton.Width - 8;
                outputsButton.Left = shareButton.Left - outputsButton.Width - 8;
                commandButton.Left = outputsButton.Left - commandButton.Width - 8;
                powerButton.Left = commandButton.Left - powerButton.Width - 8;
                modeCombo.Left = powerButton.Left - modeCombo.Width - 8;
                modeCombo.Top = 12;
                int actionsLeft = modeCombo.Left;
                int taskX = 70;
                int available = actionsLeft - taskX - 10;
                currentTaskLabel.Visible = available > 40;
                modelLabel.Visible = available > 260;
                if (available > 40)
                {
                    int taskW = modelLabel.Visible ? Math.Min(360, Math.Max(120, (int)(available * 0.58))) : Math.Max(40, available);
                    currentTaskLabel.SetBounds(taskX, 15, taskW, 22);
                    int modelX = taskX + taskW + 10;
                    int modelW = actionsLeft - modelX - 8;
                    if (modelLabel.Visible && modelW > 80)
                        modelLabel.SetBounds(modelX, 17, modelW, 22);
                    else
                        modelLabel.Visible = false;
                }
            };
            topPanel.Resize += (s, e) => layoutTop();
            layoutTop();

            chat = new RichTextBox
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = zqSurface,
                Font = new Font("Microsoft YaHei UI", 10),
                ContextMenuStrip = BuildChatContextMenu()
            };
            rightPanel.Controls.Add(chat);

            bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 174, BackColor = zqPanelBg };
            rightPanel.Controls.Add(bottomPanel);
            bottomPanel.BringToFront();
            topPanel.BringToFront();

            Action layoutChatArea = () =>
            {
                if (chat == null || topPanel == null || bottomPanel == null) return;
                int top = topPanel.Bottom;
                int bottom = bottomPanel.Top;
                if (bottom <= top) bottom = rightPanel.ClientSize.Height - bottomPanel.Height;
                chat.SetBounds(0, top, Math.Max(0, rightPanel.ClientSize.Width), Math.Max(0, bottom - top));
            };
            rightPanel.Resize += (s, e) => layoutChatArea();
            topPanel.Resize += (s, e) => layoutChatArea();

            attachLabel = new Label { Left = 12, Top = 8, Height = 24, AutoSize = false, AutoEllipsis = true, ForeColor = zqMuted };
            bottomPanel.Controls.Add(attachLabel);

            uploadButton = new Button { Text = Tr("Upload", "上传", "上傳"), Width = 112, Height = 34, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            uploadButton.Click += (s, e) => UploadFiles();
            StyleButton(uploadButton, ZqButtonRole.Secondary);
            bottomPanel.Controls.Add(uploadButton);

            saveTxtButton = new Button { Text = Tr("Save File", "保存文件", "儲存檔案"), Width = 112, Height = 30, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            saveTxtButton.Click += (s, e) => SaveLastReplyAsFile(false, "");
            StyleButton(saveTxtButton, ZqButtonRole.Ghost);
            bottomPanel.Controls.Add(saveTxtButton);

            openFolderButton = new Button { Text = Tr("Open Folder", "打开文件夹", "開啟資料夾"), Width = 112, Height = 30, Anchor = AnchorStyles.Top | AnchorStyles.Right, Enabled = false };
            openFolderButton.Click += (s, e) => OpenLastGeneratedFolder();
            StyleButton(openFolderButton, ZqButtonRole.Ghost);
            bottomPanel.Controls.Add(openFolderButton);

            sendButton = new Button
            {
                Text = Tr("SEND", "发送", "傳送"),
                Width = 112,
                Height = 40,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold)
            };
            sendButton.Click += async (s, e) => await SendMessage();
            StyleButton(sendButton, ZqButtonRole.Primary);
            bottomPanel.Controls.Add(sendButton);

            input = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom
            };
            StyleInput(input);
            input.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    await SendMessage();
                }
            };
            bottomPanel.Controls.Add(input);

            bottomPanel.Resize += (s, e) => { LayoutBottom(); layoutChatArea(); };
            LayoutBottom();
            rightPanel.PerformLayout();
            layoutChatArea();

            clipboardTimer = new Timer { Interval = 1500 };
            clipboardTimer.Tick += async (s, e) => await ClipboardTimerTick();
        }

        void LayoutBottom()
        {
            int w = Math.Max(520, bottomPanel.ClientSize.Width);
            int rightX = w - 112 - 12;
            int inputW = Math.Max(320, rightX - 24);
            attachLabel.SetBounds(12, 8, w - 24, 24);
            input.SetBounds(12, 38, inputW, Math.Max(80, bottomPanel.ClientSize.Height - 50));
            uploadButton.SetBounds(rightX, 38, 112, 30);
            saveTxtButton.SetBounds(rightX, 72, 112, 30);
            openFolderButton.SetBounds(rightX, 106, 112, 30);
            sendButton.SetBounds(rightX, 140, 112, 30);
        }

        void ToggleSidebar()
        {
            sidebarExpanded = !sidebarExpanded;
            ApplySidebarState();
        }

        void ApplySidebarState()
        {
            if (mainSplit == null || sidePanel == null || sidebarToggleButton == null) return;
            foreach (Control c in sidePanel.Controls)
            {
                if (c != sidebarToggleButton) c.Visible = sidebarExpanded;
            }

            int targetWidth = sidebarExpanded ? 250 : 44;
            try
            {
                int panel2Min = Math.Min(420, Math.Max(260, mainSplit.Width - 330));
                if (mainSplit.Width > 0) mainSplit.Panel2MinSize = panel2Min;
                mainSplit.SplitterDistance = Math.Min(targetWidth, Math.Max(mainSplit.Panel1MinSize, mainSplit.Width - panel2Min - mainSplit.SplitterWidth));
            }
            catch (Exception _ex) { LogAction("Warning", "Layout: " + _ex.Message); }

            sidebarToggleButton.Text = sidebarExpanded ? "<" : ">";
            sidebarToggleButton.SetBounds(sidebarExpanded ? Math.Max(8, sidePanel.ClientSize.Width - 40) : 8, 14, 28, 28);
            sidebarToggleButton.Visible = true;
            sidebarToggleButton.BringToFront();
            sidePanel.PerformLayout();
            sidePanel.Invalidate();
        }

        void RebuildUiForLanguage()
        {
            bool wasExpanded = sidebarExpanded;
            if (clipboardTimer != null)
            {
                clipboardTimer.Stop();
                clipboardTimer.Dispose();
                clipboardTimer = null;
            }
            Controls.Clear();
            BuildUi();
            sidebarExpanded = wasExpanded;
            ApplySidebarState();
            RefreshTaskList();
            UpdateCurrentTaskHeader();
            RefreshAttachLabel();
            RenderMessages();
        }

        string PowerButtonText()
        {
            return computerControlEnabled ? Tr("Power: On", "Power：开", "Power：開") : Tr("Power: Off", "Power：关", "Power：關");
        }

        void ToggleComputerControlPower()
        {
            if (!computerControlEnabled)
            {
                string warning = Tr(
                    "Turn on computer-control permission?\n\nThis allows ZhuaQian tools to perform local actions such as moving files, creating drafts, running plugins, and ending a selected process. Risky actions will still ask for confirmation and write to the audit log.",
                    "要打开电脑操作权限吗？\n\n打开后，抓钱工具可以执行整理文件、生成草稿、运行插件、结束指定进程等本地动作。高风险动作仍会二次确认，并写入审计日志。",
                    "要開啟電腦操作權限嗎？\n\n開啟後，抓錢工具可以執行整理檔案、產生草稿、執行外掛、結束指定處理程序等本機動作。高風險動作仍會二次確認，並寫入稽核日誌。");
                if (MessageBox.Show(this, warning, "ZhuaQian Power", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                computerControlEnabled = true;
                LogAction("Power", "Computer control enabled");
                AppendChat("ZhuaQian", Tr("Computer-control permission is ON.",
                                           "电脑操作权限已开启。",
                                           "電腦操作權限已開啟。"), Color.FromArgb(0, 130, 80));
            }
            else
            {
                computerControlEnabled = false;
                LogAction("Power", "Computer control disabled");
                AppendChat("ZhuaQian", Tr("Computer-control permission is OFF.",
                                           "电脑操作权限已关闭。",
                                           "電腦操作權限已關閉。"), Color.FromArgb(0, 130, 80));
            }
            if (powerButton != null)
            {
                powerButton.Text = PowerButtonText();
                ApplyPowerButtonStyle();
            }
            SaveConfig();
        }

        bool EnsureComputerControlPower(string actionName)
        {
            if (computerControlEnabled) return true;
            string message = Tr(
                "Computer-control permission is OFF.\n\nTurn on Power first before running: ",
                "电脑操作权限当前为关闭。\n\n请先打开 Power 开关，再执行：",
                "電腦操作權限目前為關閉。\n\n請先開啟 Power 開關，再執行：") + actionName;
            MessageBox.Show(this, message, "ZhuaQian Power", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        bool EnsurePermission(string permissionName, bool enabled, bool requiresPower, string actionName)
        {
            if (!enabled)
            {
                SetCurrentTaskStatus("needs_input", "Permission needed: " + actionName, true);
                RecordAction(actionName, "blocked", "Permission disabled: " + permissionName, "");
                MessageBox.Show(this,
                    Tr("This permission is disabled. Open Settings -> Permissions to enable: ",
                       "这个权限当前关闭。请到 设置 -> 权限细分 开启：",
                       "這個權限目前關閉。請到 設定 -> 權限細分 開啟：") + permissionName,
                    "ZhuaQian Permissions", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            if (requiresPower && !EnsureComputerControlPower(actionName)) return false;
            return true;
        }

        string TaskFile(string id)
        {
            return Path.Combine(tasksDir, id + ".json");
        }

        string GetLastModelReply()
        {
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                var msg = messages[i] as Dictionary<string, object>;
                if (msg == null || !msg.ContainsKey("role")) continue;
                if (Convert.ToString(msg["role"]) != "model") continue;
                string text = PartsToText(msg.ContainsKey("parts") ? msg["parts"] : null);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            return "";
        }

        void LoadTasks()
        {
            Directory.CreateDirectory(tasksDir);
            tasks.Clear();

            foreach (var file in Directory.GetFiles(tasksDir, "*.json"))
            {
                try
                {
                    var data = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(file, Encoding.UTF8));
                    var info = new TaskInfo();
                    info.Id = data.ContainsKey("id") ? Convert.ToString(data["id"]) : Path.GetFileNameWithoutExtension(file);
                    info.Title = data.ContainsKey("title") ? Convert.ToString(data["title"]) : "Untitled task";
                    info.Status = NormalizeTaskStatus(data.ContainsKey("status") ? Convert.ToString(data["status"]) : "draft");
                    info.LastAction = data.ContainsKey("lastAction") ? Convert.ToString(data["lastAction"]) : "";
                    DateTime updated;
                    if (data.ContainsKey("updatedAt") && DateTime.TryParse(Convert.ToString(data["updatedAt"]), out updated))
                        info.UpdatedAt = updated;
                    else
                        info.UpdatedAt = File.GetLastWriteTime(file);
                    tasks.Add(info);
                }
                catch (Exception _ex) { LogAction("Warning", "LoadTask: " + _ex.Message); }
            }

            SortTasks();
            RefreshTaskList();

            if (tasks.Count == 0)
                CreateNewTask(false);
            else
                LoadTask(tasks[0].Id, false);
        }

        void RefreshTaskList()
        {
            if (taskList == null) return;
            string filter = taskSearchBox == null ? "" : taskSearchBox.Text.Trim();
            suppressTaskSelection = true;
            taskList.Items.Clear();
            string lastStatus = "";
            foreach (var task in tasks)
            {
                bool match = filter.Length == 0
                    || (task.Title ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                    || (task.LastAction ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                    || TaskStatusLabel(task.Status).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                if (match)
                {
                    string status = NormalizeTaskStatus(task.Status);
                    if (status != lastStatus)
                    {
                        taskList.Items.Add("-- " + TaskStatusLabel(status) + " --");
                        lastStatus = status;
                    }
                    taskList.Items.Add(task);
                }
            }
            for (int i = 0; i < taskList.Items.Count; i++)
            {
                var item = taskList.Items[i] as TaskInfo;
                if (item != null && item.Id == currentTaskId)
                {
                    taskList.SelectedIndex = i;
                    break;
                }
            }
            suppressTaskSelection = false;
        }

        void CreateNewTask()
        {
            CreateNewTask(true);
        }

        void CreateNewTask(bool saveBefore)
        {
            if (saveBefore) SaveCurrentTask();
            currentTaskId = Guid.NewGuid().ToString("N");
            currentTaskTitle = "New task";
            currentTaskStatus = "draft";
            currentTaskLastAction = "Created";
            messages.Clear();
            pendingParts.Clear();
            pendingLabels.Clear();
            chat.Clear();
            UpdateCurrentTaskHeader();
            AppendChat("ZhuaQian", Tr("New task created. Upload a file or ask a question. Press Enter to send, Shift+Enter for a new line.",
                                       "新任务已创建。可以上传文件或直接提问。按 Enter 发送，Shift+Enter 换行。",
                                       "新任務已建立。可以上傳檔案或直接提問。按 Enter 傳送，Shift+Enter 換行。"), Color.FromArgb(0, 130, 80));
            SaveCurrentTask();
            RefreshTaskList();
        }

        void LoadTask(string id)
        {
            LoadTask(id, true);
        }

        void LoadTask(string id, bool saveBefore)
        {
            if (saveBefore) SaveCurrentTask(false);
            string file = TaskFile(id);
            if (!File.Exists(file)) return;

            try
            {
                var data = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(file, Encoding.UTF8));
                currentTaskId = data.ContainsKey("id") ? Convert.ToString(data["id"]) : id;
                currentTaskTitle = data.ContainsKey("title") ? Convert.ToString(data["title"]) : "Untitled task";
                currentTaskStatus = NormalizeTaskStatus(data.ContainsKey("status") ? Convert.ToString(data["status"]) : "draft");
                currentTaskLastAction = data.ContainsKey("lastAction") ? Convert.ToString(data["lastAction"]) : "";
                messages.Clear();
                if (data.ContainsKey("messages"))
                {
                    var loaded = ToObjectList(data["messages"]);
                    if (loaded != null)
                    {
                        foreach (var msg in loaded) messages.Add(msg);
                    }
                }
                pendingParts.Clear();
                pendingLabels.Clear();
                UpdateCurrentTaskHeader();
                RenderMessages();
                RefreshAttachLabel();
                RefreshTaskList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Failed to load task");
            }
        }

        void SaveCurrentTask()
        {
            SaveCurrentTask(true);
        }

        void SaveCurrentTask(bool bumpUpdatedAt)
        {
            if (string.IsNullOrWhiteSpace(currentTaskId)) return;
            Directory.CreateDirectory(tasksDir);
            if (currentTaskTitle == "New task")
                currentTaskTitle = GenerateTaskTitle();
            var existing = tasks.Find(t => t.Id == currentTaskId);
            var now = DateTime.Now;
            DateTime updatedAt = now;
            if (!bumpUpdatedAt && existing != null && existing.UpdatedAt != DateTime.MinValue)
                updatedAt = existing.UpdatedAt;
            var data = new Dictionary<string, object>
            {
                { "id", currentTaskId },
                { "title", currentTaskTitle },
                { "status", currentTaskStatus },
                { "lastAction", currentTaskLastAction },
                { "createdAt", now.ToString("o") },
                { "updatedAt", updatedAt.ToString("o") },
                { "provider", provider },
                { "model", model },
                { "openRouterModel", openRouterModel },
                { "messages", messages }
            };
            File.WriteAllText(TaskFile(currentTaskId), json.Serialize(data), Encoding.UTF8);

            if (existing == null)
            {
                existing = new TaskInfo { Id = currentTaskId };
                tasks.Add(existing);
            }
            existing.Title = currentTaskTitle;
            existing.Status = currentTaskStatus;
            existing.LastAction = currentTaskLastAction;
            existing.UpdatedAt = updatedAt;
            if (bumpUpdatedAt) SortTasks();
            UpdateCurrentTaskHeader();
            RefreshTaskList();
        }

        void SortTasks()
        {
            tasks.Sort((a, b) =>
            {
                int rank = TaskStatusRank(a.Status).CompareTo(TaskStatusRank(b.Status));
                if (rank != 0) return rank;
                return b.UpdatedAt.CompareTo(a.UpdatedAt);
            });
        }

        static int TaskStatusRank(string status)
        {
            status = NormalizeTaskStatus(status);
            if (status == "needs_input") return 0;
            if (status == "running") return 1;
            if (status == "ready_for_review") return 2;
            if (status == "failed") return 3;
            if (status == "draft") return 4;
            if (status == "done") return 5;
            return 6;
        }

        static string NormalizeTaskStatus(string status)
        {
            status = (status ?? "").Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
            if (status == "pending" || status == "created") return "draft";
            if (status == "review" || status == "ready" || status == "readyforreview") return "ready_for_review";
            if (status == "complete" || status == "completed") return "done";
            if (status == "input" || status == "needsinput") return "needs_input";
            if (status == "running" || status == "failed" || status == "done" || status == "draft" || status == "needs_input" || status == "ready_for_review")
                return status;
            return "draft";
        }

        public static string TaskStatusLabel(string status)
        {
            status = NormalizeTaskStatus(status);
            if (status == "needs_input") return "Needs input";
            if (status == "running") return "Running";
            if (status == "ready_for_review") return "Ready";
            if (status == "failed") return "Failed";
            if (status == "done") return "Done";
            return "Draft";
        }

        void SetCurrentTaskStatus(string status, string action, bool save)
        {
            currentTaskStatus = NormalizeTaskStatus(status);
            if (action != null) currentTaskLastAction = action;
            UpdateCurrentTaskHeader();
            if (save) SaveCurrentTask();
        }

        string GenerateTaskTitle()
        {
            foreach (var msgObj in messages)
            {
                var msg = msgObj as Dictionary<string, object>;
                if (msg == null || !msg.ContainsKey("role")) continue;
                if (Convert.ToString(msg["role"]) != "user") continue;
                string text = PartsToText(msg.ContainsKey("parts") ? msg["parts"] : null);
                text = Regex.Replace(text ?? "", "\\s+", " ").Trim();
                if (text.Length == 0) continue;
                if (text.Length > 30) text = text.Substring(0, 30) + "...";
                return text;
            }
            return "New task";
        }

        void RenameCurrentTask()
        {
            string value = PromptText("Rename task", "Task title:", currentTaskTitle);
            if (value == null) return;
            value = value.Trim();
            if (value.Length == 0) return;
            currentTaskTitle = value;
            SaveCurrentTask();
        }

        string PromptText(string title, string label, string value)
        {
            using (var dlg = new Form())
            {
                dlg.Text = title;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Size = new Size(420, 150);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.Font = Font;
                dlg.BackColor = zqPanelBg;
                var lbl = new Label { Text = label, Left = 14, Top = 18, AutoSize = true, ForeColor = zqMuted };
                var box = new TextBox { Left = 100, Top = 14, Width = 280, Text = value };
                var ok = new Button { Text = Tr("OK", "确定", "確定"), Left = 220, Top = 60, Width = 75 };
                var cancel = new Button { Text = Tr("Cancel", "取消", "取消"), Left = 305, Top = 60, Width = 75 };
                StyleInput(box);
                StyleButton(ok, ZqButtonRole.Primary);
                StyleButton(cancel, ZqButtonRole.Ghost);
                ok.Click += (s, e) => { dlg.DialogResult = DialogResult.OK; dlg.Close(); };
                cancel.Click += (s, e) => dlg.Close();
                dlg.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
                return dlg.ShowDialog(this) == DialogResult.OK ? box.Text : null;
            }
        }

        void UpdateCurrentTaskHeader()
        {
            if (currentTaskLabel != null)
            {
                string suffix = string.IsNullOrWhiteSpace(currentTaskLastAction) ? "" : " | " + currentTaskLastAction;
                currentTaskLabel.Text = currentTaskTitle + "  [" + TaskStatusLabel(currentTaskStatus) + "]" + suffix;
            }
            if (modelLabel != null) modelLabel.Text = CurrentModelLabel();
            if (layoutTop != null) layoutTop();
        }

        void RenderMessages()
        {
            chat.Clear();
            if (messages.Count == 0)
            {
                AppendChat("ZhuaQian", Tr("This task is empty. Upload a file or ask a question.",
                                           "这个任务还是空的。可以上传文件或直接提问。",
                                           "這個任務還是空的。可以上傳檔案或直接提問。"), Color.FromArgb(0, 130, 80));
                return;
            }

            foreach (var msgObj in messages)
            {
                var msg = msgObj as Dictionary<string, object>;
                if (msg == null || !msg.ContainsKey("role")) continue;
                string role = Convert.ToString(msg["role"]);
                string text = PartsToText(msg.ContainsKey("parts") ? msg["parts"] : null);
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (role == "user") AppendChat("You", text, Color.FromArgb(30, 90, 180));
                else AppendChat("ZhuaQian", text, Color.FromArgb(0, 130, 80));
            }
        }


        void UploadFiles()
        {
            if (!EnsurePermission(Tr("Read local files", "读取本地文件", "讀取本機檔案"), permFileRead, false, "Upload Files")) return;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Choose files";
                ofd.Multiselect = true;
                ofd.Filter = "Supported files|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.pdf;*.docx;*.xlsx;*.xlsm;*.pptx;*.txt;*.md;*.csv;*.json|All files|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                foreach (var file in ofd.FileNames)
                {
                    try { AddAttachment(file); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Load failed"); }
                }
                RefreshAttachLabel();
            }
        }

        void ShowTools()
        {
            using (var dlg = new Form())
            {
                dlg.Text = Tr("Workflow Tools", "工作流工具", "工作流工具");
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Size = new Size(560, 640);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.Font = Font;
                dlg.BackColor = zqPanelBg;

                var intro = new Label
                {
                    Text = Tr("Command Center: create artifacts, inspect state, and run guarded local actions.",
                              "命令中心：生成产物、查看状态，并执行受保护的本地动作。",
                              "命令中心：產生产物、查看狀態，並執行受保護的本機動作。"),
                    Location = new Point(16, 16),
                    Size = new Size(510, 42),
                    ForeColor = zqMuted
                };
                dlg.Controls.Add(intro);

                AddToolButton(dlg, Tr("Organize Folder", "整理文件夹", "整理資料夾"), 16, 70, (s, e) => OrganizeFolder());
                AddToolButton(dlg, Tr("Template / Email Draft", "模板/邮件草稿", "範本/郵件草稿"), 260, 70, (s, e) => CreateTemplateOrEmailDraft());
                AddToolButton(dlg, Tr("Excel Assistant", "Excel 助手", "Excel 助手"), 16, 118, (s, e) => PrepareExcelAssistant());
                AddToolButton(dlg, Tr("Resource Monitor", "资源监控", "資源監控"), 260, 118, (s, e) => ShowResourceMonitor());
                AddToolButton(dlg, Tr("Semantic File Search", "语义文件搜索", "語意檔案搜尋"), 16, 166, (s, e) => SearchKnowledge());
                AddToolButton(dlg, Tr("Audit Log", "审计日志", "稽核日誌"), 260, 166, (s, e) => ShowAuditLog());
                AddToolButton(dlg, Tr("Privacy Test", "隐私脱敏测试", "隱私遮蔽測試"), 16, 214, (s, e) => PreviewRedaction());
                AddToolButton(dlg, Tr("Voice / Mobile Plan", "语音/手机计划", "語音/手機計畫"), 260, 214, (s, e) => ShowFutureIntegrationPlan());
                AddToolButton(dlg, Tr("Use Current Context", "使用当前窗口上下文", "使用目前視窗上下文"), 16, 262, (s, e) => UseActiveWindowContext());
                AddToolButton(dlg, Tr("Agent Planner", "Agent 任务规划", "Agent 任務規劃"), 260, 262, (s, e) => ShowPlanReview());
                AddToolButton(dlg, Tr("Skill Library", "职业技能库", "職業技能庫"), 16, 310, (s, e) => ShowSkillLibrary());
                AddToolButton(dlg, Tr("Export Chat", "导出聊天", "匯出聊天"), 260, 310, (s, e) => ExportCurrentChat());
                AddToolButton(dlg, Tr("Rollback Files", "回滚整理", "回復整理"), 16, 358, (s, e) => ShowRollbackPanel());
                AddToolButton(dlg, Tr("Outputs", "产物", "產物"), 260, 358, (s, e) => ShowOutputsPanel());
                AddToolButton(dlg, Tr("Computer Control", "操控电脑", "操控電腦"), 16, 406, (s, e) => ShowComputerControlPrompt());
                AddToolButton(dlg, Tr("Activity Monitor", "\u8fd0\u884c\u89c2\u5bdf\u53f0", "\u904b\u884c\u89c0\u5bdf\u53f0"), 260, 406, (s, e) => ShowMonitoringPanel());
                AddToolButton(dlg, Tr("Prompt Workbench", "Prompt 工作台", "Prompt 工作台"), 16, 454, (s, e) => ShowPromptWorkbench());
                var close = new Button { Text = Tr("Close", "关闭", "關閉"), Location = new Point(430, 542), Size = new Size(95, 32) };
                close.Click += (s, e) => dlg.Close();
                StyleButton(close, ZqButtonRole.Primary);
                dlg.Controls.Add(close);
                dlg.ShowDialog(this);
            }
        }
        void AddToolButton(Form dlg, string text, int x, int y, EventHandler handler)
        {
            var btn = new Button { Text = text, Location = new Point(x, y), Size = new Size(225, 34) };
            btn.Click += handler;
            StyleButton(btn, ZqButtonRole.Secondary);
            dlg.Controls.Add(btn);
        }

        void ShowCommandPalette()
        {
            using (var dlg = new Form())
            using (var search = new TextBox())
            using (var list = new ListBox())
            {
                dlg.Text = Tr("Command Palette", "命令面板", "命令面板");
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Size = new Size(520, 420);
                dlg.Font = Font;
                dlg.BackColor = zqPanelBg;
                search.SetBounds(14, 14, 475, 28);
                list.SetBounds(14, 52, 475, 300);
                StyleInput(search);
                StyleList(list);
                var commands = BuildCommands();
                Action refresh = () =>
                {
                    string q = (search.Text ?? "").Trim().ToLowerInvariant();
                    list.Items.Clear();
                    foreach (var item in commands)
                    {
                        if (q.Length == 0 || item.Key.ToLowerInvariant().Contains(q)) list.Items.Add(item.Key);
                    }
                    if (list.Items.Count > 0) list.SelectedIndex = 0;
                };
                Action run = () =>
                {
                    if (list.SelectedItem == null) return;
                    string name = Convert.ToString(list.SelectedItem);
                    dlg.Close();
                    foreach (var item in commands)
                    {
                        if (item.Key == name)
                        {
                            item.Value();
                            break;
                        }
                    }
                };
                search.TextChanged += (s, e) => refresh();
                search.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; run(); }
                    if (e.KeyCode == Keys.Down && list.Items.Count > 0) { list.Focus(); if (list.SelectedIndex < 0) list.SelectedIndex = 0; }
                };
                list.DoubleClick += (s, e) => run();
                list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; run(); } };
                dlg.Controls.Add(search);
                dlg.Controls.Add(list);
                refresh();
                dlg.Shown += (s, e) => search.Focus();
                dlg.ShowDialog(this);
            }
        }

        List<KeyValuePair<string, Action>> BuildCommands()
        {
            return new List<KeyValuePair<string, Action>> {
                new KeyValuePair<string, Action>(Tr("Ask mode", "问答模式", "問答模式"), () => SetMode("Ask")),
                new KeyValuePair<string, Action>(Tr("Draft mode", "起草模式", "起草模式"), () => SetMode("Draft")),
                new KeyValuePair<string, Action>(Tr("Plan mode", "计划模式", "計畫模式"), () => SetMode("Plan")),
                new KeyValuePair<string, Action>(Tr("Execute mode", "执行模式", "執行模式"), () => SetMode("Execute")),
                new KeyValuePair<string, Action>(Tr("Upload files", "上传文件", "上傳檔案"), () => UploadFiles()),
                new KeyValuePair<string, Action>(Tr("Screenshot OCR", "截图识别", "截圖識別"), () => CaptureScreenForOcr()),
                new KeyValuePair<string, Action>(Tr("Index folder", "索引文件夹", "索引資料夾"), () => IndexFolder()),
                new KeyValuePair<string, Action>(Tr("Search knowledge base", "搜索知识库", "搜尋知識庫"), () => SearchKnowledge()),
                new KeyValuePair<string, Action>(Tr("Export chat", "导出聊天", "匯出聊天"), () => ExportCurrentChat()),
                new KeyValuePair<string, Action>(Tr("Show outputs", "查看产物", "查看產物"), () => ShowOutputsPanel()),
                new KeyValuePair<string, Action>(Tr("Rollback organized files", "回滚整理文件", "回復整理檔案"), () => ShowRollbackPanel()),
                new KeyValuePair<string, Action>(Tr("Open audit log", "打开审计日志", "開啟稽核日誌"), () => ShowAuditLog()),
                new KeyValuePair<string, Action>(Tr("Test privacy redaction", "测试隐私脱敏", "測試隱私遮蔽"), () => PreviewRedaction()),
                new KeyValuePair<string, Action>(Tr("Use current window context", "使用当前窗口上下文", "使用目前視窗上下文"), () => UseActiveWindowContext()),
                new KeyValuePair<string, Action>(Tr("Agent planner", "Agent 任务规划", "Agent 任務規劃"), () => PrepareAgentPlan()),
                new KeyValuePair<string, Action>(Tr("Computer control", "操控电脑", "操控電腦"), () => ShowComputerControlPrompt()),
                new KeyValuePair<string, Action>(Tr("Activity monitor", "\u8fd0\u884c\u89c2\u5bdf\u53f0", "\u904b\u884c\u89c0\u5bdf\u53f0"), () => ShowMonitoringPanel()),
                new KeyValuePair<string, Action>(Tr("Settings", "设置", "設定"), () => ShowSettings()),
                new KeyValuePair<string, Action>(Tr("Permissions", "权限细分", "權限細分"), () => ShowPermissionSettings(this)),
                new KeyValuePair<string, Action>(Tr("Tools", "工具", "工具"), () => ShowTools()),
                new KeyValuePair<string, Action>(Tr("Share project (package)", "分享项目（打包）", "分享專案（打包）"), () => ShareCurrentTask()),
                new KeyValuePair<string, Action>(Tr("Import package", "导入分享包", "匯入分享包"), () => ImportPackage()),
                new KeyValuePair<string, Action>(Tr("Share over LAN", "局域网分享", "區域網分享"), () => ShareOverLan()),
                new KeyValuePair<string, Action>(Tr("Share via Relay", "通过中继分享", "透過中繼分享"), () => ShareViaRelay()),
                new KeyValuePair<string, Action>(Tr("Import from URL", "从网址导入", "從網址匯入"), () => ImportFromUrl())
            };
        }

        void SetMode(string mode)
        {
            workMode = NormalizeMode(mode);
            if (modeCombo != null)
            {
                SelectModeComboItem(workMode);
            }
            SaveConfig();
            RefreshAttachLabel();
            AppendChat("ZhuaQian", Tr("Mode: ", "模式：", "模式：") + ModeDisplayName(workMode), Color.FromArgb(0, 130, 80));
        }

        void PopulateModeCombo()
        {
            if (modeCombo == null) return;
            modeCombo.Items.Clear();
            modeCombo.Items.Add(ModeDisplayName("Ask"));
            modeCombo.Items.Add(ModeDisplayName("Draft"));
            modeCombo.Items.Add(ModeDisplayName("Plan"));
            modeCombo.Items.Add(ModeDisplayName("Execute"));
            SelectModeComboItem(workMode);
        }

        void SelectModeComboItem(string mode)
        {
            if (modeCombo == null) return;
            string label = ModeDisplayName(mode);
            modeCombo.SelectedItem = label;
            if (modeCombo.SelectedIndex < 0) modeCombo.SelectedIndex = 0;
        }

        string NormalizeMode(string mode)
        {
            mode = (mode ?? "").Trim();
            if (string.Equals(mode, "Draft", StringComparison.OrdinalIgnoreCase) || mode == "草稿") return "Draft";
            if (string.Equals(mode, "Plan", StringComparison.OrdinalIgnoreCase) || mode == "计划" || mode == "計畫") return "Plan";
            if (string.Equals(mode, "Execute", StringComparison.OrdinalIgnoreCase) || mode == "执行" || mode == "執行") return "Execute";
            return "Ask";
        }

        string ModeValueFromLabel(string label)
        {
            return NormalizeMode(label);
        }

        string ModeDisplayName(string mode)
        {
            mode = NormalizeMode(mode);
            if (mode == "Draft") return Tr("Draft", "草稿", "草稿");
            if (mode == "Plan") return Tr("Plan", "计划", "計畫");
            if (mode == "Execute") return Tr("Execute", "执行", "執行");
            return Tr("Ask", "问答", "問答");
        }

        void ShowComputerControlPrompt()
        {
            string command = PromptText(
                Tr("Computer Control", "操控电脑", "操控電腦"),
                Tr("Command:", "命令：", "命令："),
                "/open notepad");
            if (command == null) return;
            if (!TryRunLocalComputerCommand(command))
            {
                MessageBox.Show(this,
                    Tr("Use /open, /type, /hotkey, /key, /click, or /wait.",
                       "请使用 /open、/type、/hotkey、/key、/click 或 /wait。",
                       "請使用 /open、/type、/hotkey、/key、/click 或 /wait。"),
                    Tr("Computer Control", "操控电脑", "操控電腦"));
            }
        }


        void ShowOutputsPanel()
        {
            using (var dlg = new Form())
            using (var list = new ListBox())
            using (var open = new Button())
            using (var folder = new Button())
            using (var refresh = new Button())
            using (var renameBtn = new Button())
            using (var deleteBtn = new Button())
            using (var addKbBtn = new Button())
            {
                dlg.Text = Tr("Outputs", "产物", "產物");
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Size = new Size(820, 500);
                dlg.Font = Font;
                list.SetBounds(14, 14, 775, 360);
                list.DisplayMember = "Display";
                open.Text = Tr("Open", "打开", "開啟");
                open.SetBounds(14, 390, 90, 30);
                folder.Text = Tr("Folder", "文件夹", "資料夾");
                folder.SetBounds(112, 390, 90, 30);
                addKbBtn.Text = Tr("Add to KB", "加入知识库", "加入知識庫");
                addKbBtn.SetBounds(210, 390, 100, 30);
                renameBtn.Text = Tr("Rename", "重命名", "重新命名");
                renameBtn.SetBounds(318, 390, 90, 30);
                deleteBtn.Text = Tr("Delete record", "删除记录", "刪除記錄");
                deleteBtn.SetBounds(416, 390, 100, 30);
                refresh.Text = Tr("Refresh", "刷新", "重新整理");
                refresh.SetBounds(524, 390, 90, 30);
                var closeBtn = new Button { Text = Tr("Close", "关闭", "關閉"), Bounds = new Rectangle(622, 390, 90, 30) };
                closeBtn.Click += (s, e) => dlg.Close();

                var outputRecords = new List<Dictionary<string, object>>();
                Action load = () =>
                {
                    outputRecords.Clear();
                    list.Items.Clear();
                    // Load the full set so every output (beyond the first 100) is visible
                    // and manageable in the panel. Delete/Rename operate by outputId against
                    // the full backing store, so lifting this cap is safe and non-destructive.
                    foreach (var row in LoadOutputRows(int.MaxValue))
                    {
                        outputRecords.Add(row);
                        string path = Convert.ToString(row["path"]);
                        string type = Convert.ToString(row["type"]);
                        string title = row.ContainsKey("displayName") && !string.IsNullOrWhiteSpace(Convert.ToString(row["displayName"])) ? Convert.ToString(row["displayName"]) : Convert.ToString(row["taskTitle"]);
                        string at = Convert.ToString(row["createdAt"]);
                        bool exists = row.ContainsKey("exists") && Convert.ToBoolean(row["exists"]);
                        if (!exists && File.Exists(path)) exists = true;
                        long size = row.ContainsKey("sizeBytes") ? Convert.ToInt64(row["sizeBytes"]) : 0;
                        string sizeStr = size > 0 ? FormatSize(size) : "";
                        string status = exists ? "" : " [MISSING]";
                        string display = Path.GetFileName(path) + "  [" + type + "]  " + sizeStr + status + "  " + title + "  " + at;
                        list.Items.Add(new { Display = display, Path = path });
                    }
                    if (list.Items.Count == 0) list.Items.Add(new { Display = Tr("No exported outputs yet.", "还没有导出的产物。", "還沒有匯出的產物。"), Path = "" });
                };
                Action<string> openPath = (mode) =>
                {
                    int i = list.SelectedIndex;
                    if (i < 0 || i >= outputRecords.Count) return;
                    string path = Convert.ToString(outputRecords[i]["path"]);
                    if (string.IsNullOrWhiteSpace(path)) return;
                    if (!File.Exists(path))
                    {
                        MessageBox.Show(this, "File not found:\r\n" + path, "Outputs");
                        return;
                    }
                    if (mode == "folder")
                        Process.Start("explorer.exe", "/select," + QuoteArg(path));
                    else
                        Process.Start(path);
                };
                open.Click += (s, e) => openPath("open");
                folder.Click += (s, e) => openPath("folder");
                refresh.Click += (s, e) => load();
                list.DoubleClick += (s, e) => openPath("open");

                deleteBtn.Click += (s, e) =>
                {
                    int i = list.SelectedIndex;
                    if (i < 0 || i >= outputRecords.Count) return;
                    string path = Convert.ToString(outputRecords[i]["path"]);
                    if (MessageBox.Show(this, Tr("Delete this output record?\r\n", "删除这条产物记录？\r\n", "刪除這條產物記錄？\r\n") + path, "Delete record", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        string source = outputRecords[i].ContainsKey("recordSource") ? Convert.ToString(outputRecords[i]["recordSource"]) : "primary";
                        string outputId = outputRecords[i].ContainsKey("outputId") ? Convert.ToString(outputRecords[i]["outputId"]) : "";
                        if (string.Equals(source, "legacy-export-history", StringComparison.OrdinalIgnoreCase)) RemoveLegacyExportEntry(path);
                        else outputsHub.Delete(outputId);
                        load();
                    }
                };

                renameBtn.Click += (s, e) =>
                {
                    int i = list.SelectedIndex;
                    if (i < 0 || i >= outputRecords.Count) return;
                    string oldPath = Convert.ToString(outputRecords[i]["path"]);
                    string oldName = outputRecords[i].ContainsKey("displayName") && !string.IsNullOrWhiteSpace(Convert.ToString(outputRecords[i]["displayName"])) ? Convert.ToString(outputRecords[i]["displayName"]) : Path.GetFileName(oldPath);
                    string newName = PromptText(Tr("Rename record", "重命名记录", "重新命名記錄"), Tr("New display name:", "新名称：", "新名稱："), oldName);
                    if (newName == null || newName.Trim().Length == 0) return;
                    if (newName != oldName)
                    {
                        string source = outputRecords[i].ContainsKey("recordSource") ? Convert.ToString(outputRecords[i]["recordSource"]) : "primary";
                        string outputId = outputRecords[i].ContainsKey("outputId") ? Convert.ToString(outputRecords[i]["outputId"]) : "";
                        if (string.Equals(source, "legacy-export-history", StringComparison.OrdinalIgnoreCase)) {
                            RecordOutput("legacy-rename", Convert.ToString(outputRecords[i]["type"]), oldPath, currentTaskId, newName.Trim(), outputId, 0);
                            RemoveLegacyExportEntry(oldPath);
                        } else outputsHub.Rename(outputId, newName.Trim());
                        load();
                    }
                };

                addKbBtn.Click += (s, e) =>
                {
                    int i = list.SelectedIndex;
                    if (i < 0 || i >= outputRecords.Count) return;
                    string path = Convert.ToString(outputRecords[i]["path"]);
                    if (!File.Exists(path))
                    {
                        MessageBox.Show(this, "File not found. Cannot add to knowledge base.", "Add to KB");
                        return;
                    }
                    try
                    {
                        var info = new FileInfo(path);
                        string text = ApplyRedaction(TrimForPrompt(ExtractTextDocument(path), 64000));
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            MessageBox.Show(this, "No extractable text found.", "Add to KB");
                            return;
                        }
                        AddKnowledgeChunks(info, text);
                        SaveKnowledgeIndex();
                        LogAction("AddOutputToKB", "Added output to KB: " + path);
                        AppendChat("ZhuaQian", Tr("Output added to knowledge base.", "产物已加入知识库。", "產物已加入知識庫。"), Color.FromArgb(0, 130, 80));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, "Add to KB failed");
                    }
                };

                dlg.Controls.AddRange(new Control[] { list, open, folder, addKbBtn, renameBtn, deleteBtn, refresh, closeBtn });
                load();
                dlg.ShowDialog(this);
            }
        }

        List<Dictionary<string, object>> LoadOutputRows(int max)
        {
            return outputsHub.LoadOutputRows(max);
        }

        void ShowRollbackPanel()
        {
            if (!EnsurePermission(Tr("Move/delete files", "移动/删除文件", "移動/刪除檔案"), permFileMoveDelete, true, "Rollback Files")) return;
            Directory.CreateDirectory(rollbackDir);
            string[] files = Directory.GetFiles(rollbackDir, "organize-*.json");
            Array.Sort(files);
            Array.Reverse(files);
            if (files.Length == 0)
            {
                MessageBox.Show(this, "No rollback manifests found.", "Rollback Files");
                return;
            }

            using (var dlg = new Form())
            using (var list = new ListBox())
            using (var preview = new TextBox())
            using (var run = new Button())
            using (var close = new Button())
            {
                dlg.Text = Tr("Rollback Files", "回滚整理", "回復整理");
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Size = new Size(760, 520);
                dlg.Font = Font;
                list.SetBounds(14, 14, 250, 410);
                preview.SetBounds(276, 14, 450, 410);
                preview.Multiline = true;
                preview.ReadOnly = true;
                preview.ScrollBars = ScrollBars.Both;
                run.Text = Tr("Rollback", "执行回滚", "執行回復");
                run.SetBounds(540, 440, 90, 30);
                close.Text = Tr("Close", "关闭", "關閉");
                close.SetBounds(640, 440, 86, 30);
                foreach (var file in files) list.Items.Add(Path.GetFileName(file));
                Action load = () =>
                {
                    if (list.SelectedIndex < 0 || list.SelectedIndex >= files.Length) return;
                    preview.Text = BuildRollbackPreview(files[list.SelectedIndex]);
                };
                list.SelectedIndexChanged += (s, e) => load();
                run.Click += (s, e) =>
                {
                    if (list.SelectedIndex < 0 || list.SelectedIndex >= files.Length) return;
                    ExecuteRollbackManifest(files[list.SelectedIndex]);
                    load();
                };
                close.Click += (s, e) => dlg.Close();
                dlg.Controls.AddRange(new Control[] { list, preview, run, close });
                list.SelectedIndex = 0;
                dlg.ShowDialog(this);
            }
        }

        string BuildRollbackPreview(string manifestPath)
        {
            try
            {
                var manifest = json.DeserializeObject(File.ReadAllText(manifestPath, Encoding.UTF8)) as Dictionary<string, object>;
                var moved = manifest != null && manifest.ContainsKey("moved") ? ToObjectList(manifest["moved"]) : null;
                var sb = new StringBuilder();
                sb.AppendLine("Manifest: " + manifestPath);
                if (manifest != null && manifest.ContainsKey("createdAt")) sb.AppendLine("createdAt: " + manifest["createdAt"]);
                if (manifest != null && manifest.ContainsKey("root")) sb.AppendLine("root: " + manifest["root"]);
                sb.AppendLine();
                sb.AppendLine("Rollback will move files back:");
                if (moved != null)
                {
                    int count = 0;
                    foreach (var item in moved)
                    {
                        var row = item as Dictionary<string, object>;
                        if (row == null) continue;
                        count++;
                        string from = row.ContainsKey("from") ? Convert.ToString(row["from"]) : "";
                        string to = row.ContainsKey("to") ? Convert.ToString(row["to"]) : "";
                        sb.AppendLine(count + ". " + to + " -> " + from);
                        if (count >= 80) { sb.AppendLine("..."); break; }
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "Failed to read rollback manifest:\r\n" + ex.Message;
            }
        }

        void ExecuteRollbackManifest(string manifestPath)
        {
            string preview = BuildRollbackPreview(manifestPath);
            var rollbackGate = PermissionGate.FromJson(permGate.ToJson());
            rollbackGate.Set("permFileMoveDelete", PermissionLevel.Ask);
            var pipeline = agentPipelineFactory.Create(rollbackGate, pluginDir, allowAdvancedPlugins);
            pipeline.RequestApproval = command => ShowApprovalCard("RollbackFiles",
                Tr("Confirm Rollback", "确认回滚", "確認回滾"),
                Tr("Execute", "执行", "執行"),
                Tr("Move/delete files", "移动/删除文件", "移動/刪除檔案"),
                manifestPath,
                Tr("Files will be moved back to their original locations.", "文件将被移回原始位置。", "檔案將被移回原始位置。"),
                "Restored files plus audit record.",
                preview,
                manifestPath);
            try
            {
                var result = pipeline.Run(new AgentCommand("RollbackFiles", "permFileMoveDelete", currentTaskId, manifestPath, "Rollback organized files", new Dictionary<string, object>()));
                if (result.Status == CommandStatus.Cancelled)
                {
                    RecordAction("RollbackFiles", "cancelled", "Rollback cancelled", manifestPath);
                    return;
                }
                if (result.Status == CommandStatus.Denied)
                    throw new Exception(result.ErrorMessage ?? "Rollback denied.");
                if (result.Status != CommandStatus.Success)
                    throw new Exception(result.ErrorMessage ?? "Rollback failed.");

                LogAction("RollbackFiles", "Rollback completed through AgentPipeline: " + manifestPath);
                SetCurrentTaskStatus("ready_for_review", "Rollback complete", true);
                RecordAction("RollbackFiles", "success", result.OutputText ?? "Rollback complete", manifestPath);
                AppendChat("ZhuaQian", "Rollback complete.\r\n" + (result.OutputText ?? "") + "\r\nManifest: " + manifestPath, Color.FromArgb(0, 130, 80));
            }
            catch (Exception ex)
            {
                SetCurrentTaskStatus("failed", "Rollback failed", true);
                RecordAction("RollbackFiles", "failed", ex.Message, manifestPath);
                MessageBox.Show(this, ex.Message, "Rollback failed");
            }
        }

        List<Dictionary<string, object>> LoadExportHistoryRows(int max)
        {
            return outputsHub.LoadExportHistoryRows(max);
        }

        void RemoveLegacyExportEntry(string path)
        {
            outputsHub.RemoveLegacyExportEntry(path);
        }

        string BuildModeInstruction()
        {
            string mode = string.IsNullOrWhiteSpace(workMode) ? "Ask" : workMode;
            if (string.Equals(mode, "Ask", StringComparison.OrdinalIgnoreCase))
                return "";
            if (string.Equals(mode, "Draft", StringComparison.OrdinalIgnoreCase))
                return "[ZhuaQian Mode: Draft]\nGenerate polished, file-ready content. If the user asks for Word, PPT, Excel, Markdown, or TXT, structure the answer so the desktop app can save it as a real local file.";
            if (string.Equals(mode, "Plan", StringComparison.OrdinalIgnoreCase))
                return "[ZhuaQian Mode: Plan]\nDo not claim execution. First produce a clear step-by-step plan, list required permissions, affected files, risks, confirmations, and rollback ideas. Wait for user approval before any local action.";
            if (string.Equals(mode, "Execute", StringComparison.OrdinalIgnoreCase))
                return "[ZhuaQian Mode: Execute]\nHelp execute the user's approved task, but never claim local side effects unless the desktop app reports a real result. For risky actions, ask for confirmation and rely on desktop tools/permissions.";
            return "";
        }

        bool ShouldUseCurrentInfo(string text)
        {
            if (!currentInfoMode || string.IsNullOrWhiteSpace(text)) return false;
            string lower = text.ToLowerInvariant();
            return ContainsAny(lower,
                "latest", "current", "today", "now", "this year", "2026", "2027", "news", "price", "pricing", "release", "version", "api", "model", "deadline",
                "\u6700\u65b0", "\u5f53\u524d", "\u7576\u524d", "\u73b0\u5728", "\u73fe\u5728", "\u4eca\u5929", "\u4eca\u65e5", "\u4eca\u5e74",
                "\u65b0\u95fb", "\u65b0\u805e", "\u4ef7\u683c", "\u5b9a\u4ef7", "\u653f\u7b56", "\u6cd5\u89c4", "\u6cd5\u898f", "\u7248\u672c",
                "\u53d1\u5e03", "\u767c\u5e03", "\u4e0a\u7ebf", "\u4e0a\u7dda", "\u514d\u8d39", "\u4ed8\u8d39", "\u6a21\u578b");
        }

        List<string> ExtractWebUrls(string text)
        {
            var urls = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return urls;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(text, @"https?://[^\s""'<>]+", RegexOptions.IgnoreCase))
            {
                string url = (m.Value ?? "").Trim();
                url = url.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '\u3002', '\uff0c', '\uff1b', '\uff1a', '\uff01', '\uff1f');
                if (url.Length == 0 || seen.Contains(url)) continue;
                seen.Add(url);
                urls.Add(url);
                if (urls.Count >= 3) break;
            }
            return urls;
        }

        string BuildCurrentInfoInstruction()
        {
            return "[ZhuaQian Current Info Mode]\nThis request may depend on recent or changing facts. Use live search grounding if the provider supports it. Prefer current, source-backed facts. If live search is unavailable, clearly say the answer may be outdated.";
        }

        string BuildWebSearchContext(string query)
        {
            var results = webSearchClient.Search(query, 5);
            if (results == null || results.Count == 0)
                return "[ZhuaQian Web Search]\nNo live web search results were retrieved. Do not pretend to have current facts; say that current search failed and the answer may be outdated.";

            var sb = new StringBuilder();
            sb.AppendLine("[ZhuaQian Web Search]");
            sb.AppendLine("Use these current search results as source context. Cite the URLs when using facts from them.");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                sb.AppendLine();
                sb.AppendLine((i + 1).ToString() + ". " + r.Title);
                sb.AppendLine("URL: " + r.Url);
                if (!string.IsNullOrWhiteSpace(r.Snippet)) sb.AppendLine("Snippet: " + r.Snippet);
            }
            return sb.ToString().Trim();
        }

        string BuildWebPageContext(List<string> urls)
        {
            if (urls == null || urls.Count == 0) return "";
            var sb = new StringBuilder();
            sb.AppendLine("[ZhuaQian URL Page Context]");
            sb.AppendLine("The user provided URL(s). Analyze only the fetched page text below. Cite the URL. If a page fetch failed, say it failed and do not invent page contents.");
            for (int i = 0; i < urls.Count; i++)
            {
                var page = webSearchClient.FetchPage(urls[i], 50000);
                sb.AppendLine();
                sb.AppendLine((i + 1).ToString() + ". URL: " + urls[i]);
                if (page != null && page.Success)
                {
                    if (!string.IsNullOrWhiteSpace(page.Title)) sb.AppendLine("Title: " + page.Title);
                    sb.AppendLine("Fetched: yes");
                    sb.AppendLine("Page text:");
                    sb.AppendLine(ApplyRedaction(page.Text ?? ""));
                }
                else
                {
                    string error = page != null ? page.ErrorMessage : "Unknown fetch error.";
                    sb.AppendLine("Fetched: no");
                    sb.AppendLine("Error: " + (string.IsNullOrWhiteSpace(error) ? "Unknown fetch error." : error));
                    sb.AppendLine("Instruction: do not summarize, score, compare, or report on this page as if it was read.");
                }
            }
            return sb.ToString().Trim();
        }

        string BuildStreamingBody()
        {
            var msgs = MessagesForProvider();
            var endpoint = providerManager.CurrentModel != null ? providerManager.CurrentModel.Endpoint : "";
            if (string.Equals(endpoint, "Gemini", StringComparison.OrdinalIgnoreCase))
            {
                var contents = new ArrayList();
                foreach (var m in msgs)
                {
                    if (!m.ContainsKey("role")) continue;
                    string role = Convert.ToString(m["role"]);
                    var parts = m["parts"] as ArrayList;
                    if (parts == null) continue;
                    var contentParts = new ArrayList();
                    foreach (var p in parts)
                    {
                        var pd = p as Dictionary<string, object>;
                        if (pd != null && pd.ContainsKey("text"))
                            contentParts.Add(new Dictionary<string, object> { { "text", pd["text"] } });
                    }
                    if (contentParts.Count > 0)
                        contents.Add(new Dictionary<string, object> { { "role", role == "model" ? "model" : "user" }, { "parts", contentParts } });
                }
                return json.Serialize(new Dictionary<string, object> { { "contents", contents } });
            }
            else
            {
                var openaiMessages = new ArrayList();
                foreach (var m in msgs)
                {
                    if (!m.ContainsKey("role")) continue;
                    string role = Convert.ToString(m["role"]);
                    var parts = m["parts"] as ArrayList;
                    if (parts == null) continue;
                    string content = "";
                    foreach (var p in parts)
                    {
                        var pd = p as Dictionary<string, object>;
                        if (pd != null && pd.ContainsKey("text"))
                            content += Convert.ToString(pd["text"]) + "\n";
                    }
                    openaiMessages.Add(new Dictionary<string, object> { { "role", role }, { "content", content.TrimEnd('\n') } });
                }
                string modelId = providerManager.CurrentModel != null ? providerManager.CurrentModel.Id : "gpt-3.5-turbo";
                return json.Serialize(new Dictionary<string, object> {
                    { "model", modelId },
                    { "messages", openaiMessages },
                    { "stream", true }
                });
            }
        }

        void ExportCurrentChat()
        {
            if (messages.Count == 0)
            {
                MessageBox.Show(this, "No chat messages to export.", "Export Chat");
                return;
            }
            string format = PromptExportFormat();
            if (string.IsNullOrWhiteSpace(format)) return;
            string content = BuildConversationExport(format);
            SaveTextAsFormat(content, format, false);
        }

        string BuildConversationExport(string format)
        {
            bool markdown = string.Equals(format, "md", StringComparison.OrdinalIgnoreCase)
                || string.Equals(format, "docx", StringComparison.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            if (markdown)
            {
                sb.AppendLine("# " + (currentTaskTitle ?? "ZhuaQian Chat"));
                sb.AppendLine();
                sb.AppendLine("- exportedAt: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("- provider: " + provider);
                sb.AppendLine("- model: " + CurrentModelLabel());
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine(currentTaskTitle ?? "ZhuaQian Chat");
                sb.AppendLine("exportedAt: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine(CurrentModelLabel());
                sb.AppendLine();
            }

            foreach (var msgObj in messages)
            {
                var msg = msgObj as Dictionary<string, object>;
                if (msg == null || !msg.ContainsKey("role")) continue;
                string role = Convert.ToString(msg["role"]);
                string label = role == "user" ? "You" : "ZhuaQian";
                string text = PartsToText(msg.ContainsKey("parts") ? msg["parts"] : null);
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (markdown) sb.AppendLine("## " + label);
                else sb.AppendLine("[" + label + "]");
                sb.AppendLine(text.Trim());
                sb.AppendLine();
            }
            return sb.ToString();
        }

        void UseActiveWindowContext()
        {
            CaptureActiveWindowContext();
            string clip = permClipboard ? GetClipboardText() : "";
            var sb = new StringBuilder();
            sb.AppendLine("[Current Windows context]");
            sb.AppendLine("capturedAt: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("activeProcess: " + lastContextProcess);
            sb.AppendLine("activeWindow: " + lastContextTitle);
            if (!string.IsNullOrWhiteSpace(clip))
            {
                sb.AppendLine();
                sb.AppendLine("[Clipboard text]");
                sb.AppendLine(ApplyRedaction(TrimForPrompt(clip, 6000)));
            }
            else if (!permClipboard)
            {
                sb.AppendLine();
                sb.AppendLine("[Clipboard text skipped: clipboard permission is off]");
            }
            pendingParts.Add(NewTextPart(sb.ToString()));
            pendingLabels.Add(Tr("Active window context", "当前窗口上下文", "目前視窗上下文"));
            if (string.IsNullOrWhiteSpace(input.Text))
                input.Text = Tr("Analyze the current app/window context and tell me what to do next.",
                                "请分析当前应用/窗口上下文，告诉我下一步应该怎么做。",
                                "請分析目前應用程式/視窗上下文，告訴我下一步應該怎麼做。");
            RefreshAttachLabel();
            LogAction("UseCurrentContext", lastContextProcess + " | " + lastContextTitle);
            AppendChat("ZhuaQian", Tr("Current window context is ready in this task.",
                                       "当前窗口上下文已加入这个任务。",
                                       "目前視窗上下文已加入這個任務。"), Color.FromArgb(0, 130, 80));
        }

        void PrepareAgentPlan()
        {
            string goal = PromptText(Tr("Agent task", "Agent 任务", "Agent 任務"), Tr("Goal:", "目标：", "目標："), input.Text.Trim());
            if (goal == null) return;
            goal = goal.Trim();
            if (goal.Length == 0) return;
            CaptureActiveWindowContext();
            input.Text =
                Tr("Create a safe execution plan for this Windows task. Do not perform destructive actions directly. List required confirmations, files affected, rollback ideas, and the first small step.\r\n\r\nTask: ",
                   "请为这个 Windows 任务制定安全执行计划。不要直接执行破坏性操作。列出需要确认的动作、会影响的文件、回滚方案，以及第一个小步骤。\r\n\r\n任务：",
                   "請為這個 Windows 任務制定安全執行計畫。不要直接執行破壞性操作。列出需要確認的動作、會影響的檔案、回復方案，以及第一個小步驟。\r\n\r\n任務：")
                + goal
                + "\r\n\r\n[Current app]\r\nprocess: " + lastContextProcess + "\r\nwindow: " + lastContextTitle;
            LogAction("AgentPlanner", goal);
        }

        void ShowSkillLibrary()
        {
            using (var dlg = new Form())
            {
                dlg.Text = Tr("Skill Library", "职业技能库", "職業技能庫");
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Size = new Size(620, 420);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.Font = Font;

                int y = 24;
                AddToolButton(dlg, Tr("Finance Reconcile", "财务对账", "財務對帳"), 16, y, (s, e) => InsertSkillPrompt("finance"));
                AddToolButton(dlg, Tr("Programmer Debug", "程序员 Debug", "程式員 Debug"), 320, y, (s, e) => InsertSkillPrompt("debug"));
                y += 52;
                AddToolButton(dlg, Tr("Copywriting Polish", "文案润色", "文案潤色"), 16, y, (s, e) => InsertSkillPrompt("copy"));
                AddToolButton(dlg, Tr("Meeting Summary", "会议纪要", "會議紀要"), 320, y, (s, e) => InsertSkillPrompt("meeting"));
                y += 52;
                AddToolButton(dlg, Tr("Excel Report", "Excel 报表分析", "Excel 報表分析"), 16, y, (s, e) => InsertSkillPrompt("excel"));
                AddToolButton(dlg, Tr("Privacy Review", "隐私检查", "隱私檢查"), 320, y, (s, e) => InsertSkillPrompt("privacy"));
                y += 60;

                // Load SKILL.md from plugin directory
                if (Directory.Exists(pluginDir))
                {
                    var skillFiles = Directory.GetFiles(pluginDir, "SKILL.md", SearchOption.AllDirectories);
                    if (skillFiles.Length > 0)
                    {
                        var lbl = new Label { Left = 16, Top = y, Width = 580, Height = 18, Text = Tr("File-based skills:", "文件技能：", "檔案技能："), ForeColor = Color.Gray };
                        dlg.Controls.Add(lbl);
                        y += 22;
                        foreach (var sf in skillFiles)
                        {
                            string relDir = Path.GetFileName(Path.GetDirectoryName(sf)) ?? "";
                            string name = relDir + " SKILL.md";
                            var btn = new Button { Text = name, Left = 16, Top = y, Width = 280, Height = 36 };
                            string path = sf;
                            btn.Click += (s, e) =>
                            {
                                try
                                {
                                    string content = File.ReadAllText(path, Encoding.UTF8);
                                    input.Text = content + (string.IsNullOrWhiteSpace(input.Text) ? "" : "\r\n\r\n" + input.Text);
                                    LogAction("SkillLoad", path);
                                    AppendChat("ZhuaQian", Tr("Skill loaded from: ", "已载入技能文件：", "已載入技能文件：") + path, Color.FromArgb(0, 130, 80));
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show(this, ex.Message, "SKILL.md load failed");
                                }
                            };
                            dlg.Controls.Add(btn);
                            y += 40;
                        }
                    }
                }

                var close = new Button { Text = Tr("Close", "关闭", "關閉"), Location = new Point(490, Math.Max(y + 10, 340) - 40), Size = new Size(95, 32) };
                close.Click += (s, e) => dlg.Close();
                dlg.Controls.Add(close);
                dlg.ShowDialog(this);
            }
        }

        void InsertSkillPrompt(string key)
        {
            string prompt;
            if (key == "finance")
                prompt = Tr("Act as a finance reconciliation assistant. Compare records, find mismatches, missing invoices, duplicate payments, and output an exception table plus next actions.",
                            "你是财务对账助手。请比对记录，找出金额不一致、缺失发票、重复付款，并输出异常表和下一步处理建议。",
                            "你是財務對帳助手。請比對記錄，找出金額不一致、缺失發票、重複付款，並輸出異常表和下一步處理建議。");
            else if (key == "debug")
                prompt = Tr("Act as a senior debugging assistant. Read the error/log/code context, identify the root cause, propose a minimal fix, and list tests to run.",
                            "你是资深 Debug 助手。请阅读错误/日志/代码上下文，定位根因，提出最小修复方案，并列出需要运行的测试。",
                            "你是資深 Debug 助手。請閱讀錯誤/日誌/程式碼上下文，定位根因，提出最小修復方案，並列出需要執行的測試。");
            else if (key == "copy")
                prompt = Tr("Polish this copy for clarity, persuasion, and conversion. Keep the original meaning, provide three versions, and explain when to use each.",
                            "请润色这段文案，让它更清晰、更有说服力、更利于转化。保持原意，输出 3 个版本，并说明各自适合的场景。",
                            "請潤色這段文案，讓它更清晰、更有說服力、更利於轉化。保持原意，輸出 3 個版本，並說明各自適合的場景。");
            else if (key == "meeting")
                prompt = Tr("Turn this meeting content into minutes: decisions, action items, owners, deadlines, risks, and follow-up message.",
                            "请把会议内容整理成纪要：决策、待办事项、负责人、截止时间、风险点，以及会后跟进消息。",
                            "請把會議內容整理成紀要：決策、待辦事項、負責人、截止時間、風險點，以及會後跟進訊息。");
            else if (key == "excel")
                prompt = Tr("Analyze this spreadsheet like a business analyst. Find trends, outliers, formula suggestions, chart suggestions, and possible VBA automation.",
                            "请像商业分析师一样分析这个表格：趋势、异常值、公式建议、图表建议，以及可自动化的 VBA 方案。",
                            "請像商業分析師一樣分析這個表格：趨勢、異常值、公式建議、圖表建議，以及可自動化的 VBA 方案。");
            else
                prompt = Tr("Review this content for sensitive information before cloud upload. Identify phone numbers, IDs, bank cards, emails, secrets, and suggest redaction.",
                            "请在云端上传前检查这份内容的敏感信息：手机号、证件号、银行卡、邮箱、密钥，并建议脱敏方式。",
                            "請在雲端上傳前檢查這份內容的敏感資訊：電話、證件號、銀行卡、信箱、金鑰，並建議遮蔽方式。");

            input.Text = prompt + (string.IsNullOrWhiteSpace(input.Text) ? "" : "\r\n\r\n" + input.Text);
            LogAction("SkillPrompt", key);
            AppendChat("ZhuaQian", Tr("Skill prompt inserted into the input box.",
                                       "技能指令已插入输入框。",
                                       "技能指令已插入輸入框。"), Color.FromArgb(0, 130, 80));
        }

        void OrganizeFolder()
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = Tr("Choose a messy folder to organize", "选择要整理的杂乱文件夹", "選擇要整理的雜亂資料夾");
                if (fbd.ShowDialog(this) != DialogResult.OK) return;
                ExecuteOrganizeFolder(fbd.SelectedPath);
            }
        }

        void ExecuteOrganizeFolder(string folderPath)
        {
            folderPath = CleanPath(folderPath);
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                AppendChat("Error", Tr("Folder not found: ", "找不到文件夹：", "找不到資料夾：") + folderPath, Color.FromArgb(190, 40, 40));
                return;
            }
            if (!EnsurePermission(Tr("Move/delete files", "移动/删除文件", "移動/刪除檔案"), permFileMoveDelete, true, "Organize Folder")) return;

            var files = new List<string>();
            try { files.AddRange(Directory.GetFiles(folderPath)); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Tr("Organize failed", "整理失败", "整理失敗")); return; }
            if (files.Count == 0)
            {
                AppendChat("ZhuaQian", Tr("No files found in this folder.", "这个文件夹里没有文件。", "這個資料夾裡沒有檔案。"), Color.FromArgb(200, 120, 0));
                return;
            }

            var preview = new StringBuilder();
            preview.AppendLine(Tr("Move ", "将 ", "將 ") + files.Count + Tr(" files into _ZhuaQian_Organized?", " 个文件移动到 _ZhuaQian_Organized？", " 個檔案移動到 _ZhuaQian_Organized？"));
            preview.AppendLine();
            for (int i = 0; i < Math.Min(12, files.Count); i++)
                preview.AppendLine(Path.GetFileName(files[i]) + " -> " + Tools.FolderOrganizer.CategoryFor(files[i]));
            if (files.Count > 12) preview.AppendLine("...");
            preview.AppendLine();
            preview.AppendLine(Tr("This will move local files by type category. Continue?", "这会按类型移动本地文件。是否继续？", "這會按類型移動本機檔案。是否繼續？"));

            var organizeGate = PermissionGate.FromJson(permGate.ToJson());
            organizeGate.Set("permFileMoveDelete", PermissionLevel.Ask);
            var pipeline = agentPipelineFactory.Create(organizeGate, pluginDir, allowAdvancedPlugins);
            pipeline.RequestApproval = command => ShowApprovalCard(
                "OrganizeFolder",
                "Organize folder",
                "Execute",
                "Move/delete files",
                folderPath,
                "This action moves local files into _ZhuaQian_Organized subfolders by category. A rollback manifest will be created.",
                "Moved files plus rollback manifest.",
                preview.ToString(),
                folderPath);

            var args = new Dictionary<string, object>();
            args["rootDir"] = folderPath;
            args["taskTitle"] = currentTaskTitle;
            var result = pipeline.Run(new AgentCommand("OrganizeFolder", "permFileMoveDelete", currentTaskId, folderPath, "Organize folder " + folderPath, args));
            if (result.Status == CommandStatus.Cancelled)
            {
                SetCurrentTaskStatus("needs_input", "Folder organize cancelled", true);
                RecordAction("OrganizeFolder", "cancelled", "Folder organize cancelled", folderPath);
                return;
            }
            if (result.Status != CommandStatus.Success)
            {
                SetCurrentTaskStatus("failed", "Organize failed", true);
                RecordAction("OrganizeFolder", "failed", result.ErrorMessage ?? "Organize failed", folderPath);
                AppendChat("Error", result.ErrorMessage ?? Tr("Organize failed.", "整理失败。", "整理失敗。"), Color.FromArgb(190, 40, 40));
                return;
            }

            string manifestPath = result.RollbackManifestPath;
            undoRedo.Record(Tools.UndoableActionType.OrganizeRollback, "Organize folder", manifestPath);
            RecordExportHistory("rollback", manifestPath, files.Count);
            LogAction("OrganizeFolder", "Organized " + folderPath + " through AgentPipeline");
            SetCurrentTaskStatus("ready_for_review", "Organized " + folderPath, true);
            RecordAction("OrganizeFolder", "success", "Files organized under " + folderPath, manifestPath);
            AppendChat("ZhuaQian", "Files organized into _ZhuaQian_Organized.\r\nRollback manifest:\r\n" + manifestPath, Color.FromArgb(0, 130, 80));
        }

        string FileTypeBucket(string ext)
        {
            ext = (ext ?? "").ToLowerInvariant();
            if (imageExts.Contains(ext)) return "Images";
            if (ext == ".pdf") return "PDF";
            if (ext == ".docx" || ext == ".doc") return "Word";
            if (ext == ".xlsx" || ext == ".xlsm" || ext == ".xls" || ext == ".csv") return "Excel";
            if (ext == ".pptx" || ext == ".ppt") return "PowerPoint";
            if (textExts.Contains(ext)) return "Text-Code";
            return "Other";
        }

        string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; i < 10000; i++)
            {
                string candidate = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(candidate)) return candidate;
            }
            return Path.Combine(dir, name + " (" + Guid.NewGuid().ToString("N").Substring(0, 8) + ")" + ext);
        }

        void CreateTemplateOrEmailDraft()
        {
            if (!EnsurePermission(Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"), permFileWrite, true, "Template / Email Draft")) return;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Choose a text/markdown/docx template";
                ofd.Filter = "Templates|*.txt;*.md;*.docx|All files|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                string info = PromptText("Template data", "Core info:", input.Text.Trim());
                if (info == null) return;
                string template = ExtractTextDocument(ofd.FileName);
                string body = template;
                body = body.Replace("{{info}}", info).Replace("{info}", info).Replace("【信息】", info);
                if (body == template) body = template + "\r\n\r\n---\r\nCore information:\r\n" + info;
                body = ApplyRedaction(body);

                bool email = MessageBox.Show(this, "Create an Outlook-compatible .eml email draft?\n\nYes = .eml draft\nNo = document draft", "Draft type", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                using (var sfd = new SaveFileDialog())
                {
                    sfd.Title = "Save draft";
                    sfd.Filter = email ? "Email draft|*.eml|Text|*.txt" : "Markdown|*.md|Text|*.txt";
                    sfd.FileName = email ? "draft.eml" : "draft.md";
                    if (sfd.ShowDialog(this) != DialogResult.OK) return;
                    if (email)
                    {
                        string subject = PromptText("Email subject", "Subject:", "ZhuaQian draft");
                        if (subject == null) subject = "ZhuaQian draft";
                        string eml = "Subject: " + subject.Replace("\r", " ").Replace("\n", " ") + "\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n" + body;
                        File.WriteAllText(sfd.FileName, eml, new UTF8Encoding(false));
                    }
                    else
                    {
                        File.WriteAllText(sfd.FileName, body, new UTF8Encoding(false));
                    }
                    LogAction("CreateDraft", "Created draft: " + sfd.FileName);
                    AppendChat("ZhuaQian", "Draft created:\r\n" + sfd.FileName, Color.FromArgb(0, 130, 80));
                }
            }
        }

        void PrepareExcelAssistant()
        {
            if (!EnsurePermission(Tr("Read local files", "读取本地文件", "讀取本機檔案"), permFileRead, false, "Excel Assistant")) return;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Choose Excel workbook";
                ofd.Filter = "Excel|*.xlsx;*.xlsm;*.csv|All files|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                string ask = PromptText("Excel instruction", "Instruction:", "分析销售额趋势并生成图表建议，同时给出可用公式或 VBA 宏代码。");
                if (ask == null) return;
                var info = new FileInfo(ofd.FileName);
                string text = ApplyRedaction(TrimForPrompt(ExtractTextDocument(ofd.FileName), 20000));
                pendingParts.Add(NewTextPart("[Excel workbook]\nname: " + info.Name + "\npath: " + info.FullName + "\n\n" + text));
                pendingLabels.Add(info.Name + " (" + FormatSize(info.Length) + ")");
                input.Text = ask;
                RefreshAttachLabel();
                LogAction("ExcelAssistant", "Prepared Excel analysis for " + info.FullName);
                AppendChat("ZhuaQian", "Excel workbook loaded. Press SEND to analyze and generate formulas/VBA suggestions.", Color.FromArgb(0, 130, 80));
            }
        }

        void ShowResourceMonitor()
        {
            var rows = new List<Tuple<long, string, int>>();
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    rows.Add(Tuple.Create(proc.WorkingSet64, proc.ProcessName, SafePid(proc)));
                    proc.Dispose();
                }
                catch (Exception _ex) { LogAction("Warning", "ResourceMonitor: " + _ex.Message); try { proc.Dispose(); } catch (Exception _ex2) { System.Diagnostics.Debug.WriteLine("ResourceMonitor dispose: " + _ex2.Message); } }
            }
            rows.Sort((a, b) => b.Item1.CompareTo(a.Item1));
            var sb = new StringBuilder();
            sb.AppendLine("Top memory processes:");
            sb.AppendLine();
            for (int i = 0; i < Math.Min(12, rows.Count); i++)
                sb.AppendLine((i + 1) + ". " + rows[i].Item2 + "  PID " + rows[i].Item3 + "  RAM " + FormatSize(rows[i].Item1));
            sb.AppendLine();
            sb.AppendLine("Advice: close browsers/editors you do not need first. End a process only when you recognize it and unsaved work is not at risk.");
            AppendChat("ZhuaQian", sb.ToString(), Color.FromArgb(0, 130, 80));

            string pidText = PromptText("End process", "PID to end:", "");
            if (string.IsNullOrWhiteSpace(pidText)) return;
            int pid;
            if (!int.TryParse(pidText.Trim(), out pid)) return;
            EndProcessByPid(pid);
        }

        void EndProcessByPid(int pid)
        {
            try
            {
                using (var proc = Process.GetProcessById(pid))
                {
                    if (!EnsurePermission(Tr("End processes", "结束进程", "結束處理程序"), permProcessManage, true, "End process PID " + pid)) return;
                    string processName = proc.ProcessName;
                    var processGate = PermissionGate.FromJson(permGate.ToJson());
                    processGate.Set("permProcessManage", PermissionLevel.Ask);
                    var pipeline = agentPipelineFactory.Create(processGate, pluginDir, allowAdvancedPlugins);
                    pipeline.RequestApproval = command => ShowApprovalCard("EndProcess",
                        Tr("Confirm end task", "确认结束进程", "確認結束處理程序"),
                        Tr("Execute", "执行", "執行"),
                        Tr("End processes", "结束进程", "結束處理程序"),
                        processName + " (PID " + pid + ")",
                        Tr("Unsaved data may be lost.", "未保存的数据可能会丢失。", "未儲存的資料可能會遺失。"),
                        "", "PID: " + pid, "");
                    var result = pipeline.Run(new AgentCommand("EndProcess", "permProcessManage", currentTaskId, pid.ToString(), "End process " + processName + " (PID " + pid + ")", new Dictionary<string, object>()));
                    if (result.Status == CommandStatus.Success)
                    {
                        LogAction("EndProcess", "Killed PID " + pid + " (" + processName + ") through AgentPipeline");
                        RecordAction("EndProcess", "success", "Killed PID " + pid + " (" + processName + ")", "");
                        AppendChat("ZhuaQian", "Ended process PID " + pid + ".", Color.FromArgb(0, 130, 80));
                    }
                    else if (result.Status == CommandStatus.Cancelled)
                    {
                        RecordAction("EndProcess", "cancelled", "Cancelled PID " + pid + " (" + processName + ")", "");
                    }
                    else
                    {
                        throw new Exception(result.ErrorMessage ?? "End process failed.");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "End process failed");
            }
        }

        int SafePid(Process p)
        {
            try { return p.Id; } catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("SafePid: " + _ex.Message); return 0; }
        }

        void ShowAuditLog()
        {
            string text = File.Exists(auditLogPath) ? File.ReadAllText(auditLogPath, Encoding.UTF8) : "No audit log yet.";
            using (var dlg = new Form())
            {
                dlg.Text = "Local Audit Log";
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Size = new Size(760, 520);
                dlg.Font = Font;
                var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Text = text };
                dlg.Controls.Add(box);
                dlg.ShowDialog(this);
            }
        }

        void PreviewRedaction()
        {
            string text = PromptText("Privacy redaction test", "Text:", input.Text.Trim());
            if (text == null) return;
            MessageBox.Show(this, ApplyRedaction(text), "Redacted preview");
        }

        void ShowFutureIntegrationPlan()
        {
            MessageBox.Show(this,
                "Planned next:\n\n- Voice ASR: local Whisper or Windows speech recognition.\n- Phone upload: local QR page on LAN for photos/files.\n- Clipboard sync: optional LAN encrypted relay.\n\nThese need a small local service, so they are listed as roadmap instead of half-built buttons.",
                "Voice / Mobile Plan");
        }

        void AddAttachment(string path)
        {
            if (!EnsurePermission(Tr("Read local files", "读取本地文件", "讀取本機檔案"), permFileRead, false, "Read File")) throw new Exception("Local file read permission is disabled.");
            path = CleanPath(path);
            if (!File.Exists(path)) throw new Exception("File does not exist: " + path);
            var info = new FileInfo(path);
            string ext = info.Extension.ToLowerInvariant();

            if (imageExts.Contains(ext) || ext == ".pdf")
            {
                if (info.Length > MaxInlineBytes) throw new Exception("File too large: " + FormatSize(info.Length));
                pendingParts.Add(NewInlinePart(path));
                pendingLabels.Add(info.Name + " (" + FormatSize(info.Length) + ")");
                LogAction("AddAttachment", "Loaded binary attachment: " + info.FullName);
                return;
            }

            if (docExts.Contains(ext))
            {
                if (info.Length > MaxDocBytes) throw new Exception("Document too large: " + FormatSize(info.Length));
                string text = ApplyRedaction(TrimText(ExtractTextDocument(path)));
                string block = "[Loaded document]\nname: " + info.Name + "\npath: " + info.FullName + "\ntype: " + ext + "\nsize: " + FormatSize(info.Length) + "\n\n" + text;
                pendingParts.Add(NewTextPart(block));
                pendingLabels.Add(info.Name + " (" + FormatSize(info.Length) + ")");
                LogAction("AddAttachment", "Loaded text document: " + info.FullName);
                return;
            }

            throw new Exception("Unsupported file type: " + ext);
        }

        void CaptureScreenForOcr()
        {
            if (!EnsurePermission(Tr("Take screenshots", "截屏识别", "截圖識別"), permScreenshot, false, "Screenshot OCR")) return;
            try
            {
                Directory.CreateDirectory(screenDir);
                Rectangle bounds = Screen.AllScreens[0].Bounds;
                foreach (var screen in Screen.AllScreens) bounds = Rectangle.Union(bounds, screen.Bounds);
                string path = Path.Combine(screenDir, "screen-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                using (var bmp = new Bitmap(bounds.Width, bounds.Height))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                AddAttachment(path);
                if (string.IsNullOrWhiteSpace(input.Text))
                    input.Text = "请识别截图里的文字，提取关键信息，并给我可执行的下一步建议。";
                RefreshAttachLabel();
                LogAction("CaptureScreen", "Captured screen to " + path);
                AppendChat("ZhuaQian", "Screenshot captured. Press SEND to run OCR/analysis with Gemini or Auto provider.", Color.FromArgb(0, 130, 80));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Screenshot failed");
            }
        }

        void ToggleClipboardMonitor()
        {
            if (!clipboardMonitorEnabled && !EnsurePermission(Tr("Read clipboard monitor", "读取剪贴板监控", "讀取剪貼簿監控"), permClipboard, false, "Clipboard Monitor")) return;
            clipboardMonitorEnabled = !clipboardMonitorEnabled;
            if (clipboardButton != null) clipboardButton.Text = clipboardMonitorEnabled
                ? Tr("Clipboard: On", "剪贴板：开", "剪貼簿：開")
                : Tr("Clipboard: Off", "剪贴板：关", "剪貼簿：關");
            if (clipboardMonitorEnabled)
            {
                lastClipboardText = GetClipboardText();
                clipboardTimer.Start();
                LogAction("ClipboardMonitor", "Enabled clipboard monitor");
                AppendChat("ZhuaQian", Tr("Clipboard monitor is on. New copied text will be summarized automatically.",
                                           "剪贴板监控已开启。新复制的文字会自动总结。",
                                           "剪貼簿監控已開啟。新複製的文字會自動摘要。"), Color.FromArgb(0, 130, 80));
            }
            else
            {
                clipboardTimer.Stop();
                LogAction("ClipboardMonitor", "Disabled clipboard monitor");
                AppendChat("ZhuaQian", Tr("Clipboard monitor is off.", "剪贴板监控已关闭。", "剪貼簿監控已關閉。"), Color.FromArgb(0, 130, 80));
            }
        }

        async Task ClipboardTimerTick()
        {
            if (!clipboardMonitorEnabled || clipboardBusy) return;
            string text = GetClipboardText();
            if (string.IsNullOrWhiteSpace(text) || text == lastClipboardText) return;
            lastClipboardText = text;
            clipboardBusy = true;
            try
            {
                input.Text = "请总结下面剪贴板内容，提取重点、风险和下一步行动：\r\n\r\n" + ApplyRedaction(TrimForPrompt(text, 12000));
                LogAction("ClipboardRead", "Read text from clipboard (" + text.Length + " chars)");
                if (HasUsableProviderKey()) await SendMessage();
            }
            finally
            {
                clipboardBusy = false;
            }
        }

        string GetClipboardText()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : "";
            }
            catch (Exception ex)
            {
                LogAction("Warning", "Clipboard read failed: " + ex.Message);
                return "";
            }
        }

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
                    AppendChat("ZhuaQian", "Indexed " + files.Count + " files / " + knowledgeIndex.Count + " chunks from:\r\n" + fbd.SelectedPath, Color.FromArgb(0, 130, 80));
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

        string BuildKnowledgeContext(string query, int limit)
        {
            var scored = new List<KeyValuePair<int, IndexedDoc>>();
            foreach (var doc in knowledgeIndex)
            {
                int score = ScoreDoc(doc, query);
                if (score > 0) scored.Add(new KeyValuePair<int, IndexedDoc>(score, doc));
            }
            scored.Sort((a, b) => b.Key.CompareTo(a.Key));
            var sb = new StringBuilder();
            sb.AppendLine("[Local knowledge base search]");
            sb.AppendLine("query: " + query);
            int count = 0;
            foreach (var item in scored)
            {
                if (count++ >= limit) break;
                sb.AppendLine();
                sb.AppendLine("[" + count + "] " + item.Value.Name);
                sb.AppendLine("chunkId: " + SafeMeta(item.Value.ChunkId));
                if (!string.IsNullOrWhiteSpace(item.Value.Heading)) sb.AppendLine("heading: " + item.Value.Heading);
                sb.AppendLine("layer: " + SafeMeta(item.Value.Layer));
                sb.AppendLine("tags: " + SafeMeta(item.Value.Tags));
                sb.AppendLine("modifiedAt: " + item.Value.ModifiedAt.ToString("yyyy-MM-dd HH:mm"));
                if (item.Value.SizeBytes > 0) sb.AppendLine("size: " + FormatSize(item.Value.SizeBytes));
                sb.AppendLine("path: " + item.Value.Path);
                if (!string.IsNullOrWhiteSpace(item.Value.Summary)) sb.AppendLine("summary: " + item.Value.Summary);
                sb.AppendLine("snippet:");
                sb.AppendLine(ExtractSnippet(item.Value.Text, query, 1400));
            }
            return count == 0 ? "" : sb.ToString();
        }

        int ScoreDoc(IndexedDoc doc, string query)
        {
            string hay = ((doc.Name ?? "") + "\n" + (doc.Heading ?? "") + "\n" + (doc.Tags ?? "") + "\n" + (doc.Summary ?? "") + "\n" + (doc.Text ?? "")).ToLowerInvariant();
            int score = 0;
            foreach (Match m in Regex.Matches(query.ToLowerInvariant(), "[\\p{L}\\p{N}_]+"))
            {
                string term = m.Value;
                if (term.Length == 0) continue;
                int idx = -1;
                while ((idx = hay.IndexOf(term, idx + 1, StringComparison.Ordinal)) >= 0) score++;
                if ((doc.Name ?? "").ToLowerInvariant().Contains(term)) score += 5;
                if ((doc.Heading ?? "").ToLowerInvariant().Contains(term)) score += 4;
                if ((doc.Tags ?? "").ToLowerInvariant().Contains(term)) score += 4;
                if ((doc.Summary ?? "").ToLowerInvariant().Contains(term)) score += 2;
            }
            if (string.Equals(doc.Layer, "hot", StringComparison.OrdinalIgnoreCase)) score += 2;
            return score;
        }

        float[] GetQueryEmbedding(string text)
        {
            string url = EmbeddingUrlFromChatUrl(string.IsNullOrWhiteSpace(localApiUrl) ? DefaultLocalApiUrl : localApiUrl);
            string model = string.IsNullOrWhiteSpace(embeddingModel) ? "nomic-embed-text" : embeddingModel;
            return vectorIndex.ComputeQueryEmbedding(text ?? "", url, model);
        }

        float CosineSimilarity(float[] a, float[] b) { return Knowledge.VectorIndex.CosineSimilarity(a, b); }

        string BuildKnowledgeContextHybrid(string query, int limit)
        {
            float[] queryVec = GetQueryEmbedding(query);
            var scored = new List<KeyValuePair<double, IndexedDoc>>();
            foreach (var doc in knowledgeIndex)
            {
                double combined = ScoreDoc(doc, query) * 0.5;
                if (queryVec != null)
                {
                    float[] docVec = GetDocEmbedding(doc);
                    if (docVec != null)
                        combined += CosineSimilarity(queryVec, docVec) * 0.5;
                }
                if (combined > 0) scored.Add(new KeyValuePair<double, IndexedDoc>(combined, doc));
            }
            scored.Sort((a, b) => b.Key.CompareTo(a.Key));
            var sb = new StringBuilder();
            sb.AppendLine("[Local knowledge base hybrid search]");
            sb.AppendLine("query: " + query);
            if (queryVec != null) sb.AppendLine("embedding: " + embeddingModel);
            sb.AppendLine("mode: hybrid (keyword 0.5 + vector 0.5)");
            int count = 0;
            foreach (var item in scored)
            {
                if (count++ >= limit) break;
                sb.AppendLine();
                sb.AppendLine("[" + count + "] " + item.Value.Name + "  score=" + item.Key.ToString("F3"));
                sb.AppendLine("chunkId: " + SafeMeta(item.Value.ChunkId));
                if (!string.IsNullOrWhiteSpace(item.Value.Heading)) sb.AppendLine("heading: " + item.Value.Heading);
                sb.AppendLine("layer: " + SafeMeta(item.Value.Layer));
                sb.AppendLine("tags: " + SafeMeta(item.Value.Tags));
                sb.AppendLine("modifiedAt: " + item.Value.ModifiedAt.ToString("yyyy-MM-dd HH:mm"));
                if (item.Value.SizeBytes > 0) sb.AppendLine("size: " + FormatSize(item.Value.SizeBytes));
                sb.AppendLine("path: " + item.Value.Path);
                if (!string.IsNullOrWhiteSpace(item.Value.Summary)) sb.AppendLine("summary: " + item.Value.Summary);
                sb.AppendLine("snippet:");
                sb.AppendLine(ExtractSnippet(item.Value.Text, query, 1400));
            }
            return count == 0 ? "" : sb.ToString();
        }

        float[] GetDocEmbedding(IndexedDoc doc)
        {
            float[] cached = vectorIndex.GetVector(doc.ChunkId);
            if (cached != null) return cached;
            string textForEmbedding = (doc.Heading + "\n" + doc.Summary + "\n" + ExtractSnippet(doc.Text, doc.Heading, 512));
            if (string.IsNullOrWhiteSpace(textForEmbedding))
                textForEmbedding = doc.Text;
            if (textForEmbedding.Length > 1024)
                textForEmbedding = textForEmbedding.Substring(0, 1024);
            string url = EmbeddingUrlFromChatUrl(string.IsNullOrWhiteSpace(localApiUrl) ? DefaultLocalApiUrl : localApiUrl);
            string model = string.IsNullOrWhiteSpace(embeddingModel) ? "nomic-embed-text" : embeddingModel;
            return vectorIndex.ComputeQueryEmbedding(textForEmbedding, url, model);
        }

        void SaveVectorsAsync(string embedUrl)
        {
            string embedModel = string.IsNullOrWhiteSpace(embeddingModel) ? "nomic-embed-text" : embeddingModel;
            var docs = new List<Knowledge.ChunkedDoc>();
            foreach (var d in knowledgeIndex)
            {
                docs.Add(new Knowledge.ChunkedDoc
                {
                    DocId = d.DocId,
                    ChunkId = d.ChunkId,
                    Path = d.Path,
                    Name = d.Name,
                    Heading = d.Heading,
                    Text = d.Text,
                    Summary = d.Summary,
                    Tags = d.Tags,
                    Layer = d.Layer,
                    Offset = d.Offset,
                    SizeBytes = d.SizeBytes,
                    ModifiedAt = d.ModifiedAt
                });
            }
            vectorIndex.SaveAll(docs, embedUrl, embedModel);
        }

        string SafeMeta(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        string ExtractSnippet(string text, string query, int max)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            int pos = 0;
            foreach (Match m in Regex.Matches(query.ToLowerInvariant(), "[\\p{L}\\p{N}_]+"))
            {
                int found = text.ToLowerInvariant().IndexOf(m.Value, StringComparison.Ordinal);
                if (found >= 0) { pos = Math.Max(0, found - 220); break; }
            }
            string snippet = text.Substring(pos, Math.Min(max, text.Length - pos));
            return snippet.Replace("\0", " ");
        }

        string BuildLocalSummary(string text, int max)
        {
            return chunker.BuildSummary(text, max);
        }

        string InferKnowledgeTags(string name, string text)
        {
            return chunker.InferTags(name, text);
        }

        string InferKnowledgeLayer(string path, DateTime modifiedAt)
        {
            return chunker.InferLayer(path, modifiedAt);
        }

        async Task RunBatchFiles()
        {
            if (!EnsurePermission(Tr("Read local files", "读取本地文件", "讀取本機檔案"), permFileRead, false, "Batch Report")) return;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Choose files for batch report";
                ofd.Multiselect = true;
                ofd.Filter = "Supported files|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.pdf;*.docx;*.xlsx;*.xlsm;*.pptx;*.txt;*.md;*.csv;*.json|All files|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                pendingParts.Clear();
                pendingLabels.Clear();
                var sb = new StringBuilder();
                sb.AppendLine("[Batch queue]");
                sb.AppendLine("请逐个处理这些文件，并生成一份总报告：摘要、关键数据、风险、建议行动。");
                foreach (var file in ofd.FileNames)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        string ext = info.Extension.ToLowerInvariant();
                        if (imageExts.Contains(ext) || ext == ".pdf")
                        {
                            AddAttachment(file);
                        }
                        else
                        {
                            sb.AppendLine();
                            sb.AppendLine("## " + info.Name);
                            sb.AppendLine("path: " + info.FullName);
                            sb.AppendLine(ApplyRedaction(TrimForPrompt(ExtractTextDocument(file), 8000)));
                            pendingLabels.Add(info.Name + " (" + FormatSize(info.Length) + ")");
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine();
                        sb.AppendLine("[Skipped] " + file + ": " + ex.Message);
                    }
                }
                pendingParts.Insert(0, NewTextPart(sb.ToString()));
                input.Text = "请执行批量文件处理，输出一份结构化汇总报告。";
                RefreshAttachLabel();
                LogAction("BatchFiles", "Prepared batch report for " + ofd.FileNames.Length + " files");
                if (HasUsableProviderKey()) await SendMessage();
            }
        }

        void RunPlugin()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Choose plugin script";
                ofd.Filter = allowAdvancedPlugins ? "Plugins|*.py;*.ps1;*.exe;*.bat;*.cmd|All files|*.*" : "Safe plugins|*.py;*.ps1";
                if (Directory.Exists(pluginDir)) ofd.InitialDirectory = pluginDir;
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                RunPluginPath(ofd.FileName, input.Text);
            }
        }

        void RunPluginPath(string path, string stdin)
        {
            path = CleanPath(path);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                AppendChat("Error", Tr("Plugin file not found: ", "找不到插件文件：", "找不到外掛檔案：") + path, Color.FromArgb(190, 40, 40));
                return;
            }
            if (!EnsurePermission(Tr("Run plugins", "运行插件", "執行外掛"), permPluginRun, true, "Run Plugin")) return;
            try
            {
                var pluginGate = PermissionGate.FromJson(permGate.ToJson());
                pluginGate.Set("permPluginRun", PermissionLevel.Ask);
                var pipeline = agentPipelineFactory.Create(pluginGate, pluginDir, allowAdvancedPlugins);
                pipeline.RequestApproval = approvalCommand => ConfirmPluginRun(approvalCommand.Target, stdin);

                var args = new Dictionary<string, object>();
                args["stdin"] = stdin ?? "";
                args["taskTitle"] = currentTaskTitle;
                var pluginCommand = new AgentCommand("RunPlugin", "permPluginRun", currentTaskId, path, "Run plugin: " + Path.GetFileName(path), args);
                var result = pipeline.Run(pluginCommand);

                if (result.Status == CommandStatus.Cancelled)
                {
                    SetCurrentTaskStatus("needs_input", "Plugin cancelled", true);
                    RecordAction("RunPlugin", "cancelled", "Plugin run cancelled", path);
                    return;
                }
                if (result.Status == CommandStatus.Denied)
                {
                    SetCurrentTaskStatus("needs_input", "Plugin denied", true);
                    RecordAction("RunPlugin", "denied", result.ErrorMessage, path);
                    MessageBox.Show(this, result.ErrorMessage, "Plugin denied");
                    return;
                }
                if (result.Status != CommandStatus.Success)
                    throw new Exception(result.ErrorMessage ?? "Plugin failed.");

                input.Text = result.OutputText ?? "";
                LogAction("RunPlugin", "Executed plugin through AgentPipeline: " + path);
                SetCurrentTaskStatus("ready_for_review", "Plugin finished", true);
                RecordAction("RunPlugin", "success", "Executed plugin through AgentPipeline: " + path, "");
                AppendChat("ZhuaQian", "Plugin finished. Its output is ready in the input box.", Color.FromArgb(0, 130, 80));
            }
            catch (Exception ex)
            {
                SetCurrentTaskStatus("failed", "Plugin failed", true);
                RecordAction("RunPlugin", "failed", ex.Message, path);
                MessageBox.Show(this, ex.Message, "Plugin failed");
            }
        }

        bool ConfirmPluginRun(string path, string stdin)
        {
            string detail = Tr("Path: ", "路径：", "路徑：") + path
                + "\r\n" + Tr("Input chars: ", "输入字符数：", "輸入字元數：") + ((stdin ?? "").Length).ToString()
                + "\r\n\r\n" + Tr("Input preview:\r\n", "输入预览：\r\n", "輸入預覽：\r\n") + TrimForPrompt(stdin ?? "", 1200);
            string risk = Tr("This script can read its input and may perform local actions allowed by Windows and the selected interpreter.", "该脚本可以读取输入，并可能执行 Windows 与所选解释器允许的本地动作。", "該腳本可以讀取輸入，並可能執行 Windows 與所選直譯器允許的本機動作。");
            string output = Tr("Plugin stdout/stderr will be placed in the input box and saved as an output record.", "插件的标准输出/错误会放入输入框，并保存为产物记录。", "外掛的標準輸出/錯誤會放入輸入框，並儲存為產物記錄。");
            return ShowApprovalCard("RunPlugin", Tr("Run plugin", "运行插件", "執行外掛"), Tr("Execute", "执行", "執行"), Tr("Run plugins", "运行插件", "執行外掛"), path, risk, output, detail, path);
        }

        bool ShowApprovalCard(string actionType, string title, string modeName, string permission, string affected, string risk, string output, string detail, string outputPath)
        {
            string editNote;
            var decision = Tools.ApprovalCard.Show(this, title, modeName,
                new List<string> { permission },
                new List<string> { affected },
                risk, output, detail, Tr,
                out editNote);
            bool approved = decision == Tools.ApprovalDecision.Approved || decision == Tools.ApprovalDecision.Edited;
            RecordAction(actionType, approved ? "approved" : "cancelled", title + "\n" + (editNote ?? ""), outputPath);
            return approved;
        }

        async Task SendMessage()
        {
            if (sendButton == null || !sendButton.Enabled) return;
            if (TryRunLocalComputerCommand(input.Text)) return;
            if (TryRunNaturalLocalAction(input.Text)) return;
            if (!HasUsableProviderKey())
            {
                ShowSettings();
                if (!HasUsableProviderKey()) return;
            }

            string text = input.Text.Trim();
            text = ApplyRedaction(text);
            if (text.Length == 0 && pendingParts.Count == 0) return;
            string requestedExportFormat = DetectExportFormat(text);
            lastExportNameHint = string.IsNullOrWhiteSpace(requestedExportFormat) ? "" : BuildExportNameHint(text, requestedExportFormat);
            string requestedExportPath = DetectExportTargetPath(text, requestedExportFormat);
            List<string> requestedUrls = ExtractWebUrls(text);
            bool needsCurrentInfo = ShouldUseCurrentInfo(text);
            string webPageContext = "";
            string webSearchContext = "";
            if (requestedUrls.Count > 0)
            {
                if (!EnsurePermission(Tr("Fetch/analyze URL", "读取/分析网址", "讀取/分析網址"), permNetworkUpload, false, "URL Page Fetch")) return;
                webPageContext = BuildWebPageContext(requestedUrls);
            }
            if (needsCurrentInfo)
            {
                if (!EnsurePermission(Tr("Web search/current info", "联网检索/当前资料", "聯網檢索/目前資料"), permNetworkUpload, false, "Current Info Search")) return;
                webSearchContext = BuildWebSearchContext(text);
            }

            string maybePath = CleanPath(text);
            if (text.Length > 0 && File.Exists(maybePath) && pendingParts.Count == 0)
            {
                try
                {
                    AddAttachment(maybePath);
                    text = "Please analyze this file.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Load failed");
                    return;
                }
            }

            var parts = new ArrayList();
            string modeInstruction = BuildModeInstruction();
            if (!string.IsNullOrWhiteSpace(modeInstruction)) parts.Add(NewTextPart(modeInstruction));
            if (needsCurrentInfo) parts.Add(NewTextPart(BuildCurrentInfoInstruction()));
            if (!string.IsNullOrWhiteSpace(webPageContext)) parts.Add(NewTextPart(webPageContext));
            if (!string.IsNullOrWhiteSpace(webSearchContext)) parts.Add(NewTextPart(webSearchContext));
            string fileInstruction = BuildFileGenerationInstruction(requestedExportFormat);
            if (!string.IsNullOrWhiteSpace(fileInstruction)) parts.Add(NewTextPart(fileInstruction));
            if (text.Length > 0) parts.Add(NewTextPart(text));
            foreach (var p in pendingParts) parts.Add(p);

            if (MayUseCloudProvider(parts)
                && !EnsurePermission(Tr("Cloud/network upload", "云端/网络上传", "雲端/網路上傳"), permNetworkUpload, false, "Cloud Provider Call"))
                return;
            if (MayUseCloudProvider(parts) && !ConfirmCloudUploadIfNeeded(parts)) return;

            string displayText = text.Length > 0 ? text : "Please analyze the uploaded file.";
            if (pendingLabels.Count > 0) displayText += "\n[Files] " + string.Join("; ", pendingLabels.ToArray());
            if (needsCurrentInfo) displayText = "[Current info: live search requested]\n" + displayText;
            displayText = "[Mode: " + ModeDisplayName(workMode) + "]\n" + displayText;
            AppendChat("You", displayText, Color.FromArgb(30, 90, 180));

            messages.Add(new Dictionary<string, object> { { "role", "user" }, { "parts", parts } });
            if (currentTaskTitle == "New task") currentTaskTitle = GenerateTaskTitle();
            SetCurrentTaskStatus("running", "Waiting for " + CurrentModelLabel(), false);
            pendingParts.Clear();
            pendingLabels.Clear();
            RefreshAttachLabel();
            input.Clear();
            SaveCurrentTask();

            sendButton.Enabled = false;
            uploadButton.Enabled = false;
            Cursor = Cursors.WaitCursor;

            try
            {
                string reply;
                string modelBefore = providerManager.CurrentModel != null ? providerManager.CurrentModel.Id : "";
                providerManager.UseGoogleSearchForNextRequest = needsCurrentInfo;
                if (useStreaming && !needsCurrentInfo)
                {
                    string url = providerManager.StreamingUrl();
                    string apiKey = providerManager.CurrentApiKey();
                    string body = BuildStreamingBody();
                    if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(body))
                    {
                        var bridge = new StreamingBridge { TimeoutMs = 120000 };
                        var replyBuilder = new StringBuilder();
                        string errorMsg = null;
                        await bridge.StreamAsync(url, apiKey, body,
                            delta => replyBuilder.Append(delta),
                            () => { },
                            ex => errorMsg = ex.Message);
                        string streamed = replyBuilder.ToString();
                        if (errorMsg == null)
                        {
                            reply = streamed;
                        }
                        else
                        {
                            // Streaming failed (dead model / 4xx / 5xx / timeout). Gracefully
                            // fall back to non-streaming on another usable model so the work
                            // is not interrupted.
                            try
                            {
                                reply = await providerManager.SendAsync(MessagesForProvider());
                            }
                            catch (Exception ex)
                            {
                                LogAction("Warning", "Streaming fallback failed: " + ex.Message);
                                reply = streamed + "\r\n[Stream Error: " + errorMsg + "]";
                            }
                        }
                    }
                    else
                    {
                        // No streaming support for this model -> use non-streaming path.
                        reply = await providerManager.SendAsync(MessagesForProvider());
                    }
                }
                else
                {
                    reply = await providerManager.SendAsync(MessagesForProvider());
                }

                string modelAfter = providerManager.CurrentModel != null ? providerManager.CurrentModel.Id : ""; if (!string.Equals(modelBefore, modelAfter, StringComparison.OrdinalIgnoreCase)) { string notice = string.IsNullOrWhiteSpace(providerManager.LastFallbackNotice) ? "Auto-switched to fallback model: " + CurrentModelLabel() : providerManager.LastFallbackNotice; reply += "\r\n[" + notice + "]"; try { SaveConfig(); } catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("SaveConfig after model switch: " + _ex.Message); } }
                if (modelLabel != null) modelLabel.Text = CurrentModelLabel();
                AppendChat("ZhuaQian", reply, Color.FromArgb(0, 130, 80));
                var modelParts = new ArrayList { NewTextPart(reply) };
                messages.Add(new Dictionary<string, object> { { "role", "model" }, { "parts", modelParts } });
                SetCurrentTaskStatus("ready_for_review", "Model reply received", false);
                RecordAction("ChatCompletion", "success", "Received model reply (" + reply.Length + " chars)", "");
                SaveCurrentTask();
                if (!string.IsNullOrWhiteSpace(requestedExportFormat) && !SaveTextAsFormat(reply, requestedExportFormat, true, requestedExportPath))
                {
                    AppendChat("Error", Tr("File generation failed. Use Save File to choose a path manually.", "文件生成失败。可用“保存文件”手动选择路径。", "檔案產生失敗。可用「儲存檔案」手動選擇路徑。"), Color.FromArgb(190, 40, 40));
                }
            }
            catch (Exception ex)
            {
                AppendChat("Error", ex.Message, Color.FromArgb(190, 40, 40));
                SetCurrentTaskStatus("failed", "Provider error", false);
                RecordAction("ChatCompletion", "failed", ex.Message, "");
                SaveCurrentTask();
            }
            finally
            {
                sendButton.Enabled = true;
                uploadButton.Enabled = true;
                Cursor = Cursors.Default;
            }
        }

        bool HasUsableProviderKey()
        {
            return providerManager.HasUsableKey();
        }

        bool MayUseCloudProvider(ArrayList currentParts)
        {
            return providerManager.MayUseCloud();
        }

        List<Dictionary<string, object>> MessagesForProvider()
        {
            var result = new List<Dictionary<string, object>>();
            foreach (var msgObj in messages)
            {
                var dict = msgObj as Dictionary<string, object>;
                if (dict != null)
                    result.Add(dict);
            }
            return result;
        }

        bool ConfirmCloudUploadIfNeeded(ArrayList parts)
        {
            int textChars = 0;
            int inlineCount = 0;
            foreach (var partObj in parts)
            {
                var part = partObj as Dictionary<string, object>;
                if (part == null) continue;
                if (part.ContainsKey("text")) textChars += Convert.ToString(part["text"]).Length;
                if (part.ContainsKey("inlineData")) inlineCount++;
            }
            bool hasFiles = pendingLabels.Count > 0 || inlineCount > 0;
            if (!hasFiles && textChars < 4000) return true;

            var fileList = new List<string>();
            if (pendingLabels.Count > 0)
            {
                foreach (var label in pendingLabels) fileList.Add("- " + label);
            }
            string detail = "provider: " + provider + "\r\nmodel: " + CurrentModelLabel() + "\r\ntextChars: " + textChars + "\r\ninlineFiles: " + inlineCount;
            string risk = Tr("Content will be sent to a third-party cloud provider.",
                              "内容将被发送到第三方云端服务商。",
                              "內容將被傳送到第三方雲端服務商。");
            string affected = string.Join("\r\n", fileList);
            bool ok = ShowApprovalCard("CloudUploadConfirm",
                Tr("Confirm Cloud Upload", "确认云端上传", "確認雲端上傳"),
                Tr("Upload", "上传", "上傳"),
                Tr("Cloud/network upload", "云端/网络上传", "雲端/網路上傳"),
                affected, risk, "provider=" + provider + ", model=" + CurrentModelLabel() + ", textChars=" + textChars + ", inlineFiles=" + inlineCount,
                detail, "");
            if (!ok) SetCurrentTaskStatus("needs_input", "Cloud upload cancelled", true);
            return ok;
        }

        bool ConfirmDiagnosticsCloudUpload(string report)
        {
            string preview = TrimForPrompt(report ?? "", 1800);
            string risk = Tr("Local diagnostics include process names, PIDs, memory usage, drives, and system details. Window titles are omitted and sensitive tokens are redacted before upload.",
                              "本机诊断包含进程名、PID、内存占用、磁盘和系统信息。窗口标题已省略，敏感内容会在上传前脱敏。",
                              "本機診斷包含處理程序名稱、PID、記憶體、磁碟和系統資訊。視窗標題已省略，敏感內容會在上傳前遮蔽。");
            bool ok = ShowApprovalCard("DiagnosticsCloudUpload",
                Tr("Confirm Diagnostics Upload", "确认上传诊断信息", "確認上傳診斷資訊"),
                Tr("Upload", "上传", "上傳"),
                Tr("Cloud/network upload", "云端/网络上传", "雲端/網路上傳"),
                "Local computer diagnostics",
                risk,
                "provider=" + provider + ", model=" + CurrentModelLabel() + ", chars=" + (report ?? "").Length,
                preview,
                "");
            if (!ok) SetCurrentTaskStatus("needs_input", "Diagnostics upload cancelled", true);
            return ok;
        }

        bool PartsContainInlineData(ArrayList parts)
        {
            foreach (var partObj in parts)
            {
                var part = partObj as Dictionary<string, object>;
                if (part != null && part.ContainsKey("inlineData")) return true;
            }
            return false;
        }

        string CallOpenRouter()
        {
            if (string.IsNullOrWhiteSpace(openRouterApiKey))
                throw new Exception("OpenRouter API Key is empty. Open Settings and paste your key from https://openrouter.ai/settings/keys");

            var orMessages = new ArrayList();
            orMessages.Add(new Dictionary<string, object> {
                { "role", "system" },
                { "content", "You are ZhuaQian Desktop, a practical AI work assistant. Be concise, useful, and focused on making work faster. Never claim that you created, saved, exported, attached, moved, deleted, renamed, emailed, ran, clicked, or ended anything locally unless the desktop app explicitly reports a real saved path or execution result. If the user asks for TXT, Word, PowerPoint, or Excel output, provide clean content for that file; the desktop app will handle actual local saving." }
            });

            foreach (var msgObj in messages)
            {
                var msg = msgObj as Dictionary<string, object>;
                if (msg == null) continue;
                string role = Convert.ToString(msg["role"]);
                if (role == "model") role = "assistant";
                if (!msg.ContainsKey("parts")) continue;
                string content = PartsToText(msg["parts"]);
                if (string.IsNullOrWhiteSpace(content)) continue;
                orMessages.Add(new Dictionary<string, object> { { "role", role }, { "content", content } });
            }

            var payload = new Dictionary<string, object> {
                { "model", string.IsNullOrWhiteSpace(openRouterModel) ? DefaultOpenRouterModel : openRouterModel },
                { "messages", orMessages },
                { "temperature", 0.4 }
            };

            string body = json.Serialize(payload);
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + openRouterApiKey;
                wc.Headers["HTTP-Referer"] = "https://zhuaqian.local";
                wc.Headers["X-Title"] = "ZhuaQian Desktop";
                try
                {
                    string respText = wc.UploadString("https://openrouter.ai/api/v1/chat/completions", "POST", body);
                    var resp = json.DeserializeObject(respText) as Dictionary<string, object>;
                    return ExtractOpenRouterReply(resp);
                }
                catch (WebException ex)
                {
                    string detail = "";
                    if (ex.Response != null)
                    {
                        using (var stream = ex.Response.GetResponseStream())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                            detail = reader.ReadToEnd();
                    }
                    throw new Exception("OpenRouter API error for model '" + openRouterModel + "': " + ex.Message + (detail.Length > 0 ? "\n" + detail : ""));
                }
            }
        }

        string CallLocalApi()
        {
            if (string.IsNullOrWhiteSpace(localApiUrl) || string.IsNullOrWhiteSpace(localModel))
                throw new Exception("Local model is not configured. Open Settings and set Ollama URL/model.");

            var localMessages = new ArrayList();
            localMessages.Add(new Dictionary<string, object> {
                { "role", "system" },
                { "content", "You are ZhuaQian Desktop, a practical local AI work assistant. Be concise and useful. Never claim that you created, saved, exported, attached, moved, deleted, renamed, emailed, ran, clicked, or ended anything locally unless the desktop app explicitly reports a real saved path or execution result. If the user asks for TXT, Word, PowerPoint, or Excel output, provide clean content for that file; the desktop app will handle actual local saving." }
            });
            foreach (var msgObj in messages)
            {
                var msg = msgObj as Dictionary<string, object>;
                if (msg == null) continue;
                string role = Convert.ToString(msg["role"]);
                if (role == "model") role = "assistant";
                string content = PartsToText(msg.ContainsKey("parts") ? msg["parts"] : null);
                if (string.IsNullOrWhiteSpace(content)) continue;
                localMessages.Add(new Dictionary<string, object> { { "role", role }, { "content", content } });
            }

            var payload = new Dictionary<string, object> {
                { "model", localModel },
                { "messages", localMessages },
                { "stream", false }
            };
            string body = json.Serialize(payload);
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                try
                {
                    string respText = wc.UploadString(localApiUrl, "POST", body);
                    var resp = json.DeserializeObject(respText) as Dictionary<string, object>;
                    if (resp != null && resp.ContainsKey("message"))
                    {
                        var message = resp["message"] as Dictionary<string, object>;
                        if (message != null && message.ContainsKey("content"))
                        {
                            string content = Convert.ToString(message["content"]);
                            if (!string.IsNullOrWhiteSpace(content)) return content;
                        }
                    }
                    if (resp != null && resp.ContainsKey("response"))
                    {
                        string content = Convert.ToString(resp["response"]);
                        if (!string.IsNullOrWhiteSpace(content)) return content;
                    }
                    throw new Exception("Local API returned no text: " + respText);
                }
                catch (WebException ex)
                {
                    string detail = "";
                    if (ex.Response != null)
                    {
                        using (var stream = ex.Response.GetResponseStream())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                            detail = reader.ReadToEnd();
                    }
                    throw new Exception("Local API error for model '" + localModel + "': " + ex.Message + (detail.Length > 0 ? "\n" + detail : ""));
                }
            }
        }

        string PartsToText(object partsValue)
        {
            var parts = ToObjectList(partsValue);
            if (parts == null) return "";
            var sb = new StringBuilder();
            foreach (var partObj in parts)
            {
                var part = partObj as Dictionary<string, object>;
                if (part == null) continue;
                if (part.ContainsKey("text")) sb.AppendLine(Convert.ToString(part["text"]));
                if (part.ContainsKey("inlineData")) sb.AppendLine("[Binary image/PDF attachment omitted for OpenRouter text model. Use Gemini provider for visual/PDF analysis.]");
            }
            return sb.ToString().Trim();
        }

        string ExtractOpenRouterReply(Dictionary<string, object> resp)
        {
            if (resp == null) throw new Exception("OpenRouter returned empty response.");
            if (!resp.ContainsKey("choices")) throw new Exception("OpenRouter returned no choices: " + json.Serialize(resp));
            var choices = ToObjectList(resp["choices"]);
            if (choices == null || choices.Count == 0) throw new Exception("OpenRouter returned no choices: " + json.Serialize(resp));
            var choice = choices[0] as Dictionary<string, object>;
            var message = choice != null && choice.ContainsKey("message") ? choice["message"] as Dictionary<string, object> : null;
            if (message != null && message.ContainsKey("content"))
            {
                string content = Convert.ToString(message["content"]);
                if (!string.IsNullOrWhiteSpace(content)) return content;
            }
            throw new Exception("OpenRouter returned no text: " + json.Serialize(resp));
        }

        string PostGemini(string candidateModel, string body)
        {
            string url = "https://generativelanguage.googleapis.com/v1beta/models/" + Uri.EscapeDataString(candidateModel) + ":generateContent?key=" + Uri.EscapeDataString(apiKey);
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                try
                {
                    return wc.UploadString(url, "POST", body);
                }
                catch (WebException ex)
                {
                    string detail = "";
                    if (ex.Response != null)
                    {
                        using (var stream = ex.Response.GetResponseStream())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            detail = reader.ReadToEnd();
                        }
                    }
                    var httpResp = ex.Response as HttpWebResponse;
                    if (httpResp != null && (int)httpResp.StatusCode == 429)
                    {
                        string retry = ExtractRetryDelay(detail);
                        throw new Exception("Gemini quota/rate limit reached for model '" + candidateModel + "'. " + retry + "Open Settings later and keep model as gemini-flash-lite-latest, or wait for the free quota to reset.");
                    }
                    throw new Exception("Gemini API error for model '" + candidateModel + "': " + ex.Message + (detail.Length > 0 ? "\n" + detail : ""));
                }
            }
        }

        string ExtractRetryDelay(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail)) return "";
            var m = Regex.Match(detail, "\"retryDelay\"\\s*:\\s*\"([^\"]+)\"");
            if (m.Success) return "Retry after about " + m.Groups[1].Value + ". ";
            m = Regex.Match(detail, "Please retry in ([0-9.]+s)");
            if (m.Success) return "Retry after about " + m.Groups[1].Value + ". ";
            return "";
        }

        bool IsModelNotFoundError(string message)
        {
            if (message == null) return false;
            return message.Contains("(404)") || message.IndexOf("NOT_FOUND", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("no longer available", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        bool IsRetryableModelError(string message)
        {
            if (IsModelNotFoundError(message)) return true;
            if (message == null) return false;
            return message.Contains("(503)") || message.IndexOf("quota/rate limit", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("No candidates returned", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("No text response received", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        string ExtractReply(Dictionary<string, object> resp)
        {
            if (resp == null) throw new Exception("No candidates returned. Raw response is null.");
            if (!resp.ContainsKey("candidates")) throw new Exception("No candidates returned. Raw response: " + json.Serialize(resp));
            var candidates = ToObjectList(resp["candidates"]);
            if (candidates == null || candidates.Count == 0) throw new Exception("No candidates returned. Raw response: " + json.Serialize(resp));
            var cand = candidates[0] as Dictionary<string, object>;
            var content = cand != null && cand.ContainsKey("content") ? cand["content"] as Dictionary<string, object> : null;
            var parts = content != null && content.ContainsKey("parts") ? ToObjectList(content["parts"]) : null;
            if (parts == null) throw new Exception("No text response received. Raw response: " + json.Serialize(resp));
            var sb = new StringBuilder();
            foreach (var partObj in parts)
            {
                var part = partObj as Dictionary<string, object>;
                if (part != null && part.ContainsKey("text")) sb.Append(Convert.ToString(part["text"]));
            }
            if (sb.Length == 0) throw new Exception("No text response received. Raw response: " + json.Serialize(resp));
            return sb.ToString();
        }

        List<object> ToObjectList(object value)
        {
            if (value == null) return null;
            var list = new List<object>();
            var arrayList = value as ArrayList;
            if (arrayList != null)
            {
                foreach (var item in arrayList) list.Add(item);
                return list;
            }
            var objectArray = value as object[];
            if (objectArray != null)
            {
                foreach (var item in objectArray) list.Add(item);
                return list;
            }
            var enumerable = value as IEnumerable;
            if (enumerable != null && !(value is string))
            {
                foreach (var item in enumerable) list.Add(item);
                return list;
            }
            return null;
        }

        Dictionary<string, object> NewTextPart(string text)
        {
            return new Dictionary<string, object> { { "text", text } };
        }

        Dictionary<string, object> NewInlinePart(string path)
        {
            return new Dictionary<string, object>
            {
                {
                    "inlineData",
                    new Dictionary<string, object>
                    {
                        { "mimeType", GetMimeType(path) },
                        { "data", Convert.ToBase64String(File.ReadAllBytes(path)) }
                    }
                }
            };
        }

        string ExtractTextDocument(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".docx") return ExtractZipXml(path, "word/", null);
            if (ext == ".pptx") return ExtractPptx(path);
            if (ext == ".xlsx" || ext == ".xlsm") return ExtractXlsx(path);
            if (ext == ".doc") return "Old .doc is not supported yet. Save as .docx and upload again.";
            if (ext == ".xls") return "Old .xls is not supported yet. Save as .xlsx and upload again.";
            if (ext == ".ppt") return "Old .ppt is not supported yet. Save as .pptx and upload again.";
            return ReadText(path);
        }

        string ExtractZipXml(string path, string prefix, string contains)
        {
            var sb = new StringBuilder();
            using (var zip = ZipFile.OpenRead(path))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                    if (contains != null && entry.FullName.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    using (var sr = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        string text = ExtractXmlText(sr.ReadToEnd());
                        if (!string.IsNullOrWhiteSpace(text)) sb.AppendLine(text).AppendLine();
                    }
                }
            }
            return sb.ToString();
        }

        string ExtractPptx(string path)
        {
            var sb = new StringBuilder();
            int i = 1;
            using (var zip = ZipFile.OpenRead(path))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) || !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var sr = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        string text = ExtractXmlText(sr.ReadToEnd());
                        if (!string.IsNullOrWhiteSpace(text)) sb.AppendLine("[Slide " + i++ + "]").AppendLine(text).AppendLine();
                    }
                }
            }
            return sb.ToString();
        }

        string ExtractXlsx(string path)
        {
            var shared = new List<string>();
            var sb = new StringBuilder();
            using (var zip = ZipFile.OpenRead(path))
            {
                var sharedEntry = zip.GetEntry("xl/sharedStrings.xml");
                if (sharedEntry != null)
                {
                    using (var sr = new StreamReader(sharedEntry.Open(), Encoding.UTF8))
                    {
                        shared.AddRange(ExtractXmlText(sr.ReadToEnd()).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
                    }
                }

                int sheetNo = 1;
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) || !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var sr = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        string xml = sr.ReadToEnd();
                        sb.AppendLine("[Sheet " + sheetNo++ + "]");
                        int rowCount = 0;
                        foreach (Match rm in Regex.Matches(xml, "<row[^>]*>(.*?)</row>", RegexOptions.Singleline))
                        {
                            var cells = new List<string>();
                            foreach (Match cm in Regex.Matches(rm.Groups[1].Value, "<c([^>]*)>(.*?)</c>", RegexOptions.Singleline))
                            {
                                string attrs = cm.Groups[1].Value;
                                string body = cm.Groups[2].Value;
                                string v = "";
                                var vm = Regex.Match(body, "<v>(.*?)</v>", RegexOptions.Singleline);
                                if (vm.Success) v = WebUtility.HtmlDecode(vm.Groups[1].Value);
                                if (attrs.Contains("t=\"s\"") && Regex.IsMatch(v, "^\\d+$"))
                                {
                                    int idx = int.Parse(v);
                                    if (idx >= 0 && idx < shared.Count) v = shared[idx];
                                }
                                if (body.Contains("<is>")) v = ExtractXmlText(body);
                                cells.Add(v);
                            }
                            if (string.Join("", cells.ToArray()).Trim().Length > 0) sb.AppendLine(string.Join("\t", cells.ToArray()));
                            if (++rowCount >= 120) { sb.AppendLine("[sheet truncated]"); break; }
                        }
                        sb.AppendLine();
                    }
                }
            }
            return sb.ToString();
        }

        string ExtractXmlText(string xml)
        {
            var items = new List<string>();
            foreach (Match m in Regex.Matches(xml, "<[^>/]*:?t[^>]*>(.*?)</[^>]*:?t>", RegexOptions.Singleline))
            {
                string value = WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "")).Trim();
                if (value.Length > 0) items.Add(value);
            }
            return string.Join("\r\n", items.ToArray());
        }

        string ReadText(string path)
        {
            try { return File.ReadAllText(path, new UTF8Encoding(true)); }
            catch (DecoderFallbackException) { return File.ReadAllText(path, Encoding.Default); }
        }

        string TrimText(string text)
        {
            if (text == null) return "";
            if (text.Length <= MaxExtractedChars) return text;
            return text.Substring(0, MaxExtractedChars) + "\n\n[content truncated]";
        }

        string TrimForPrompt(string text, int maxChars)
        {
            if (text == null) return "";
            if (text.Length <= maxChars) return text;
            return text.Substring(0, maxChars) + "\n\n[content truncated]";
        }

        string ApplyRedaction(string text)
        {
            redactor.Enabled = redactSensitive;
            return redactor.Apply(text);
        }

        string ForceRedaction(string text)
        {
            return new Documents.Redactor(true).Apply(text);
        }

        void LogAction(string action, string detail)
        {
            try
            {
                Directory.CreateDirectory(configDir);
                string line = DateTime.Now.ToString("o") + "\t" + action + "\t" + (detail ?? "").Replace("\r", " ").Replace("\n", " ") + "\r\n";
                File.AppendAllText(auditLogPath, line, Encoding.UTF8);
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("LogAction: " + _ex.Message); }
        }

        void RecordAction(string type, string status, string detail, string outputPath)
        {
            try
            {
                Directory.CreateDirectory(configDir);
                var row = new Dictionary<string, object>
                {
                    { "actionId", Guid.NewGuid().ToString("N") },
                    { "at", DateTime.Now.ToString("o") },
                    { "taskId", currentTaskId ?? "" },
                    { "taskTitle", currentTaskTitle ?? "" },
                    { "taskStatus", currentTaskStatus ?? "" },
                    { "type", type ?? "" },
                    { "status", status ?? "" },
                    { "detail", detail ?? "" },
                    { "outputPath", outputPath ?? "" }
                };
                File.AppendAllText(actionLogPath, json.Serialize(row) + "\r\n", Encoding.UTF8);
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("RecordAction: " + _ex.Message); }
        }

        string QuoteArg(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        string CleanPath(string path)
        {
            if (path == null) return "";
            path = path.Trim().Trim('"').Trim('\'');
            if (path.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                path = Uri.UnescapeDataString(path.Substring(8)).Replace("/", "\\");
            return path;
        }

        string GetMimeType(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".png") return "image/png";
            if (ext == ".jpg" || ext == ".jpeg") return "image/jpeg";
            if (ext == ".gif") return "image/gif";
            if (ext == ".webp") return "image/webp";
            if (ext == ".bmp") return "image/bmp";
            if (ext == ".pdf") return "application/pdf";
            return "application/octet-stream";
        }

        string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("N1") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("N1") + " MB";
        }

        void RefreshAttachLabel()
        {
            string prefix = Tr("Mode: ", "模式：", "模式：") + ModeDisplayName(workMode) + " | ";
            if (pendingLabels.Count == 0)
                attachLabel.Text = prefix + Tr("No file loaded. Blue=You, Green=ZhuaQian, Red=Error. Supports images, PDF, Word, PPT, Excel, Markdown, TXT, CSV, JSON.",
                                      "未加载文件。蓝色=你，绿色=抓钱，红色=错误。支持图片、PDF、Word、PPT、Excel、Markdown、TXT、CSV、JSON。",
                                      "未載入檔案。藍色=你，綠色=抓錢，紅色=錯誤。支援圖片、PDF、Word、PPT、Excel、Markdown、TXT、CSV、JSON。");
            else
                attachLabel.Text = prefix + Tr("Loaded: ", "已加载：", "已載入：") + string.Join("; ", pendingLabels.ToArray());
        }

        ContextMenuStrip BuildChatContextMenu()
        {
            var menu = new ContextMenuStrip();
            var copyItem = new ToolStripMenuItem(Tr("Copy", "复制", "複製"));
            copyItem.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(chat.SelectedText))
                    try { Clipboard.SetText(chat.SelectedText); } catch (Exception _ex) { LogAction("Warning", "Clipboard: " + _ex.Message); }
            };
            var copyAllItem = new ToolStripMenuItem(Tr("Copy all", "复制全部", "複製全部"));
            copyAllItem.Click += (s, e) =>
            {
                try { Clipboard.SetText(chat.Text); } catch (Exception _ex) { LogAction("Warning", "ClipboardAll: " + _ex.Message); }
            };
            var saveItem = new ToolStripMenuItem(Tr("Save last reply", "保存最后回复", "儲存最後回覆"));
            saveItem.Click += (s, e) => SaveLastReplyAsFile(false, "");
            var saveAllItem = new ToolStripMenuItem(Tr("Export chat", "导出聊天", "匯出聊天"));
            saveAllItem.Click += (s, e) => ExportCurrentChat();
            menu.Items.AddRange(new ToolStripItem[] { copyItem, copyAllItem, saveItem, saveAllItem });
            return menu;
        }

        Font boldFont;
        void AppendChat(string speaker, string text, Color color)
        {
            Color bg = Color.White;
            if (speaker == "You") bg = Color.FromArgb(232, 243, 255);
            else if (speaker == "ZhuaQian") bg = Color.FromArgb(232, 248, 239);
            else if (speaker == "Error") bg = Color.FromArgb(255, 235, 235);

            if (boldFont == null || boldFont.FontFamily != chat.Font.FontFamily || boldFont.Size != chat.Font.Size)
            {
                if (boldFont != null) boldFont.Dispose();
                boldFont = new Font(chat.Font, FontStyle.Bold);
            }

            chat.SelectionStart = chat.TextLength;
            chat.SelectionBackColor = bg;
            chat.SelectionColor = color;
            chat.SelectionFont = boldFont;
            chat.AppendText("  " + speaker + "\r\n");
            chat.SelectionColor = Color.FromArgb(30, 30, 30);
            chat.SelectionFont = chat.Font;
            chat.AppendText("  " + text.Replace("\n", "\n  ") + "\r\n");
            chat.SelectionBackColor = Color.White;
            chat.AppendText("\r\n");
            chat.ScrollToCaret();
        }

        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyHotkeyRegistration();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (hotkeyRegistered)
            {
                UnregisterHotKey(Handle, HotkeyId);
                hotkeyRegistered = false;
            }
            base.OnHandleDestroyed(e);
        }

        void ApplyHotkeyRegistration()
        {
            if (!IsHandleCreated) return;
            if (hotkeyRegistered)
            {
                UnregisterHotKey(Handle, HotkeyId);
                hotkeyRegistered = false;
            }
            if (enableHotkey)
                hotkeyRegistered = RegisterHotKey(Handle, HotkeyId, ModAlt, (uint)Keys.Space);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
            {
                ToggleWindowVisible();
                return;
            }
            base.WndProc(ref m);
        }

        void ToggleWindowVisible()
        {
            if (WindowState == FormWindowState.Minimized || !Visible)
            {
                CaptureActiveWindowContext();
                Show();
                WindowState = FormWindowState.Normal;
                Activate();
                if (!string.IsNullOrWhiteSpace(lastContextTitle))
                {
                    input.Text = Tr("Context captured from: ", "已捕获上下文：", "已擷取上下文：") + lastContextTitle;
                    AppendChat("ZhuaQian", Tr("Hotkey captured the active window. Open Tools > Use Current Context to attach details, or ask directly.",
                                               "快捷键已捕获当前窗口。可打开 工具 > 使用当前窗口上下文 附加详情，也可以直接提问。",
                                               "快捷鍵已擷取目前視窗。可開啟 工具 > 使用目前視窗上下文 附加詳情，也可以直接提問。"), Color.FromArgb(0, 130, 80));
                }
                input.Focus();
            }
            else
            {
                Hide();
            }
        }

        void CaptureActiveWindowContext()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero || hwnd == Handle) return;
                var title = new StringBuilder(512);
                GetWindowText(hwnd, title, title.Capacity);
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                string processName = "";
                try
                {
                    var proc = Process.GetProcessById((int)pid);
                    processName = proc.ProcessName;
                }
                catch (Exception _ex) { LogAction("Warning", "GetProcess: " + _ex.Message); }
                lastContextTitle = title.ToString();
                lastContextProcess = processName;
                lastContextAt = DateTime.Now;
            }
            catch (Exception _ex) { LogAction("Warning", "CaptureContext: " + _ex.Message); }
        }

        [STAThread]
        public static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "ZhuaQian startup error");
            }
        }
    }
}
