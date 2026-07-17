using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ZhuaQianDesktopApp
{
    public class SettingsDialog : Form
    {
        readonly ProviderManager mgr;
        readonly string configPath;
        readonly Func<string, string, string, string> tr;
        readonly Dictionary<ModelInfo, RadioButton> modelRadios = new Dictionary<ModelInfo, RadioButton>();
        readonly Dictionary<ModelInfo, Panel> modelRows = new Dictionary<ModelInfo, Panel>();
        readonly Dictionary<ModelInfo, Label> modelStatusLabels = new Dictionary<ModelInfo, Label>();
        readonly Dictionary<string, string> modelTestStatus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ModelInfo selectedModel;
        TextBox txtModelSearch;
        TextBox txtGeminiKey, txtOrKey, txtLocalUrl, txtCustomUrl, txtCustomKey;
        TextBox txtTencentKey, txtAlibabaKey, txtZhipuKey;
        ComboBox cmbCustomPreset, cmbLanguage;
        Label lblKeyHint, lblConnectionStatus;
        Panel mainPanel;
        string modelSearch = "";
        bool showPaidModels;
        bool updatingSelection;
        string uiLanguage;

        public SettingsDialog(ProviderManager manager, string configPath, Func<string, string, string, string> translator, string languageCode)
        {
            mgr = manager;
            this.configPath = configPath;
            tr = translator ?? ((en, zhHans, zhHant) => en);
            uiLanguage = string.IsNullOrWhiteSpace(languageCode) ? "zh-Hans" : languageCode;
            selectedModel = mgr.CurrentModel;
            showPaidModels = selectedModel != null && selectedModel.RequiresApiKey && !selectedModel.IsFree;

            Text = T("Settings", "设置", "設定");
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(880, 720);
            MinimumSize = new Size(780, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(245, 245, 245);
            Font = new Font(IsEnglish() ? "Segoe UI" : "Microsoft YaHei UI", 9f);

            BuildUI();
        }

        public ModelInfo SelectedModel { get { return selectedModel; } }
        public string SelectedLanguage { get { return uiLanguage; } }
        public string GeminiKey { get { return TextOrEmpty(txtGeminiKey); } }
        public string OpenRouterKey { get { return TextOrEmpty(txtOrKey); } }
        public string LocalApiUrl { get { return txtLocalUrl != null ? txtLocalUrl.Text.Trim() : mgr.LocalApiUrl; } }
        public string CustomApiUrl { get { return TextOrEmpty(txtCustomUrl); } }
        public string CustomApiKey { get { return TextOrEmpty(txtCustomKey); } }
        public string TencentKey { get { return TextOrEmpty(txtTencentKey); } }
        public string AlibabaKey { get { return TextOrEmpty(txtAlibabaKey); } }
        public string ZhipuKey { get { return TextOrEmpty(txtZhipuKey); } }

        static string TextOrEmpty(TextBox box)
        {
            return box != null ? box.Text.Trim() : "";
        }

        void BuildUI()
        {
            Controls.Clear();
            modelRadios.Clear();
            modelRows.Clear();
            modelStatusLabels.Clear();

            txtModelSearch = null;
            txtGeminiKey = null;
            txtOrKey = null;
            txtLocalUrl = null;
            txtCustomUrl = null;
            txtCustomKey = null;
            txtTencentKey = null;
            txtAlibabaKey = null;
            txtZhipuKey = null;
            cmbCustomPreset = null;
            cmbLanguage = null;
            lblKeyHint = null;
            lblConnectionStatus = null;

            mainPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, 0, 18) };
            Controls.Add(mainPanel);

            int y = 16;
            AddLabel(T("Model & Provider", "模型与服务商", "模型與服務商"), 20, y, 790, 26, 14f, FontStyle.Bold, Color.FromArgb(30, 30, 30));
            y += 34;

            AddLabel(T("Choose one model. The matching key field below is highlighted automatically.", "选择一个模型，下方对应的密钥输入框会自动高亮。", "選擇一個模型，下方對應的金鑰輸入框會自動高亮。"), 20, y, 810, 22, 9f, FontStyle.Regular, Color.FromArgb(90, 90, 90));
            y += 30;

            AddLabel(T("Available: ", "可用：", "可用：") + ModelRegistry.Local.Count + T(" local, ", " 个本地，", " 個本機，") + ModelRegistry.Free.Count + T(" free/trial cloud, ", " 个免费/试用云模型，", " 個免費/試用雲模型，") + ModelRegistry.Paid.Count + T(" paid cloud.", " 个付费云模型。", " 個付費雲模型。"), 20, y, 810, 22, 8.5f, FontStyle.Bold, Color.FromArgb(70, 70, 70));
            y += 32;

            var languageLabel = new Label { Text = T("Language", "语言", "語言"), Location = new Point(20, y + 4), AutoSize = true };
            cmbLanguage = new ComboBox { Location = new Point(120, y), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbLanguage.Items.Add("简体中文");
            cmbLanguage.Items.Add("繁體中文");
            cmbLanguage.Items.Add("English");
            cmbLanguage.SelectedItem = LanguageDisplay(uiLanguage);
            cmbLanguage.SelectedIndexChanged += (s, e) =>
            {
                SyncFieldsToManager();
                uiLanguage = LanguageCode(Convert.ToString(cmbLanguage.SelectedItem));
                BuildUI();
            };
            mainPanel.Controls.Add(languageLabel);
            mainPanel.Controls.Add(cmbLanguage);
            y += 38;

            var searchLabel = new Label { Text = T("Search", "搜索", "搜尋"), Location = new Point(20, y + 4), AutoSize = true };
            txtModelSearch = new TextBox { Location = new Point(120, y), Width = 360, Text = modelSearch };
            var searchHint = new Label
            {
                Text = T("Try: gemini, qwen, openrouter, local, deepseek", "可输入：gemini、qwen、openrouter、本地、deepseek", "可輸入：gemini、qwen、openrouter、本機、deepseek"),
                Location = new Point(490, y + 4),
                AutoSize = true,
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 7.5f)
            };
            txtModelSearch.TextChanged += (s, e) =>
            {
                SyncFieldsToManager();
                modelSearch = txtModelSearch.Text.Trim();
                BuildUI();
                BeginInvoke((Action)(() =>
                {
                    if (txtModelSearch != null)
                    {
                        txtModelSearch.Focus();
                        txtModelSearch.SelectionStart = txtModelSearch.Text.Length;
                    }
                }));
            };
            mainPanel.Controls.Add(searchLabel);
            mainPanel.Controls.Add(txtModelSearch);
            mainPanel.Controls.Add(searchHint);
            y += 38;

            if (string.IsNullOrWhiteSpace(modelSearch))
            {
                y = AddModelSection(y, T("Recommended - easiest to start", "推荐 - 最容易开始", "推薦 - 最容易開始"), Color.FromArgb(88, 88, 128), GetRecommendedModels());
                y += 8;
            }

            y = AddModelSection(y, T("Local Models - no API key needed", "本地模型 - 不需要 API 密钥", "本機模型 - 不需要 API 金鑰"), Color.FromArgb(30, 130, 80), ModelRegistry.Local);
            y += 8;
            y = AddModelSection(y, T("Free Cloud Models - free API key required", "免费云模型 - 需要免费 API 密钥", "免費雲模型 - 需要免費 API 金鑰"), Color.FromArgb(30, 100, 180), ModelRegistry.Free);
            y += 8;

            var paidToggle = new CheckBox
            {
                Text = T("Show paid cloud models", "显示付费云模型", "顯示付費雲模型") + " (" + ModelRegistry.Paid.Count + ")",
                Location = new Point(20, y + 2),
                AutoSize = true,
                Checked = showPaidModels,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            paidToggle.CheckedChanged += (s, e) =>
            {
                SyncFieldsToManager();
                showPaidModels = paidToggle.Checked;
                BuildUI();
            };
            mainPanel.Controls.Add(paidToggle);
            y += 34;

            y = AddModelSection(y, T("Paid Cloud Models - paid API key required", "付费云模型 - 需要付费 API 密钥", "付費雲模型 - 需要付費 API 金鑰"), Color.FromArgb(160, 80, 40), showPaidModels ? ModelRegistry.Paid : new List<ModelInfo>());
            y += 16;

            y = AddApiKeySection(y);
            y += 12;

            lblConnectionStatus = new Label
            {
                Location = new Point(20, y),
                Size = new Size(810, 60),
                ForeColor = Color.FromArgb(80, 80, 80),
                Text = T("Select a model and click Test Connection.", "选择模型后点击测试连接。", "選擇模型後點擊測試連線。")
            };
            mainPanel.Controls.Add(lblConnectionStatus);
            y += 70;

            var btnTest = new Button { Text = T("Test Connection", "测试连接", "測試連線"), Location = new Point(440, y), Width = 140, Height = 32 };
            btnTest.BackColor = Color.FromArgb(230, 230, 230);
            btnTest.Click += OnTestClick;

            var btnSave = new Button { Text = T("Save", "保存", "儲存"), Location = new Point(600, y), Width = 90, Height = 32 };
            btnSave.BackColor = Color.FromArgb(0, 120, 200);
            btnSave.ForeColor = Color.White;
            btnSave.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            var btnCancel = new Button { Text = T("Cancel", "取消", "取消"), Location = new Point(700, y), Width = 90, Height = 32 };
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            mainPanel.Controls.Add(btnTest);
            mainPanel.Controls.Add(btnSave);
            mainPanel.Controls.Add(btnCancel);

            SelectModelFromSettings(selectedModel, false);
        }

        void AddLabel(string text, int x, int y, int w, int h, float size, FontStyle style, Color color)
        {
            mainPanel.Controls.Add(new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                Font = new Font("Segoe UI", size, style),
                ForeColor = color
            });
        }

        int AddModelSection(int y, string title, Color accent, List<ModelInfo> sourceModels)
        {
            var models = FilterModels(sourceModels);
            var bar = new Panel { Location = new Point(16, y), Size = new Size(810, 28), BackColor = accent };
            bar.Controls.Add(new Label
            {
                Text = title,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(10, 5),
                AutoSize = true
            });
            mainPanel.Controls.Add(bar);
            y += 34;

            if (models.Count == 0)
            {
                mainPanel.Controls.Add(new Label
                {
                    Text = string.IsNullOrWhiteSpace(modelSearch) ? T("Hidden. Enable the checkbox above to show this section.", "已隐藏。勾选上方选项后显示此区域。", "已隱藏。勾選上方選項後顯示此區域。") : T("No models match the search.", "没有匹配的模型。", "沒有符合的模型。"),
                    Location = new Point(24, y),
                    AutoSize = true,
                    ForeColor = Color.FromArgb(120, 120, 120)
                });
                return y + 26;
            }

            foreach (var model in models)
            {
                AddModelRow(y, model);
                y += 42;
            }
            return y;
        }

        List<ModelInfo> FilterModels(List<ModelInfo> models)
        {
            var result = new List<ModelInfo>();
            if (models == null) return result;
            if (string.IsNullOrWhiteSpace(modelSearch)) return models;
            string q = modelSearch.Trim().ToLowerInvariant();
            foreach (var m in models)
            {
                string text = ((m.DisplayName ?? "") + " " + (m.Id ?? "") + " " + (m.ProviderId ?? "") + " " + (m.Endpoint ?? "") + " " + (m.ApiKeyLabel ?? "")).ToLowerInvariant();
                if (text.IndexOf(q) >= 0) result.Add(m);
            }
            return result;
        }

        List<ModelInfo> GetRecommendedModels()
        {
            var result = new List<ModelInfo>();
            AddModelIfFound(result, ModelRegistry.Local, "qwen3:8b");
            AddModelIfFound(result, ModelRegistry.Free, "gemini-flash-lite-latest");
            AddModelIfFound(result, ModelRegistry.Free, "openrouter/auto");
            AddModelIfFound(result, ModelRegistry.Paid, "__custom_openai__");
            return result;
        }

        void AddModelIfFound(List<ModelInfo> result, List<ModelInfo> source, string id)
        {
            if (source == null) return;
            foreach (var m in source)
            {
                if (string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase) && !result.Contains(m))
                {
                    result.Add(m);
                    return;
                }
            }
        }

        void AddModelRow(int y, ModelInfo model)
        {
            var row = new Panel
            {
                Location = new Point(20, y),
                Size = new Size(805, 40),
                BackColor = Color.White,
                Cursor = Cursors.Hand
            };

            var radio = new RadioButton { Location = new Point(8, 10), AutoSize = true, Tag = model };
            var name = new Label { Text = model.DisplayName, Location = new Point(32, 6), AutoSize = true, Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand };
            var meta = new Label { Text = FormatModelMeta(model), Location = new Point(32, 22), AutoSize = true, Font = new Font("Segoe UI", 7.5f), ForeColor = Color.FromArgb(120, 120, 120), Cursor = Cursors.Hand };
            var status = new Label { Text = RowStatusLabel(model), ForeColor = RowStatusColor(model), Location = new Point(710, 11), AutoSize = true, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Cursor = Cursors.Hand };

            EventHandler choose = (s, e) => SelectModelFromSettings(model, true);
            row.Click += choose;
            name.Click += choose;
            meta.Click += choose;
            status.Click += choose;
            radio.CheckedChanged += (s, e) =>
            {
                if (!updatingSelection && radio.Checked) SelectModelFromSettings(model, true);
            };

            row.Controls.Add(radio);
            row.Controls.Add(name);
            row.Controls.Add(meta);
            row.Controls.Add(status);
            mainPanel.Controls.Add(row);

            modelRadios[model] = radio;
            modelRows[model] = row;
            modelStatusLabels[model] = status;
        }

        string FormatModelMeta(ModelInfo m)
        {
            var parts = new List<string>();
            if (m.ContextLength > 0) parts.Add((m.ContextLength / 1000) + T("K context", "K 上下文", "K 上下文"));
            if (m.SupportsVision) parts.Add(T("vision", "支持图片", "支援圖片"));
            if (!m.RequiresApiKey) parts.Add(T("no key needed", "无需密钥", "無需金鑰"));
            else if (!string.IsNullOrWhiteSpace(m.ApiKeyLabel)) parts.Add(m.ApiKeyLabel);
            parts.Add(KeyStateLabel(m));
            return string.Join(" | ", parts.ToArray());
        }

        int AddApiKeySection(int y)
        {
            var sectionBar = new Panel { Location = new Point(16, y), Size = new Size(810, 28), BackColor = Color.FromArgb(70, 70, 70) };
            sectionBar.Controls.Add(new Label { Text = T("API Keys", "API 密钥", "API 金鑰"), ForeColor = Color.White, Font = new Font(IsEnglish() ? "Segoe UI" : "Microsoft YaHei UI", 9f, FontStyle.Bold), Location = new Point(10, 5), AutoSize = true });
            mainPanel.Controls.Add(sectionBar);
            y += 34;

            lblKeyHint = new Label { Text = "", Location = new Point(24, y), Size = new Size(780, 28), ForeColor = Color.FromArgb(40, 90, 160), Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            mainPanel.Controls.Add(lblKeyHint);
            y += 32;

            y = AddSecretRow(y, T("Gemini API Key", "Gemini API 密钥", "Gemini API 金鑰"), mgr.GeminiKey, "https://aistudio.google.com/apikey", out txtGeminiKey);
            y = AddSecretRow(y, T("OpenRouter API Key", "OpenRouter API 密钥", "OpenRouter API 金鑰"), mgr.OpenRouterKey, "https://openrouter.ai/settings/keys", out txtOrKey);
            y = AddSecretRow(y, T("Tencent WorkBuddy Key", "腾讯 WorkBuddy 密钥", "騰訊 WorkBuddy 金鑰"), mgr.TencentKey, "https://tokenhub.tencentcloud.com", out txtTencentKey);
            y = AddSecretRow(y, T("Alibaba DashScope Key", "阿里 DashScope 密钥", "阿里 DashScope 金鑰"), mgr.AlibabaKey, "https://dashscope.aliyuncs.com", out txtAlibabaKey);
            y = AddSecretRow(y, T("Zhipu AI Key", "智谱 AI 密钥", "智譜 AI 金鑰"), mgr.ZhipuKey, "https://open.bigmodel.cn", out txtZhipuKey);

            var lblLocal = new Label { Text = T("Local API URL", "本地 API 地址", "本機 API 位址"), Location = new Point(24, y + 4), AutoSize = true };
            txtLocalUrl = new TextBox { Location = new Point(180, y), Width = 430, Text = mgr.LocalApiUrl };
            mainPanel.Controls.Add(lblLocal);
            mainPanel.Controls.Add(txtLocalUrl);
            y += 24;
            mainPanel.Controls.Add(new Label { Text = T("Ollama default: ", "Ollama 默认：", "Ollama 預設：") + "http://localhost:11434/api/chat", Location = new Point(180, y), AutoSize = true, ForeColor = Color.FromArgb(120, 120, 120), Font = new Font(IsEnglish() ? "Segoe UI" : "Microsoft YaHei UI", 7.5f) });
            y += 26;

            var lblCustomUrl = new Label { Text = T("Custom API URL", "自定义 API 地址", "自訂 API 位址"), Location = new Point(24, y + 4), AutoSize = true };
            txtCustomUrl = new TextBox { Location = new Point(180, y), Width = 430, Text = mgr.CustomApiUrl };
            var openAiLink = new LinkLabel { Text = T("OpenAI key", "OpenAI 密钥", "OpenAI 金鑰"), Location = new Point(620, y + 2), AutoSize = true };
            openAiLink.LinkClicked += (s, e) => OpenUrl("https://platform.openai.com/api-keys");
            mainPanel.Controls.Add(lblCustomUrl);
            mainPanel.Controls.Add(txtCustomUrl);
            mainPanel.Controls.Add(openAiLink);
            y += 28;

            cmbCustomPreset = new ComboBox { Location = new Point(180, y), Width = 290, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbCustomPreset.Items.Add(T("Custom /v1 endpoint", "自定义 /v1 接口", "自訂 /v1 端點"));
            cmbCustomPreset.Items.Add("OpenAI - https://api.openai.com/v1");
            cmbCustomPreset.Items.Add("DeepSeek - https://api.deepseek.com/v1");
            cmbCustomPreset.Items.Add("Kimi - https://api.moonshot.cn/v1");
            cmbCustomPreset.Items.Add("SiliconFlow - https://api.siliconflow.cn/v1");
            cmbCustomPreset.SelectedIndex = 0;
            cmbCustomPreset.SelectedIndexChanged += (s, e) =>
            {
                if (cmbCustomPreset.SelectedIndex == 1) txtCustomUrl.Text = "https://api.openai.com/v1";
                else if (cmbCustomPreset.SelectedIndex == 2) txtCustomUrl.Text = "https://api.deepseek.com/v1";
                else if (cmbCustomPreset.SelectedIndex == 3) txtCustomUrl.Text = "https://api.moonshot.cn/v1";
                else if (cmbCustomPreset.SelectedIndex == 4) txtCustomUrl.Text = "https://api.siliconflow.cn/v1";
            };
            mainPanel.Controls.Add(cmbCustomPreset);
            mainPanel.Controls.Add(new Label { Text = T("Preset fills only the URL. Paste the matching key below.", "预设只会填写地址；请在下方粘贴对应密钥。", "預設只會填入位址；請在下方貼上對應金鑰。"), Location = new Point(480, y + 4), AutoSize = true, ForeColor = Color.FromArgb(120, 120, 120), Font = new Font(IsEnglish() ? "Segoe UI" : "Microsoft YaHei UI", 7.5f) });
            y += 28;

            mainPanel.Controls.Add(new Label { Text = T("OpenAI-compatible: OpenAI, DeepSeek, Kimi, SiliconFlow, or any /v1 gateway.", "OpenAI 兼容：OpenAI、DeepSeek、Kimi、SiliconFlow 或任意 /v1 网关。", "OpenAI 相容：OpenAI、DeepSeek、Kimi、SiliconFlow 或任意 /v1 閘道。"), Location = new Point(180, y), AutoSize = true, ForeColor = Color.FromArgb(120, 120, 120), Font = new Font(IsEnglish() ? "Segoe UI" : "Microsoft YaHei UI", 7.5f) });
            y += 24;

            y = AddSecretRow(y, T("Custom API Key", "自定义 API 密钥", "自訂 API 金鑰"), mgr.CustomApiKey, "", out txtCustomKey);

            mainPanel.Controls.Add(new Label { Text = T("Config saved at: ", "配置保存位置：", "設定儲存位置：") + configPath, Location = new Point(24, y), AutoSize = true, ForeColor = Color.FromArgb(120, 120, 120), Font = new Font(IsEnglish() ? "Segoe UI" : "Microsoft YaHei UI", 7.5f) });
            return y + 22;
        }

        int AddSecretRow(int y, string label, string value, string link, out TextBox box)
        {
            mainPanel.Controls.Add(new Label { Text = label, Location = new Point(24, y + 4), AutoSize = true });
            box = new TextBox { Location = new Point(180, y), Width = 430, UseSystemPasswordChar = true, Text = value ?? "" };
            mainPanel.Controls.Add(box);
            if (!string.IsNullOrWhiteSpace(link))
            {
                var keyLink = new LinkLabel { Text = T("Get key", "获取密钥", "取得金鑰"), Location = new Point(620, y + 2), AutoSize = true, Tag = link };
                keyLink.LinkClicked += (s, e) => OpenUrl(Convert.ToString(((Control)s).Tag));
                mainPanel.Controls.Add(keyLink);
            }
            return y + 30;
        }

        void SelectModelFromSettings(ModelInfo model, bool focusKeyField)
        {
            if (model == null) return;
            selectedModel = model;
            updatingSelection = true;
            try
            {
                foreach (var item in modelRows)
                {
                    bool selected = item.Key == model;
                    item.Value.BackColor = selected ? Color.FromArgb(220, 235, 255) : Color.White;
                    RadioButton radio;
                    if (modelRadios.TryGetValue(item.Key, out radio)) radio.Checked = selected;
                    Label status;
                    if (modelStatusLabels.TryGetValue(item.Key, out status))
                    {
                        status.Text = selected ? T("SELECTED", "已选择", "已選擇") : RowStatusLabel(item.Key);
                        status.ForeColor = selected ? Color.FromArgb(0, 110, 70) : RowStatusColor(item.Key);
                    }
                }
            }
            finally
            {
                updatingSelection = false;
            }
            UpdateKeyFields();
            if (focusKeyField) FocusFieldForSelectedModel();
        }

        void UpdateKeyFields()
        {
            if (selectedModel == null) return;
            Color active = Color.FromArgb(255, 255, 210);
            string ep = selectedModel.Endpoint;
            SetBackColor(txtGeminiKey, ep == "Gemini", active);
            SetBackColor(txtOrKey, ep == "OpenRouter", active);
            SetBackColor(txtLocalUrl, ep == "Local", active);
            SetBackColor(txtCustomUrl, ep == "OpenAICompatible", active);
            SetBackColor(txtCustomKey, ep == "OpenAICompatible", active);
            SetBackColor(txtTencentKey, ep == "TencentWorkBuddy", active);
            SetBackColor(txtAlibabaKey, ep == "AlibabaQianwen", active);
            SetBackColor(txtZhipuKey, ep == "ZhipuAI", active);
            if (lblKeyHint != null) lblKeyHint.Text = BuildKeyHint();
        }

        static void SetBackColor(TextBox box, bool isActive, Color active)
        {
            if (box != null) box.BackColor = isActive ? active : Color.White;
        }

        string BuildKeyHint()
        {
            if (selectedModel == null) return "";
            if (!selectedModel.RequiresApiKey)
                return T("Selected local model. No API key is needed; check the Local API URL.", "已选择本地模型。无需 API 密钥，请确认本地 API 地址。", "已選擇本機模型。無需 API 金鑰，請確認本機 API 位址。");
            string label = string.IsNullOrWhiteSpace(selectedModel.ApiKeyLabel) ? "API Key" : selectedModel.ApiKeyLabel;
            return T("Selected: ", "已选择：", "已選擇：") + selectedModel.DisplayName + T(" - fill ", " - 填写 ", " - 填寫 ") + label + T(", then click Test Connection.", "，然后点击测试连接。", "，然後點擊測試連線。");
        }

        string KeyStateLabel(ModelInfo model)
        {
            if (model == null) return "";
            string status;
            if (modelTestStatus.TryGetValue(model.Id, out status)) return status;
            if (!model.RequiresApiKey) return T("local", "本地", "本機");
            return string.IsNullOrWhiteSpace(mgr.GetApiKey(model)) ? T("needs key", "需要密钥", "需要金鑰") : T("key saved", "密钥已保存", "金鑰已儲存");
        }

        string RowStatusLabel(ModelInfo model)
        {
            string status;
            if (model != null && modelTestStatus.TryGetValue(model.Id, out status))
                return status == "test passed" ? T("PASS", "通过", "通過") : T("FAIL", "失败", "失敗");
            if (model == null) return "";
            if (!model.RequiresApiKey) return T("LOCAL", "本地", "本機");
            return string.IsNullOrWhiteSpace(mgr.GetApiKey(model)) ? T("NO KEY", "缺密钥", "缺金鑰") : T("READY", "就绪", "就緒");
        }

        Color RowStatusColor(ModelInfo model)
        {
            string label = RowStatusLabel(model);
            if (label == T("PASS", "通过", "通過") || label == T("READY", "就绪", "就緒")) return Color.FromArgb(0, 140, 70);
            if (label == T("FAIL", "失败", "失敗")) return Color.FromArgb(190, 40, 40);
            if (label == T("NO KEY", "缺密钥", "缺金鑰")) return Color.FromArgb(190, 120, 20);
            return Color.FromArgb(120, 120, 120);
        }

        void FocusFieldForSelectedModel()
        {
            TextBox target = null;
            string ep = selectedModel != null ? selectedModel.Endpoint : "";
            if (ep == "Gemini") target = txtGeminiKey;
            else if (ep == "OpenRouter") target = txtOrKey;
            else if (ep == "Local") target = txtLocalUrl;
            else if (ep == "OpenAICompatible") target = string.IsNullOrWhiteSpace(CustomApiUrl) ? txtCustomUrl : txtCustomKey;
            else if (ep == "TencentWorkBuddy") target = txtTencentKey;
            else if (ep == "AlibabaQianwen") target = txtAlibabaKey;
            else if (ep == "ZhipuAI") target = txtZhipuKey;
            if (target != null)
            {
                target.Focus();
                target.SelectAll();
            }
        }

        void SyncFieldsToManager()
        {
            mgr.GeminiKey = GeminiKey;
            mgr.OpenRouterKey = OpenRouterKey;
            mgr.LocalApiUrl = LocalApiUrl;
            mgr.CustomApiUrl = CustomApiUrl;
            mgr.CustomApiKey = CustomApiKey;
            mgr.TencentKey = TencentKey;
            mgr.AlibabaKey = AlibabaKey;
            mgr.ZhipuKey = ZhipuKey;
        }

        async void OnTestClick(object sender, EventArgs e)
        {
            if (selectedModel == null) return;
            SyncFieldsToManager();

            lblConnectionStatus.Text = T("Testing...", "测试中...", "測試中...");
            lblConnectionStatus.ForeColor = Color.FromArgb(80, 80, 80);

            string result = await mgr.TestModelAsync(selectedModel);
            bool pass = result.StartsWith("PASS", StringComparison.OrdinalIgnoreCase);
            modelTestStatus[selectedModel.Id] = pass ? "test passed" : "test failed";
            SelectModelFromSettings(selectedModel, false);
            lblConnectionStatus.Text = result;
            lblConnectionStatus.ForeColor = pass ? Color.FromArgb(0, 140, 60) : Color.FromArgb(190, 40, 40);
        }

        string T(string en, string zhHans, string zhHant)
        {
            if (string.Equals(uiLanguage, "zh-Hant", StringComparison.OrdinalIgnoreCase)) return zhHant;
            if (string.Equals(uiLanguage, "en", StringComparison.OrdinalIgnoreCase)) return en;
            return zhHans;
        }

        bool IsEnglish()
        {
            return string.Equals(uiLanguage, "en", StringComparison.OrdinalIgnoreCase);
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

        static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            System.Diagnostics.Process.Start(url);
        }
    }
}
