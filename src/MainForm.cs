using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp
{
    public class MainForm : Form
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
        readonly Core.PermissionGate permissionGate = new Core.PermissionGate();
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
        bool permAutomationInput = false;
        bool useStreaming = false;
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
        string currentTaskId = "";
        string currentTaskTitle = "New task";
        string currentTaskStatus = "draft";
        string currentTaskLastAction = "";

        RichTextBox chat;
        TextBox input;
        Label attachLabel;
        Label modelLabel;
        Label currentTaskLabel;
        ListBox taskList;
        TextBox taskSearchBox;
        Button sendButton;
        Button uploadButton;
        Button saveTxtButton;
        Button clipboardButton;
        Button powerButton;
        ComboBox modeCombo;
        Button sidebarToggleButton;
        SplitContainer mainSplit;
        Panel sidePanel;
        Panel rightPanel;
        Panel bottomPanel;
        Timer clipboardTimer;
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

            mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                SplitterWidth = 4,
                Panel1MinSize = 44,
                IsSplitterFixed = false
            };
            Controls.Add(mainSplit);

            sidePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(246, 248, 251) };
            rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            mainSplit.Panel1.Controls.Add(sidePanel);
            mainSplit.Panel2.Controls.Add(rightPanel);

            var sideTitle = new Label
            {
                Text = "ZhuaQian",
                Font = new Font("Microsoft YaHei UI", 15, FontStyle.Bold),
                Location = new Point(14, 14),
                AutoSize = true
            };
            sidePanel.Controls.Add(sideTitle);

            sidebarToggleButton = new Button
            {
                Text = "<",
                Location = new Point(210, 14),
                Size = new Size(28, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            sidebarToggleButton.Click += (s, e) => ToggleSidebar();
            sidePanel.Controls.Add(sidebarToggleButton);
            sidebarToggleButton.BringToFront();

            var newTaskButton = new Button
            {
                Text = Tr("+ New Task", "+ 新任务", "+ 新任務"),
                Location = new Point(14, 54),
                Size = new Size(220, 34),
                Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold)
            };
            newTaskButton.Click += (s, e) => CreateNewTask();
            sidePanel.Controls.Add(newTaskButton);

            taskSearchBox = new TextBox
            {
                Location = new Point(14, 100),
                Size = new Size(220, 28)
            };
            taskSearchBox.TextChanged += (s, e) => RefreshTaskList();
            sidePanel.Controls.Add(taskSearchBox);

            taskList = new ListBox
            {
                Location = new Point(14, 138),
                Size = new Size(220, 260),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(246, 248, 251),
                Font = new Font("Microsoft YaHei UI", 10)
            };
            taskList.SelectedIndexChanged += (s, e) =>
            {
                if (suppressTaskSelection) return;
                var item = taskList.SelectedItem as TaskInfo;
                if (item != null && item.Id != currentTaskId) LoadTask(item.Id);
            };
            sidePanel.Controls.Add(taskList);

            var screenButton = new Button
            {
                Text = Tr("Screenshot OCR", "截图识别", "截圖識別"),
                Location = new Point(14, 410),
                Size = new Size(220, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            screenButton.Click += (s, e) => CaptureScreenForOcr();
            sidePanel.Controls.Add(screenButton);

            clipboardButton = new Button
            {
                Text = Tr("Clipboard: Off", "剪贴板：关", "剪貼簿：關"),
                Location = new Point(14, 446),
                Size = new Size(220, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            clipboardButton.Click += (s, e) => ToggleClipboardMonitor();
            sidePanel.Controls.Add(clipboardButton);

            var indexButton = new Button
            {
                Text = Tr("Index Folder", "索引文件夹", "索引資料夾"),
                Location = new Point(14, 482),
                Size = new Size(105, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            indexButton.Click += (s, e) => IndexFolder();
            sidePanel.Controls.Add(indexButton);

            var searchButton = new Button
            {
                Text = Tr("Search KB", "搜索知识库", "搜尋知識庫"),
                Location = new Point(129, 482),
                Size = new Size(105, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            searchButton.Click += (s, e) => SearchKnowledge();
            sidePanel.Controls.Add(searchButton);

            var batchButton = new Button
            {
                Text = Tr("Batch Report", "批量报告", "批次報告"),
                Location = new Point(14, 518),
                Size = new Size(105, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            batchButton.Click += async (s, e) => await RunBatchFiles();
            sidePanel.Controls.Add(batchButton);

            var pluginButton = new Button
            {
                Text = Tr("Run Plugin", "运行插件", "執行外掛"),
                Location = new Point(129, 518),
                Size = new Size(105, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            pluginButton.Click += (s, e) => RunPlugin();
            sidePanel.Controls.Add(pluginButton);

            var sideSettingsButton = new Button
            {
                Text = Tr("Settings", "设置", "設定"),
                Location = new Point(14, 590),
                Size = new Size(105, 32),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            sideSettingsButton.Click += (s, e) => ShowSettings();
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
                taskList.Height = Math.Max(130, sidePanel.ClientSize.Height - 390);
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

            var topPanel = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Color.FromArgb(245, 247, 250) };
            rightPanel.Controls.Add(topPanel);

            var title = new Label
            {
                Text = Tr("Task", "任务", "任務"),
                Font = new Font("Microsoft YaHei UI", 14, FontStyle.Bold),
                Location = new Point(14, 10),
                AutoSize = true
            };
            topPanel.Controls.Add(title);
        }
    }
}
