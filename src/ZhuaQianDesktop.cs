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

        void SaveLastReplyToTxt(bool automatic)
        {
            string text = GetLastModelReply();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this,
                    Tr("No AI reply to save yet.", "还没有可保存的 AI 回复。", "還沒有可儲存的 AI 回覆。"),
                    "Save TXT");
                return;
            }
            SaveTextToTxt(text, automatic);
        }

        bool SaveTextToTxt(string text, bool automatic)
        {
            if (!EnsurePermission(Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"), permFileWrite, false, "Save TXT")) return false;
            if (automatic)
            {
                string path = BuildAutoExportPath("txt");
                var result = RunExportFilePipeline("txt", path, text);
                if (result.Status != CommandStatus.Success)
                {
                    RecordAction("ExportFile", "failed", result.ErrorMessage ?? "Export failed", path);
                    return false;
                }
                SetCurrentTaskStatus("ready_for_review", "Generated TXT", true);
                AppendChat("ZhuaQian",
                    Tr("TXT file generated:\r\n", "TXT 文件已生成：\r\n", "TXT 檔案已產生：\r\n") + path,
                    Color.FromArgb(0, 130, 80));
                return true;
            }
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = Tr("Save reply as TXT", "保存回复为 TXT", "儲存回覆為 TXT");
                sfd.Filter = "Text file|*.txt|All files|*.*";
                string safeTitle = BuildExportFileBaseName();
                sfd.FileName = safeTitle + ".txt";
                if (sfd.ShowDialog(this) != DialogResult.OK) return false;
                File.WriteAllText(sfd.FileName, text, new UTF8Encoding(false));
                LogAction("SaveTxt", "Saved reply to " + sfd.FileName);
                SetCurrentTaskStatus("ready_for_review", "Saved TXT", true);
                RecordAction("SaveTxt", "success", "Saved reply text", sfd.FileName);
                AppendChat("ZhuaQian",
                    Tr("TXT file saved:\r\n", "TXT 文件已保存：\r\n", "TXT 檔案已儲存：\r\n") + sfd.FileName,
                    Color.FromArgb(0, 130, 80));
                return true;
            }
        }

        bool WantsTxtFile(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string value = text.ToLowerInvariant();
            return value.Contains(".txt")
                || value.Contains("txt文件")
                || value.Contains("txt 文件")
                || value.Contains("txt檔")
                || value.Contains("txt 檔")
                || value.Contains("保存为txt")
                || value.Contains("保存成txt")
                || value.Contains("生成txt")
                || value.Contains("生成 txt")
                || (value.Contains("txt") && (value.Contains("\u751f\u6210") || value.Contains("\u521b\u5efa") || value.Contains("\u5275\u5efa") || value.Contains("\u4fdd\u5b58") || value.Contains("\u5bfc\u51fa") || value.Contains("\u684c\u9762")))
                || value.Contains("导出txt")
                || value.Contains("匯出txt")
                || value.Contains("save as txt")
                || value.Contains("export txt")
                || value.Contains("create a txt")
                || value.Contains("generate a txt");
        }

        void SaveLastReplyAsFile(bool automatic, string requestedFormat)
        {
            string text = GetLastModelReply();
            if (string.IsNullOrWhiteSpace(text))
            {
                text = chat != null ? chat.SelectedText : "";
                if (string.IsNullOrWhiteSpace(text)) text = input != null ? input.Text.Trim() : "";
                if (string.IsNullOrWhiteSpace(text))
                {
                    MessageBox.Show(this,
                        Tr("No AI reply, selected chat text, or input text to save yet.",
                           "还没有可保存的 AI 回复、选中文本或输入框内容。",
                           "還沒有可儲存的 AI 回覆、選取文字或輸入框內容。"),
                        Tr("Save File", "保存文件", "儲存檔案"));
                    return;
                }
            }

            string format = string.IsNullOrWhiteSpace(requestedFormat) ? PromptExportFormat() : NormalizeExportFormat(requestedFormat);
            if (string.IsNullOrWhiteSpace(format)) return;
            SaveTextAsFormat(text, format, automatic);
        }

        bool SaveTextAsFormat(string text, string format, bool automatic)
        {
            return SaveTextAsFormat(text, format, automatic, "");
        }

        bool SaveTextAsFormat(string text, string format, bool automatic, string requestedPath)
        {
            if (!EnsurePermission(Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"), permFileWrite, false, "Save File")) return false;
            format = NormalizeExportFormat(format);
            if (string.IsNullOrWhiteSpace(format)) format = "txt";
            string fileText = PrepareGeneratedFileContent(text, format);
            string upper = format.ToUpperInvariant();
            if (automatic)
            {
                string path = string.IsNullOrWhiteSpace(requestedPath) ? BuildAutoExportPath(format) : requestedPath;
                var result = RunExportFilePipeline(format, path, fileText);
                if (result.Status != CommandStatus.Success)
                {
                    RecordAction("ExportFile", "failed", result.ErrorMessage ?? "Export failed", path);
                    return false;
                }
                SetCurrentTaskStatus("ready_for_review", "Generated " + upper, true);
                RememberGeneratedFilePath(path);
                AppendChat("ZhuaQian", upper + Tr(" file generated:\r\n", " 文件已生成：\r\n", " 檔案已產生：\r\n") + path + "\r\n" + Tr("Folder: ", "文件夹：", "資料夾：") + Path.GetDirectoryName(path), Color.FromArgb(0, 130, 80));
                return true;
            }
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = Tr("Save reply as ", "保存回复为 ", "儲存回覆為 ") + upper;
                if (format == "docx") sfd.Filter = "Word document|*.docx|All files|*.*";
                else if (format == "pptx") sfd.Filter = "PowerPoint presentation|*.pptx|All files|*.*";
                else if (format == "xlsx") sfd.Filter = "Excel workbook|*.xlsx|All files|*.*";
                else if (format == "pdf") sfd.Filter = "PDF document|*.pdf|All files|*.*";
                else if (format == "png") sfd.Filter = "PNG image|*.png|All files|*.*";
                else if (format == "md") sfd.Filter = "Markdown file|*.md|All files|*.*";
                else sfd.Filter = "Text file|*.txt|All files|*.*";

                string safeTitle = BuildExportFileBaseName();
                sfd.FileName = safeTitle + "." + format;
                if (sfd.ShowDialog(this) != DialogResult.OK) return false;

                if (format == "docx") officeExporter.SaveDocx(sfd.FileName, fileText);
                else if (format == "pptx") officeExporter.SavePptx(sfd.FileName, fileText);
                else if (format == "xlsx") officeExporter.SaveXlsx(sfd.FileName, fileText);
                else if (format == "pdf") officeExporter.SavePdf(sfd.FileName, fileText);
                else if (format == "png") officeExporter.SavePng(sfd.FileName, fileText);
                else if (format == "md") officeExporter.SaveMd(sfd.FileName, fileText);
                else officeExporter.SaveTxt(sfd.FileName, fileText);

                LogAction("SaveFile", "Saved " + format + " reply to " + sfd.FileName);
                RecordExportHistory(format, sfd.FileName, fileText.Length);
                SetCurrentTaskStatus("ready_for_review", "Saved " + upper, true);
                RecordAction("SaveFile", "success", "Saved " + format + " reply", sfd.FileName);
                RememberGeneratedFilePath(sfd.FileName);
                AppendChat("ZhuaQian", upper + Tr(" file saved:\r\n", " 文件已保存：\r\n", " 檔案已儲存：\r\n") + sfd.FileName + "\r\n" + Tr("Folder: ", "文件夹：", "資料夾：") + Path.GetDirectoryName(sfd.FileName), Color.FromArgb(0, 130, 80));
                return true;
            }
        }

        void RememberGeneratedFilePath(string path)
        {
            lastGeneratedFilePath = path ?? "";
            if (openFolderButton == null) return;
            string dir = string.IsNullOrWhiteSpace(lastGeneratedFilePath) ? "" : Path.GetDirectoryName(lastGeneratedFilePath);
            openFolderButton.Enabled = !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir);
        }

        void OpenLastGeneratedFolder()
        {
            if (string.IsNullOrWhiteSpace(lastGeneratedFilePath))
            {
                MessageBox.Show(this, Tr("No generated file yet.", "还没有生成文件。", "還沒有產生檔案。"), Tr("Open Folder", "打开文件夹", "開啟資料夾"));
                return;
            }
            try
            {
                if (File.Exists(lastGeneratedFilePath))
                    Process.Start("explorer.exe", "/select," + QuoteArg(lastGeneratedFilePath));
                else
                {
                    string dir = Path.GetDirectoryName(lastGeneratedFilePath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                        Process.Start("explorer.exe", QuoteArg(dir));
                    else
                        MessageBox.Show(this, Tr("Folder not found:\r\n", "找不到文件夹：\r\n", "找不到資料夾：\r\n") + lastGeneratedFilePath, Tr("Open Folder", "打开文件夹", "開啟資料夾"));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Tr("Open Folder failed", "打开文件夹失败", "開啟資料夾失敗"));
            }
        }

        CommandResult RunExportFilePipeline(string format, string path, string text)
        {
            var exportGate = PermissionGate.FromJson(permGate.ToJson());
            exportGate.Set("permFileWrite", permFileWrite ? PermissionLevel.Allow : PermissionLevel.Deny);
            var pipeline = agentPipelineFactory.Create(exportGate, pluginDir, allowAdvancedPlugins);
            var args = new Dictionary<string, object>();
            args["format"] = NormalizeExportFormat(format);
            args["text"] = text ?? "";
            args["taskTitle"] = currentTaskTitle;
            var command = new AgentCommand("ExportFile", "permFileWrite", currentTaskId, path, "Generate " + (format ?? "txt") + " file", args);
            return pipeline.Run(command);
        }

        string BuildAutoExportPath(string format)
        {
            format = NormalizeExportFormat(format);
            if (string.IsNullOrWhiteSpace(format)) format = "txt";
            string dir = Path.Combine(configDir, "generated");
            Directory.CreateDirectory(dir);
            string safeTitle = BuildExportFileBaseName();
            string name = safeTitle + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "." + format;
            return UniquePath(Path.Combine(dir, name));
        }

        string BuildDesktopExportPath(string format)
        {
            format = NormalizeExportFormat(format);
            if (string.IsNullOrWhiteSpace(format)) format = "txt";
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(dir))
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
            Directory.CreateDirectory(dir);
            string safeTitle = BuildExportFileBaseName();
            string name = safeTitle + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "." + format;
            return UniquePath(Path.Combine(dir, name));
        }

        string BuildExportFileBaseName()
        {
            string value = !string.IsNullOrWhiteSpace(lastExportNameHint) ? lastExportNameHint : currentTaskTitle;
            value = SanitizeFileTitle(value);
            if (value.Length == 0 || string.Equals(value, "New task", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "ZhuaQian-reply", StringComparison.OrdinalIgnoreCase))
                value = "ZhuaQian-output";
            if (value.Length > 48) value = value.Substring(0, 48).Trim();
            return value.Length == 0 ? "ZhuaQian-output" : value;
        }

        string BuildExportNameHint(string prompt, string format)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return "";
            string value = prompt.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')[0].Trim();
            value = Regex.Replace(value, @"https?://\S+", "", RegexOptions.IgnoreCase).Trim();

            string[] markers = {
                "主题是", "主题：", "主题:", "標題是", "标题是", "标题：", "标题:",
                "功能是", "功能：", "功能:", "内容是", "内容：", "内容:",
                "做一个", "做一個", "写一个", "寫一個", "创建一个", "創建一個",
                "生成一个", "生成一個", "生成", "创建", "建立",
                "make", "build", "create", "generate"
            };
            int best = -1;
            string bestMarker = "";
            foreach (string marker in markers)
            {
                int idx = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && idx >= best)
                {
                    best = idx;
                    bestMarker = marker;
                }
            }
            if (best >= 0) value = value.Substring(best + bestMarker.Length).Trim();

            string fmt = NormalizeExportFormat(format);
            string[] noise = {
                "." + fmt, fmt, "到桌面", "保存到桌面", "生成到桌面", "桌面上", "桌面",
                "文件", "檔案", "脚本", "腳本", "代码", "程式碼", "源码", "原始碼",
                "网页", "網頁", "小网页", "小網頁", "一个", "一個", "一份", "一张", "一張",
                "帮我", "幫我", "请", "請", "保存", "导出", "匯出", "生成", "创建", "建立",
                "excel", "xlsx", "pptx", "ppt", "powerpoint", "word", "docx", "doc",
                "pdf", "png", "html", "txt", "markdown", "md",
                "file", "script", "code", "source", "desktop", "save", "export", "please"
            };
            foreach (string item in noise)
                if (!string.IsNullOrWhiteSpace(item)) value = value.Replace(item, "");

            value = value.Trim(' ', '\t', '，', ',', '。', '.', '：', ':', '；', ';', '-', '_', '"', '\'', '“', '”', '‘', '’');
            value = SanitizeFileTitle(value);
            return value;
        }

        string SanitizeFileTitle(string value)
        {
            value = Regex.Replace(value ?? "", "[\\\\/:*?\"<>|]+", "_").Trim();
            value = Regex.Replace(value, "\\s+", " ").Trim();
            value = value.Trim('.', ' ', '_', '-');
            if (value.Length > 48) value = value.Substring(0, 48).Trim();
            return value;
        }

        string PromptExportFormat()
        {
            using (var form = new Form())
            using (var combo = new ComboBox())
            using (var ok = new Button())
            using (var cancel = new Button())
            using (var label = new Label())
            {
                form.Text = Tr("Save File", "保存文件", "儲存檔案");
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(300, 118);
                form.BackColor = zqPanelBg;
                label.Text = Tr("Choose output format:", "选择输出格式：", "選擇輸出格式：");
                label.SetBounds(16, 14, 260, 22);
                label.ForeColor = zqMuted;
                combo.DropDownStyle = ComboBoxStyle.DropDownList;
                combo.Items.Add("txt");
                combo.Items.Add("md");
                combo.Items.Add("docx");
                combo.Items.Add("pptx");
                combo.Items.Add("xlsx");
                combo.Items.Add("pdf");
                combo.Items.Add("png");
                combo.Items.Add("html");
                combo.Items.Add("py");
                combo.Items.Add("js");
                combo.Items.Add("cs");
                combo.Items.Add("ps1");
                combo.SelectedIndex = 0;
                combo.SetBounds(16, 42, 260, 24);
                ok.Text = Tr("OK", "确定", "確定");
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(118, 80, 74, 26);
                cancel.Text = Tr("Cancel", "取消", "取消");
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(202, 80, 74, 26);
                combo.BackColor = zqSurface;
                combo.ForeColor = zqInk;
                StyleButton(ok, ZqButtonRole.Primary);
                StyleButton(cancel, ZqButtonRole.Ghost);
                form.Controls.Add(label);
                form.Controls.Add(combo);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                if (form.ShowDialog(this) != DialogResult.OK) return "";
                return Convert.ToString(combo.SelectedItem);
            }
        }

        string DetectExportFormat(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string value = text.ToLowerInvariant();
            bool wantsFileOutput = value.Contains("generate") || value.Contains("create") || value.Contains("make ")
                || value.Contains("save") || value.Contains("export") || value.Contains("write") || value.Contains("output")
                || value.Contains("file")
                || value.Contains("\u751f\u6210") || value.Contains("\u521b\u5efa") || value.Contains("\u5275\u5efa")
                || value.Contains("\u5236\u4f5c") || value.Contains("\u4fdd\u5b58") || value.Contains("\u5bfc\u51fa")
                || value.Contains("\u532f\u51fa") || value.Contains("\u843d\u76d8")
                || value.Contains("\u6587\u4ef6") || value.Contains("\u6a94\u6848");
            bool explicitExtension = value.Contains(".pptx") || value.Contains(".ppt")
                || value.Contains(".xlsx") || value.Contains(".xls")
                || value.Contains(".docx") || value.Contains(".doc")
                || value.Contains(".pdf")
                || value.Contains(".png")
                || value.Contains(".html") || value.Contains(".css") || value.Contains(".js") || value.Contains(".ts")
                || value.Contains(".py") || value.Contains(".cs") || value.Contains(".ps1") || value.Contains(".bat") || value.Contains(".cmd")
                || value.Contains(".json") || value.Contains(".sql") || value.Contains(".yaml") || value.Contains(".yml")
                || value.Contains(".md")
                || value.Contains(".txt");
            if (!wantsFileOutput && !explicitExtension) return "";
            if (value.Contains(".html") || value.Contains("html") || value.Contains("\u7f51\u9875") || value.Contains("\u7db2\u9801")) return "html";
            if (value.Contains(".css") || value.Contains("css")) return "css";
            if (value.Contains(".ts") || value.Contains("typescript")) return "ts";
            if (value.Contains(".js") || value.Contains("javascript") || value.Contains("node.js")) return "js";
            if (value.Contains(".py") || value.Contains("python")) return "py";
            if (value.Contains(".cs") || value.Contains("c#") || value.Contains("csharp")) return "cs";
            if (value.Contains(".ps1") || value.Contains("powershell")) return "ps1";
            if (value.Contains(".bat") || value.Contains(".cmd") || value.Contains("\u6279\u5904\u7406") || value.Contains("\u6279\u8655\u7406")) return "bat";
            if (value.Contains(".json") || value.Contains("json")) return "json";
            if (value.Contains(".sql") || value.Contains("sql")) return "sql";
            if (value.Contains(".yaml") || value.Contains(".yml") || value.Contains("yaml")) return "yaml";
            if (value.Contains(".pdf") || value.Contains("pdf")) return "pdf";
            if (value.Contains(".png") || value.Contains("png")
                || value.Contains("\u56fe\u7247") || value.Contains("\u5716\u7247")
                || value.Contains("\u6d77\u62a5") || value.Contains("\u6d77\u5831")) return "png";
            if (value.Contains(".pptx") || value.Contains(".ppt") || value.Contains("ppt") || value.Contains("powerpoint")
                || value.Contains("\u5e7b\u706f\u7247") || value.Contains("\u5e7b\u71c8\u7247") || value.Contains("\u6f14\u793a\u6587\u7a3f")) return "pptx";
            if (value.Contains(".xlsx") || value.Contains(".xls") || value.Contains("excel")
                || value.Contains("\u8868\u683c") || value.Contains("\u5de5\u4f5c\u7c3f")) return "xlsx";
            if (value.Contains(".docx") || value.Contains(".doc") || value.Contains("word")
                || value.Contains("\u6587\u6863") || value.Contains("\u6587\u6a94")) return "docx";
            if (value.Contains(".md") || value.Contains("markdown")) return "md";
            if (WantsTxtFile(text)) return "txt";
            if (wantsFileOutput) return "txt";
            return "";
        }

        string DetectExportTargetPath(string text, string format)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(format)) return "";
            string value = text.ToLowerInvariant();
            bool wantsDesktop = value.Contains("desktop")
                || value.Contains("\u684c\u9762")
                || value.Contains("\u684c\u9762\u4e0a")
                || value.Contains("\u5230\u684c\u9762")
                || value.Contains("\u4fdd\u5b58\u5230\u684c\u9762")
                || value.Contains("\u751f\u6210\u5230\u684c\u9762");
            if (wantsDesktop) return BuildDesktopExportPath(format);
            return "";
        }

        string BuildFileGenerationInstruction(string format)
        {
            format = NormalizeExportFormat(format);
            if (string.IsNullOrWhiteSpace(format)) return "";
            string upper = format.ToUpperInvariant();
            return "The user asked for a real " + upper + " file. The desktop app will create and save the file locally after your reply. "
                + "Do not say that you cannot create files. Produce only the content that should go into the file. "
                + "For DOCX/MD/TXT use clean headings and paragraphs. For PPTX use slide titles and bullet points. "
                + "For XLSX use simple rows with columns separated by |, one row per line. "
                + "For PDF/PNG use a concise document layout with headings and short paragraphs. "
                + "For source code or scripts, output raw file content only, with no markdown code fences and no explanation.";
        }

        string NormalizeExportFormat(string format)
        {
            if (string.IsNullOrWhiteSpace(format)) return "";
            string value = format.Trim().TrimStart('.').ToLowerInvariant();
            if (value == "ppt") return "pptx";
            if (value == "doc") return "docx";
            if (value == "xls" || value == "xlsm") return "xlsx";
            if (value == "text") return "txt";
            if (value == "markdown") return "md";
            if (value == "jpeg" || value == "jpg") return "png";
            if (value == "javascript") return "js";
            if (value == "typescript") return "ts";
            if (value == "python") return "py";
            if (value == "powershell") return "ps1";
            if (value == "csharp" || value == "c#") return "cs";
            if (value == "yml") return "yaml";
            if (value == "txt" || value == "md" || value == "docx" || value == "pptx" || value == "xlsx" || value == "pdf" || value == "png"
                || value == "html" || value == "css" || value == "js" || value == "ts" || value == "py" || value == "cs" || value == "ps1"
                || value == "bat" || value == "cmd" || value == "json" || value == "xml" || value == "yaml" || value == "sql") return value;
            return "";
        }

        bool IsCodeExportFormat(string format)
        {
            format = NormalizeExportFormat(format);
            return format == "html" || format == "css" || format == "js" || format == "ts" || format == "py" || format == "cs"
                || format == "ps1" || format == "bat" || format == "cmd" || format == "json" || format == "xml" || format == "yaml" || format == "sql";
        }

        string PrepareGeneratedFileContent(string text, string format)
        {
            if (!IsCodeExportFormat(format)) return text ?? "";
            string value = (text ?? "").Trim();
            var match = Regex.Match(value, "^```[a-zA-Z0-9_#+.-]*\\s*\\r?\\n([\\s\\S]*?)\\r?\\n```\\s*$");
            if (match.Success) return match.Groups[1].Value.TrimEnd();
            return text ?? "";
        }

        void RecordExportHistory(string format, string path, int chars)
        {
            outputsHub.RecordExportHistory(format, path, chars, currentTaskId, currentTaskTitle);
        }

        void RecordOutput(string sourceAction, string type, string path, string taskId, string taskTitle, string sourceActionId, int sizeBytes)
        {
            outputsHub.RecordOutput(sourceAction, type, path, taskId, taskTitle, sourceActionId, sizeBytes);
        }


        string XmlEscape(string value)
        {
            return System.Security.SecurityElement.Escape(value ?? "") ?? "";
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

        void LoadConfig()
        {
            Directory.CreateDirectory(configDir);
            if (!File.Exists(configPath)) return;
            try
            {
                var cfg = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(configPath, Encoding.UTF8));
                if (cfg.ContainsKey("apiKeyProtected")) apiKey = UnprotectSecret(Convert.ToString(cfg["apiKeyProtected"]));
                else if (cfg.ContainsKey("apiKey")) apiKey = Convert.ToString(cfg["apiKey"]);
                if (cfg.ContainsKey("model")) model = Convert.ToString(cfg["model"]);
                if (cfg.ContainsKey("provider")) provider = Convert.ToString(cfg["provider"]);
                if (cfg.ContainsKey("openRouterApiKeyProtected")) openRouterApiKey = UnprotectSecret(Convert.ToString(cfg["openRouterApiKeyProtected"]));
                else if (cfg.ContainsKey("openRouterApiKey")) openRouterApiKey = Convert.ToString(cfg["openRouterApiKey"]);
                if (cfg.ContainsKey("openRouterModel")) openRouterModel = Convert.ToString(cfg["openRouterModel"]);
                if (cfg.ContainsKey("localApiUrl")) localApiUrl = Convert.ToString(cfg["localApiUrl"]);
                if (cfg.ContainsKey("localModel")) localModel = Convert.ToString(cfg["localModel"]);
                if (cfg.ContainsKey("embeddingModel")) embeddingModel = Convert.ToString(cfg["embeddingModel"]);
                if (cfg.ContainsKey("relayUrl")) relayUrl = Convert.ToString(cfg["relayUrl"]);
                if (cfg.ContainsKey("pluginDir")) pluginDir = Convert.ToString(cfg["pluginDir"]);
                if (cfg.ContainsKey("uiLanguage")) uiLanguage = Convert.ToString(cfg["uiLanguage"]);
                if (cfg.ContainsKey("workMode")) workMode = Convert.ToString(cfg["workMode"]);
                if (cfg.ContainsKey("enableHotkey")) enableHotkey = Convert.ToBoolean(cfg["enableHotkey"]);
                if (cfg.ContainsKey("computerControlEnabled")) computerControlEnabled = Convert.ToBoolean(cfg["computerControlEnabled"]);
                if (cfg.ContainsKey("allowAdvancedPlugins")) allowAdvancedPlugins = Convert.ToBoolean(cfg["allowAdvancedPlugins"]);
                if (cfg.ContainsKey("permFileRead")) permFileRead = Convert.ToBoolean(cfg["permFileRead"]);
                if (cfg.ContainsKey("permFileWrite")) permFileWrite = Convert.ToBoolean(cfg["permFileWrite"]);
                if (cfg.ContainsKey("permFileMoveDelete")) permFileMoveDelete = Convert.ToBoolean(cfg["permFileMoveDelete"]);
                if (cfg.ContainsKey("permProcessManage")) permProcessManage = Convert.ToBoolean(cfg["permProcessManage"]);
                if (cfg.ContainsKey("permPluginRun")) permPluginRun = Convert.ToBoolean(cfg["permPluginRun"]);
                if (cfg.ContainsKey("permScreenshot")) permScreenshot = Convert.ToBoolean(cfg["permScreenshot"]);
                if (cfg.ContainsKey("permClipboard")) permClipboard = Convert.ToBoolean(cfg["permClipboard"]);
                if (cfg.ContainsKey("permNetworkUpload")) permNetworkUpload = Convert.ToBoolean(cfg["permNetworkUpload"]);
                if (cfg.ContainsKey("permAutomationInput")) permAutomationInput = Convert.ToBoolean(cfg["permAutomationInput"]);
                if (cfg.ContainsKey("currentInfoMode")) currentInfoMode = Convert.ToBoolean(cfg["currentInfoMode"]);
                if (cfg.ContainsKey("redactSensitive")) redactSensitive = Convert.ToBoolean(cfg["redactSensitive"]);
                if (cfg.ContainsKey("autoMode")) autoMode = Convert.ToBoolean(cfg["autoMode"]);
                if (cfg.ContainsKey("allowedDirs"))
                {
                    allowedDirs.Clear();
                    foreach (var d in Convert.ToString(cfg["allowedDirs"]).Split('|'))
                        if (!string.IsNullOrWhiteSpace(d)) allowedDirs.Add(d);
                }

                // Build the gate baseline from the boolean switches, then load any
                // stored fine-grained gate (patterns / auto / dirs) on top.
                permGate.Set("permFileRead", permFileRead ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permFileWrite", permFileWrite ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permFileMoveDelete", permFileMoveDelete ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permProcessManage", permProcessManage ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permPluginRun", permPluginRun ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permScreenshot", permScreenshot ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permClipboard", permClipboard ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permNetworkUpload", permNetworkUpload ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.Set("permAutomationInput", permAutomationInput ? PermissionLevel.Allow : PermissionLevel.Deny);
                permGate.AutoMode = autoMode;
                permGate.AllowedDirectories = new List<string>(allowedDirs);

                if (cfg.ContainsKey("permGateJson"))
                {
                    var loaded = PermissionGate.FromJson(Convert.ToString(cfg["permGateJson"]));
                    if (loaded != null)
                    {
                        permGate = loaded;
                        autoMode = permGate.AutoMode;
                        allowedDirs = new List<string>(permGate.AllowedDirectories);
                    }
                }
                ApplyBooleanPermissionsToGate();
                if (string.IsNullOrWhiteSpace(model)) model = DefaultModel;
                if (string.IsNullOrWhiteSpace(provider)) provider = DefaultProvider;
                if (string.IsNullOrWhiteSpace(openRouterModel)) openRouterModel = DefaultOpenRouterModel;
                if (string.IsNullOrWhiteSpace(localApiUrl)) localApiUrl = DefaultLocalApiUrl;
                if (string.IsNullOrWhiteSpace(localModel)) localModel = DefaultLocalModel;
                if (string.IsNullOrWhiteSpace(embeddingModel)) embeddingModel = "nomic-embed-text";
                if (string.IsNullOrWhiteSpace(uiLanguage)) uiLanguage = "zh-Hans";
                if (string.IsNullOrWhiteSpace(workMode)) workMode = "Ask";

                // Sync legacy fields -> ProviderManager
                providerManager.GeminiKey = apiKey;
                providerManager.OpenRouterKey = openRouterApiKey;
                providerManager.LocalApiUrl = localApiUrl;
                providerManager.EmbeddingModel = embeddingModel;
                if (cfg.ContainsKey("tencentKeyProtected")) providerManager.TencentKey = UnprotectSecret(Convert.ToString(cfg["tencentKeyProtected"]));
                else if (cfg.ContainsKey("tencentKey")) providerManager.TencentKey = Convert.ToString(cfg["tencentKey"]);
                if (cfg.ContainsKey("alibabaKeyProtected")) providerManager.AlibabaKey = UnprotectSecret(Convert.ToString(cfg["alibabaKeyProtected"]));
                else if (cfg.ContainsKey("alibabaKey")) providerManager.AlibabaKey = Convert.ToString(cfg["alibabaKey"]);
                if (cfg.ContainsKey("zhipuKeyProtected")) providerManager.ZhipuKey = UnprotectSecret(Convert.ToString(cfg["zhipuKeyProtected"]));
                else if (cfg.ContainsKey("zhipuKey")) providerManager.ZhipuKey = Convert.ToString(cfg["zhipuKey"]);
                var found = ModelRegistry.ByProvider(provider).Find(m => m.Id == model);
                if (found != null) providerManager.SelectModel(found);
                else if (provider == "OpenRouter")
                {
                    found = ModelRegistry.ByProvider("OpenRouter").Find(m => m.Id == openRouterModel);
                    if (found != null) providerManager.SelectModel(found);
                }
                else if (provider == "Local")
                {
                    found = ModelRegistry.Local.Find(m => m.Id == localModel);
                    if (found != null) providerManager.SelectModel(found);
                }
            }
            catch (Exception _ex) { LogAction("Warning", "LoadConfig: " + _ex.Message); }
        }

        void NormalizeModel()
        {
            if (string.Equals(model, "gemini-2.5-flash-latest", StringComparison.OrdinalIgnoreCase))
            {
                model = DefaultModel;
                SaveConfig();
            }
            if (string.Equals(model, "gemini-2.5-flash", StringComparison.OrdinalIgnoreCase))
            {
                model = DefaultModel;
                SaveConfig();
            }
            if (string.Equals(model, "gemini-3.1-flash-lite", StringComparison.OrdinalIgnoreCase))
            {
                model = DefaultModel;
                SaveConfig();
            }
        }

        void SaveConfig()
        {
            Directory.CreateDirectory(configDir);
            ApplyBooleanPermissionsToGate();
            var cfg = new Dictionary<string, object> {
                { "apiKey", "" },
                { "apiKeyProtected", ProtectSecret(apiKey) },
                { "model", model },
                { "provider", provider },
                { "openRouterApiKey", "" },
                { "openRouterApiKeyProtected", ProtectSecret(openRouterApiKey) },
                { "openRouterModel", openRouterModel },
                { "tencentKey", "" },
                { "tencentKeyProtected", ProtectSecret(providerManager.TencentKey) },
                { "alibabaKey", "" },
                { "alibabaKeyProtected", ProtectSecret(providerManager.AlibabaKey) },
                { "zhipuKey", "" },
                { "zhipuKeyProtected", ProtectSecret(providerManager.ZhipuKey) },
                { "localApiUrl", localApiUrl },
                { "localModel", localModel },
                { "embeddingModel", embeddingModel },
                { "pluginDir", pluginDir },
                { "uiLanguage", uiLanguage },
                { "workMode", workMode },
                { "enableHotkey", enableHotkey },
                { "computerControlEnabled", computerControlEnabled },
                { "allowAdvancedPlugins", allowAdvancedPlugins },
                { "permFileRead", permFileRead },
                { "permFileWrite", permFileWrite },
                { "permFileMoveDelete", permFileMoveDelete },
                { "permProcessManage", permProcessManage },
                { "permPluginRun", permPluginRun },
                { "permScreenshot", permScreenshot },
                { "permClipboard", permClipboard },
                { "permNetworkUpload", permNetworkUpload },
                { "permAutomationInput", permAutomationInput },
                { "currentInfoMode", currentInfoMode },
                { "redactSensitive", redactSensitive },
                { "autoMode", autoMode },
                { "allowedDirs", string.Join("|", allowedDirs.ToArray()) },
                { "permGateJson", permGate.ToJson() },
                { "relayUrl", relayUrl }
            };
            File.WriteAllText(configPath, json.Serialize(cfg), Encoding.UTF8);
        }

        void ApplyBooleanPermissionsToGate()
        {
            permGate.Set("permFileRead", permFileRead ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permFileWrite", permFileWrite ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permFileMoveDelete", permFileMoveDelete ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permProcessManage", permProcessManage ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permPluginRun", permPluginRun ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permScreenshot", permScreenshot ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permClipboard", permClipboard ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permNetworkUpload", permNetworkUpload ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.Set("permAutomationInput", permAutomationInput ? PermissionLevel.Allow : PermissionLevel.Deny);
            permGate.AutoMode = autoMode;
            permGate.AllowedDirectories = new List<string>(allowedDirs);
        }

        string ProtectSecret(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try
            {
                byte[] raw = Encoding.UTF8.GetBytes(value);
                byte[] protectedBytes = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
                return "dpapi:" + Convert.ToBase64String(protectedBytes);
            }
            catch (Exception ex)
            {
                LogAction("Warning", "ProtectSecret failed: " + ex.Message);
                return "";
            }
        }

        string UnprotectSecret(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            try
            {
                if (!value.StartsWith("dpapi:", StringComparison.OrdinalIgnoreCase)) return value;
                byte[] protectedBytes = Convert.FromBase64String(value.Substring("dpapi:".Length));
                byte[] raw = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(raw);
            }
            catch (Exception ex)
            {
                LogAction("Warning", "UnprotectSecret failed: " + ex.Message);
                return "";
            }
        }

        string Tr(string en, string zhHans, string zhHant)
        {
            if (string.Equals(uiLanguage, "zh-Hant", StringComparison.OrdinalIgnoreCase)) return zhHant;
            if (string.Equals(uiLanguage, "en", StringComparison.OrdinalIgnoreCase)) return en;
            return zhHans;
        }

        string LanguageDisplay(string code)
        {
            if (string.Equals(code, "zh-Hant", StringComparison.OrdinalIgnoreCase)) return "繁體中文";
            if (string.Equals(code, "en", StringComparison.OrdinalIgnoreCase)) return "English";
            return "简体中文";
        }

        string LanguageCode(string display)
        {
            if (display == "繁體中文") return "zh-Hant";
            if (display == "English") return "en";
            return "zh-Hans";
        }

        string CurrentModelLabel()
        {
            return providerManager.CurrentModelLabel();
        }

        void ShowSettings()
        {
            using (var dlg = new SettingsDialog(providerManager, configPath, Tr, uiLanguage))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    string oldLanguage = uiLanguage;

                    var sel = dlg.SelectedModel;
                    if (sel != null)
                    {
                        provider = sel.Endpoint;
                        model = sel.Id;
                        if (sel.Endpoint == "Local") localModel = sel.Id;
                        providerManager.SelectModel(sel);
                    }

                    apiKey = dlg.GeminiKey;
                    openRouterApiKey = dlg.OpenRouterKey;
                    localApiUrl = dlg.LocalApiUrl;
                    embeddingModel = providerManager.EmbeddingModel;
                    uiLanguage = dlg.SelectedLanguage;
                    providerManager.GeminiKey = dlg.GeminiKey;
                    providerManager.OpenRouterKey = dlg.OpenRouterKey;
                    providerManager.LocalApiUrl = dlg.LocalApiUrl;
                    providerManager.CustomApiUrl = dlg.CustomApiUrl;
                    providerManager.CustomApiKey = dlg.CustomApiKey;
                    providerManager.TencentKey = dlg.TencentKey;
                    providerManager.AlibabaKey = dlg.AlibabaKey;
                    providerManager.ZhipuKey = dlg.ZhipuKey;

                    SaveConfig();
                    if (modelLabel != null) modelLabel.Text = CurrentModelLabel();
                    ApplyHotkeyRegistration();
                    if (!string.Equals(oldLanguage, uiLanguage, StringComparison.OrdinalIgnoreCase))
                        RebuildUiForLanguage();
                }
            }
        }

        void ShowPermissionSettings(IWin32Window owner)
        {
            using (var dlg = new Form())
            {
                dlg.Text = Tr("Permissions", "权限细分", "權限細分");
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Size = new Size(460, 440);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.Font = Font;
                dlg.BackColor = zqPanelBg;

                var intro = new Label
                {
                    Text = Tr("Power is the master switch. These switches decide what local actions are allowed after Power is on.",
                              "Power 是总开关。下面这些开关决定 Power 开启后允许哪些本地动作。",
                              "Power 是總開關。下面這些開關決定 Power 開啟後允許哪些本機動作。"),
                    Location = new Point(16, 16),
                    Size = new Size(410, 42),
                    ForeColor = zqMuted
                };
                var chkRead = new CheckBox { Text = Tr("Read local files", "读取本地文件", "讀取本機檔案"), Location = new Point(24, 70), Width = 380, Checked = permFileRead };
                var chkWrite = new CheckBox { Text = Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"), Location = new Point(24, 100), Width = 380, Checked = permFileWrite };
                var chkMove = new CheckBox { Text = Tr("Move/delete files", "移动/删除文件", "移動/刪除檔案"), Location = new Point(24, 130), Width = 380, Checked = permFileMoveDelete };
                var chkProc = new CheckBox { Text = Tr("End processes", "结束进程", "結束處理程序"), Location = new Point(24, 160), Width = 380, Checked = permProcessManage };
                var chkPlugin = new CheckBox { Text = Tr("Run plugins", "运行插件", "執行外掛"), Location = new Point(24, 190), Width = 380, Checked = permPluginRun };
                var chkShot = new CheckBox { Text = Tr("Take screenshots", "截屏识别", "截圖識別"), Location = new Point(24, 220), Width = 380, Checked = permScreenshot };
                var chkClip = new CheckBox { Text = Tr("Read clipboard monitor", "读取剪贴板监控", "讀取剪貼簿監控"), Location = new Point(24, 250), Width = 380, Checked = permClipboard };
                var chkNetwork = new CheckBox { Text = Tr("Cloud/network upload", "云端/网络上传", "雲端/網路上傳"), Location = new Point(24, 280), Width = 380, Checked = permNetworkUpload };
                var chkAutoInput = new CheckBox { Text = Tr("Automation/click/keyboard input", "自动化点击/键盘输入", "自動化點擊/鍵盤輸入"), Location = new Point(24, 310), Width = 380, Checked = permAutomationInput };
                var chkAutoMode = new CheckBox { Text = Tr("Auto mode (auto-approve Ask permissions)", "自动模式（自动批准 Ask 权限）", "自動模式（自動批准 Ask 權限）"), Location = new Point(24, 340), Width = 380, Checked = autoMode };
                var ok = new Button { Text = Tr("Save", "保存", "儲存"), Location = new Point(260, 385), Width = 76 };
                var cancel = new Button { Text = Tr("Cancel", "取消", "取消"), Location = new Point(348, 355), Width = 76 };
                StyleButton(ok, ZqButtonRole.Primary);
                StyleButton(cancel, ZqButtonRole.Ghost);
                ok.Click += (s, e) =>
                {
                    permFileRead = chkRead.Checked;
                    permFileWrite = chkWrite.Checked;
                    permFileMoveDelete = chkMove.Checked;
                    permProcessManage = chkProc.Checked;
                    permPluginRun = chkPlugin.Checked;
                    permScreenshot = chkShot.Checked;
                    permClipboard = chkClip.Checked;
                    permNetworkUpload = chkNetwork.Checked;
                    permAutomationInput = chkAutoInput.Checked;
                    autoMode = chkAutoMode.Checked;
                    permGate.AutoMode = autoMode;
                    SaveConfig();
                    LogAction("Permissions", "Updated fine-grained permissions");
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                };
                cancel.Click += (s, e) => dlg.Close();
                dlg.Controls.AddRange(new Control[] { intro, chkRead, chkWrite, chkMove, chkProc, chkPlugin, chkShot, chkClip, chkNetwork, chkAutoInput, chkAutoMode, ok, cancel });
                dlg.ShowDialog(owner);
            }
        }

        string TestGeminiConnection(string key, string testModel)
        {
            if (!permNetworkUpload) return "Cloud/network upload permission is off.";
            if (string.IsNullOrWhiteSpace(key)) return "Gemini API Key is empty.";
            try
            {
                var payload = new Dictionary<string, object>
                {
                    { "contents", new ArrayList { new Dictionary<string, object> {
                        { "role", "user" },
                        { "parts", new ArrayList { NewTextPart("Reply with OK.") } }
                    } } },
                    { "generationConfig", new Dictionary<string, object> { { "temperature", 0 }, { "maxOutputTokens", 16 } } }
                };
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    string url = "https://generativelanguage.googleapis.com/v1beta/models/" + Uri.EscapeDataString(testModel) + ":generateContent?key=" + Uri.EscapeDataString(key);
                    wc.UploadString(url, "POST", json.Serialize(payload));
                }
                return "Gemini connection OK.";
            }
            catch (Exception ex)
            {
                return "Gemini test failed:\r\n" + ShortError(ex);
            }
        }

        string TestOpenRouterConnection(string key, string testModel)
        {
            if (!permNetworkUpload) return "Cloud/network upload permission is off.";
            if (string.IsNullOrWhiteSpace(key)) return "OpenRouter API Key is empty.";
            try
            {
                var payload = new Dictionary<string, object> {
                    { "model", string.IsNullOrWhiteSpace(testModel) ? DefaultOpenRouterModel : testModel },
                    { "messages", new ArrayList {
                        new Dictionary<string, object> { { "role", "user" }, { "content", "Reply with OK." } }
                    } },
                    { "temperature", 0 },
                    { "max_tokens", 16 }
                };
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + key;
                    wc.Headers["HTTP-Referer"] = "https://zhuaqian.local";
                    wc.Headers["X-Title"] = "ZhuaQian Desktop";
                    wc.UploadString("https://openrouter.ai/api/v1/chat/completions", "POST", json.Serialize(payload));
                }
                return "OpenRouter connection OK.";
            }
            catch (Exception ex)
            {
                return "OpenRouter test failed:\r\n" + ShortError(ex);
            }
        }

        string TestLocalConnection(string url, string testModel)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(testModel)) return "Local URL/model is empty.";
            try
            {
                var payload = new Dictionary<string, object> {
                    { "model", testModel },
                    { "messages", new ArrayList {
                        new Dictionary<string, object> { { "role", "user" }, { "content", "Reply with OK." } }
                    } },
                    { "stream", false }
                };
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    wc.UploadString(url, "POST", json.Serialize(payload));
                }
                return "Local model connection OK.";
            }
            catch (Exception ex)
            {
                return "Local model test failed:\r\n" + ShortError(ex);
            }
        }

        string TestEmbeddingConnection(string chatUrl, string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return "Embedding model is empty.";
            try
            {
                string url = EmbeddingUrlFromChatUrl(string.IsNullOrWhiteSpace(chatUrl) ? DefaultLocalApiUrl : chatUrl);
                var payload = new Dictionary<string, object> {
                    { "model", modelName },
                    { "prompt", "ZhuaQian embedding test" }
                };
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    string resp = wc.UploadString(url, "POST", json.Serialize(payload));
                    if (!resp.Contains("embedding")) return "Embedding endpoint responded, but no embedding field was found.";
                }
                return "Embedding connection OK.";
            }
            catch (Exception ex)
            {
                return "Embedding test failed:\r\n" + ShortError(ex);
            }
        }

        string EmbeddingUrlFromChatUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "http://localhost:11434/api/embeddings";
            if (url.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
                return url.Substring(0, url.Length - "/api/chat".Length) + "/api/embeddings";
            if (url.EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase))
                return url.Substring(0, url.Length - "/api/generate".Length) + "/api/embeddings";
            return url.TrimEnd('/') + "/embeddings";
        }

        string ShortError(Exception ex)
        {
            var web = ex as WebException;
            if (web != null && web.Response != null)
            {
                try
                {
                    using (var stream = web.Response.GetResponseStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        return TrimForPrompt(reader.ReadToEnd(), 1200);
                }
                catch (Exception _ex) { LogAction("Warning", "ShortError: " + _ex.Message); }
            }
            return TrimForPrompt(ex.Message, 1200);
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

        bool TryRunLocalComputerCommand(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string trimmed = raw.Trim();
            if (!trimmed.StartsWith("/")) return false;

            var parsed = new Tools.CommandParser().Parse(trimmed);
            if (!parsed.IsCommand) return false;
            if (!string.IsNullOrWhiteSpace(parsed.Error))
            {
                AppendChat("Error", parsed.Error, Color.FromArgb(190, 40, 40));
                return true;
            }

            string verb = (parsed.Verb ?? "").ToLowerInvariant();
            if (verb == "help")
            {
                AppendChat("ZhuaQian",
                    "Local computer-control commands:\r\n" +
                    "  /open notepad\r\n" +
                    "  /open \"C:\\\\path\\\\file.txt\"\r\n" +
                    "  /save docx\r\n" +
                    "  /save format=pptx text=\"meeting outline\"\r\n" +
                    "  /type \"hello\"\r\n" +
                    "  /hotkey ctrl+v\r\n" +
                    "  /key enter\r\n" +
                    "  /click 300 450\r\n" +
                    "  /wait 1000",
                    Color.FromArgb(0, 130, 80));
                input.Clear();
                return true;
            }

            if (verb == "save" || verb == "file" || verb == "export")
            {
                RunSaveCommand(parsed);
                input.Clear();
                return true;
            }

            if (verb != "open" && verb != "type" && verb != "hotkey" && verb != "key" && verb != "click" && verb != "wait")
                return false;

            var parameters = new Dictionary<string, object>();
            string target = "";
            string summary = "";

            if (verb == "open")
            {
                target = FlagOrArgs(parsed, "target", "path", "url");
                parameters["action"] = "open";
                parameters["target"] = target;
                summary = "Open " + target;
            }
            else if (verb == "type")
            {
                target = FlagOrArgs(parsed, "text", "target", "");
                parameters["action"] = "type";
                parameters["text"] = target;
                summary = "Type text (" + target.Length + " chars)";
            }
            else if (verb == "hotkey")
            {
                target = FlagOrArgs(parsed, "sequence", "target", "");
                parameters["action"] = "hotkey";
                parameters["sequence"] = target;
                summary = "Send hotkey " + target;
            }
            else if (verb == "key")
            {
                target = FlagOrArgs(parsed, "key", "target", "");
                parameters["action"] = "key";
                parameters["key"] = target;
                summary = "Press key " + target;
            }
            else if (verb == "click")
            {
                string x = parsed.Flags.ContainsKey("x") ? parsed.Flags["x"] : (parsed.Args.Count > 0 ? parsed.Args[0] : "");
                string y = parsed.Flags.ContainsKey("y") ? parsed.Flags["y"] : (parsed.Args.Count > 1 ? parsed.Args[1] : "");
                parameters["action"] = "click";
                parameters["x"] = x;
                parameters["y"] = y;
                if (parsed.Flags.ContainsKey("button")) parameters["button"] = parsed.Flags["button"];
                target = x + "," + y;
                summary = "Click " + target;
            }
            else if (verb == "wait")
            {
                string ms = parsed.Flags.ContainsKey("ms") ? parsed.Flags["ms"] : (parsed.Args.Count > 0 ? parsed.Args[0] : "1000");
                parameters["action"] = "wait";
                parameters["ms"] = ms;
                target = ms + "ms";
                summary = "Wait " + target;
            }

            if (string.IsNullOrWhiteSpace(target) && verb != "wait")
            {
                AppendChat("Error", "Missing target for /" + verb + ". Type /help for examples.", Color.FromArgb(190, 40, 40));
                return true;
            }

            RunComputerControl(parameters, target, summary);
            input.Clear();
            return true;
        }

        bool TryRunNaturalLocalAction(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string text = raw.Trim();
            if (text.StartsWith("/")) return false;
            string lower = text.ToLowerInvariant();

            if (LooksLikeComputerDiagnosisRequest(lower))
            {
                string report = ForceRedaction(systemDiagnostics.BuildReport());
                if (!HasUsableProviderKey())
                {
                    AppendChat("ZhuaQian", report, Color.FromArgb(0, 130, 80));
                    input.Clear();
                    return true;
                }
                if (MayUseCloudProvider(null))
                {
                    if (!EnsurePermission(Tr("Cloud/network upload", "云端/网络上传", "雲端/網路上傳"), permNetworkUpload, false, "Upload diagnostics")) return true;
                    if (!ConfirmDiagnosticsCloudUpload(report)) return true;
                }
                pendingParts.Insert(0, NewTextPart("[Local computer diagnostics]\r\n" + report));
                return false;
            }

            if (LooksLikeEndProcessRequest(lower))
            {
                int pid;
                if (!TryExtractPid(text, out pid))
                {
                    AppendChat("Error", Tr("Missing PID. Example: ", "缺少 PID。示例：", "缺少 PID。範例：") + "结束 PID 1234", Color.FromArgb(190, 40, 40));
                    return true;
                }
                EndProcessByPid(pid);
                input.Clear();
                return true;
            }

            if (LooksLikePluginRequest(lower))
            {
                string path = ExtractNaturalTarget(text, new string[] { "运行插件", "执行插件", "執行外掛", "run plugin", "execute plugin" });
                path = CleanPath(path);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    AppendChat("Error", Tr("Plugin file not found. Example: ", "找不到插件文件。示例：", "找不到外掛檔案。範例：") + "运行插件 \"C:\\path\\tool.py\"", Color.FromArgb(190, 40, 40));
                    return true;
                }
                RunPluginPath(path, text);
                input.Clear();
                return true;
            }

            if (LooksLikeOrganizeFolderRequest(lower))
            {
                string path = ExtractNaturalTarget(text, new string[] { "整理文件夹", "整理資料夾", "整理文件夾", "整理目录", "整理目錄", "organize folder", "organise folder" });
                path = CleanPath(path);
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    AppendChat("Error", Tr("Folder not found. Example: ", "找不到文件夹。示例：", "找不到資料夾。範例：") + "整理文件夹 \"C:\\path\\folder\"", Color.FromArgb(190, 40, 40));
                    return true;
                }
                ExecuteOrganizeFolder(path);
                input.Clear();
                return true;
            }

            if (LooksLikeOpenRequest(lower))
            {
                string target = ExtractNaturalTarget(text, new string[] { "打开", "開啟", "启动", "啟動", "open", "launch" });
                target = NormalizeOpenTarget(target);
                if (string.IsNullOrWhiteSpace(target))
                {
                    AppendChat("Error", Tr("Missing open target. Example: ", "缺少打开目标。示例：", "缺少開啟目標。範例：") + "打开记事本 / 打开 \"C:\\path\\file.txt\"", Color.FromArgb(190, 40, 40));
                    return true;
                }

                var parameters = new Dictionary<string, object>();
                parameters["action"] = "open";
                parameters["target"] = target;
                RunComputerControl(parameters, target, "Open " + target);
                input.Clear();
                return true;
            }

            return false;
        }

        bool LooksLikeEndProcessRequest(string lower)
        {
            return ContainsAny(lower, "结束进程", "終止處理程序", "结束 pid", "終止 pid", "kill pid", "end process", "terminate process");
        }

        bool LooksLikeComputerDiagnosisRequest(string lower)
        {
            return ContainsAny(lower,
                "分析电脑", "分析電腦", "诊断电脑", "診斷電腦", "电脑很卡", "電腦很卡", "系统诊断", "系統診斷",
                "电脑问题", "電腦問題", "本机诊断", "本機診斷", "computer diagnostic", "diagnose computer",
                "analyze my computer", "slow computer", "system diagnostic", "what is wrong with my pc");
        }

        bool LooksLikePluginRequest(string lower)
        {
            return ContainsAny(lower, "运行插件", "执行插件", "執行外掛", "run plugin", "execute plugin")
                || (ContainsAny(lower, "运行", "执行", "run", "execute") && ContainsAny(lower, ".py", ".ps1", ".bat", ".cmd", ".exe"));
        }

        bool LooksLikeOrganizeFolderRequest(string lower)
        {
            return ContainsAny(lower, "整理文件夹", "整理文件夾", "整理資料夾", "整理目录", "整理目錄", "organize folder", "organise folder")
                || (ContainsAny(lower, "整理", "分类", "分類", "organize", "organise") && ContainsAny(lower, "文件夹", "文件夾", "資料夾", "目录", "目錄", "folder"));
        }

        bool LooksLikeOpenRequest(string lower)
        {
            return ContainsAny(lower, "打开", "開啟", "启动", "啟動", "open ", "launch ");
        }

        bool ContainsAny(string value, params string[] needles)
        {
            if (value == null) return false;
            foreach (string needle in needles)
                if (!string.IsNullOrEmpty(needle) && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        string ExtractNaturalTarget(string text, string[] verbs)
        {
            string quoted = ExtractQuotedText(text);
            if (!string.IsNullOrWhiteSpace(quoted)) return TrimNaturalTarget(quoted);

            var url = Regex.Match(text, @"https?://\S+", RegexOptions.IgnoreCase);
            if (url.Success) return TrimNaturalTarget(url.Value);

            var path = Regex.Match(text, @"[A-Za-z]:\\[^""'<>|\r\n]+");
            if (path.Success) return TrimNaturalTarget(path.Value);

            foreach (string verb in verbs)
            {
                int index = text.IndexOf(verb, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                    return TrimNaturalTarget(text.Substring(index + verb.Length));
            }
            return "";
        }

        string ExtractQuotedText(string text)
        {
            var match = Regex.Match(text, "\"([^\"]+)\"|'([^']+)'|“([^”]+)”|‘([^’]+)’");
            if (!match.Success) return "";
            for (int i = 1; i < match.Groups.Count; i++)
                if (match.Groups[i].Success) return match.Groups[i].Value;
            return "";
        }

        string TrimNaturalTarget(string target)
        {
            if (target == null) return "";
            target = target.Trim();
            target = target.Trim('"', '\'', '“', '”', '‘', '’', ' ', '\t', '\r', '\n', '，', '。', '；', ';');
            string[] prefixes = { "一下", "这个", "這個", "该", "該", "文件夹", "文件夾", "資料夾", "目录", "目錄", "文件" };
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (string prefix in prefixes)
                {
                    if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        target = target.Substring(prefix.Length).Trim();
                        changed = true;
                    }
                }
            }
            string[] stops = { " 这个", " 這個", " 文件夹", " 文件夾", " 資料夾", " 目录", " 目錄", " folder", " 然后", " 然後", " 并", " 並", "，", "。", "\r", "\n" };
            foreach (string stop in stops)
            {
                int index = target.IndexOf(stop, StringComparison.OrdinalIgnoreCase);
                if (index > 0) target = target.Substring(0, index).Trim();
            }
            return target.Trim('"', '\'', '“', '”', '‘', '’', ' ', '\t', '，', '。', '；', ';');
        }

        string NormalizeOpenTarget(string target)
        {
            target = TrimNaturalTarget(target);
            string lower = (target ?? "").ToLowerInvariant();
            if (lower == "记事本" || lower == "記事本") return "notepad";
            if (lower == "计算器" || lower == "計算機" || lower == "計算器") return "calc";
            if (lower == "画图" || lower == "小画家" || lower == "小畫家") return "mspaint";
            if (lower == "资源管理器" || lower == "檔案總管" || lower == "文件资源管理器") return "explorer";
            if (File.Exists(target) || Directory.Exists(target)) return CleanPath(target);
            return target;
        }

        bool TryExtractPid(string text, out int pid)
        {
            pid = 0;
            var match = Regex.Match(text, @"(?:pid|PID|进程|處理程序|process)\D{0,12}(\d{1,10})");
            if (!match.Success) match = Regex.Match(text, @"\b(\d{2,10})\b");
            return match.Success && int.TryParse(match.Groups[1].Value, out pid);
        }

        void RunSaveCommand(Tools.ParsedCommand parsed)
        {
            string format = "";
            if (parsed.Flags.ContainsKey("format")) format = parsed.Flags["format"];
            else if (parsed.Flags.ContainsKey("type")) format = parsed.Flags["type"];
            else if (parsed.Args.Count > 0) format = parsed.Args[0];
            format = NormalizeExportFormat(format);
            if (string.IsNullOrWhiteSpace(format)) format = "txt";

            string content = "";
            if (parsed.Flags.ContainsKey("text")) content = parsed.Flags["text"];
            else if (parsed.Flags.ContainsKey("content")) content = parsed.Flags["content"];
            else if (parsed.Args.Count > 1)
            {
                var tail = new List<string>();
                for (int i = 1; i < parsed.Args.Count; i++) tail.Add(parsed.Args[i]);
                content = string.Join(" ", tail.ToArray());
            }
            if (string.IsNullOrWhiteSpace(content)) content = GetLastModelReply();
            if (string.IsNullOrWhiteSpace(content) && chat != null) content = chat.SelectedText;
            if (string.IsNullOrWhiteSpace(content))
            {
                AppendChat("Error", "Nothing to save. Use /save docx \"content\" or ask the model first, then run /save docx.", Color.FromArgb(190, 40, 40));
                return;
            }

            if (!SaveTextAsFormat(content, format, true))
                AppendChat("Error", "File generation failed. Check write/export permission.", Color.FromArgb(190, 40, 40));
        }

        string FlagOrArgs(Tools.ParsedCommand parsed, string firstFlag, string secondFlag, string thirdFlag)
        {
            if (!string.IsNullOrEmpty(firstFlag) && parsed.Flags.ContainsKey(firstFlag)) return parsed.Flags[firstFlag];
            if (!string.IsNullOrEmpty(secondFlag) && parsed.Flags.ContainsKey(secondFlag)) return parsed.Flags[secondFlag];
            if (!string.IsNullOrEmpty(thirdFlag) && parsed.Flags.ContainsKey(thirdFlag)) return parsed.Flags[thirdFlag];
            return string.Join(" ", parsed.Args.ToArray()).Trim();
        }

        void RunComputerControl(Dictionary<string, object> parameters, string target, string summary)
        {
            if (!EnsurePermission(
                    Tr("Automation/click/keyboard input", "自动化点击/键盘输入", "自動化點擊/鍵盤輸入"),
                    permAutomationInput,
                    true,
                    "Computer Control"))
                return;

            var controlGate = PermissionGate.FromJson(permGate.ToJson());
            controlGate.Set("permAutomationInput", PermissionLevel.Ask);
            var pipeline = agentPipelineFactory.Create(controlGate, pluginDir, allowAdvancedPlugins);
            pipeline.RequestApproval = approvalCommand => ShowApprovalCard(
                "ComputerControl",
                Tr("Confirm computer control", "确认操控电脑", "確認操控電腦"),
                Tr("Execute", "执行", "執行"),
                Tr("Automation/click/keyboard input", "自动化点击/键盘输入", "自動化點擊/鍵盤輸入"),
                target,
                Tr("This action affects the active Windows desktop. Make sure the correct window is focused.",
                   "该动作会影响当前 Windows 桌面。请确认焦点窗口正确。",
                   "該動作會影響目前 Windows 桌面。請確認焦點視窗正確。"),
                summary,
                "Command: " + summary + "\r\nTarget: " + target,
                "");

            var controlCommand = new AgentCommand("ComputerControl", "permAutomationInput", currentTaskId, target, summary, parameters);
            var result = pipeline.Run(controlCommand);
            if (result.Status == CommandStatus.Success)
            {
                LogAction("ComputerControl", summary + " -> " + target);
                RecordAction("ComputerControl", "success", summary + " -> " + target, "");
                AppendChat("ZhuaQian", result.OutputText ?? "Computer control completed.", Color.FromArgb(0, 130, 80));
            }
            else if (result.Status == CommandStatus.Cancelled)
            {
                RecordAction("ComputerControl", "cancelled", summary + " -> " + target, "");
            }
            else if (result.Status == CommandStatus.Denied)
            {
                SetCurrentTaskStatus("needs_input", "Computer control denied", true);
                RecordAction("ComputerControl", "denied", result.ErrorMessage, "");
                MessageBox.Show(this, result.ErrorMessage, "Computer control denied");
            }
            else
            {
                SetCurrentTaskStatus("failed", "Computer control failed", true);
                RecordAction("ComputerControl", "failed", result.ErrorMessage, "");
                MessageBox.Show(this, result.ErrorMessage, "Computer control failed");
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
