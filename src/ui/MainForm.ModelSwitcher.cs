using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ZhuaQianDesktopApp
{
    public partial class MainForm
    {
        ComboBox topModelCombo;
        Label topModelStatusLabel;
        bool updatingTopModelCombo;

        void CreateTopModelControls(Control parent)
        {
            modelLabel = new Label
            {
                Text = Tr("Model", "\u6A21\u578B", "\u6A21\u578B"),
                Location = new Point(290, 12),
                AutoSize = false,
                AutoEllipsis = true,
                Size = new Size(44, 22),
                ForeColor = zqMuted
            };
            parent.Controls.Add(modelLabel);

            topModelCombo = CreateTopModelCombo();
            topModelCombo.BackColor = zqSurface;
            topModelCombo.ForeColor = zqInk;
            parent.Controls.Add(topModelCombo);

            topModelStatusLabel = new Label
            {
                Text = "",
                AutoSize = false,
                AutoEllipsis = true,
                ForeColor = zqMuted,
                Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular)
            };
            parent.Controls.Add(topModelStatusLabel);

            PopulateTopModelCombo();
            RefreshTopModelStatus();
        }

        ComboBox CreateTopModelCombo()
        {
            var combo = new ComboBox
            {
                Width = 280,
                Height = 28,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            combo.SelectedIndexChanged += (s, e) => SelectTopModelFromCombo();
            return combo;
        }

        void PopulateTopModelCombo()
        {
            if (topModelCombo == null) return;
            updatingTopModelCombo = true;
            try
            {
                topModelCombo.Items.Clear();
                AddTopModelItems(ModelRegistry.Free, Tr("Free", "\u514D\u8D39", "\u514D\u8CBB"));
                AddTopModelItems(ModelRegistry.Local, Tr("Local", "\u672C\u5730", "\u672C\u6A5F"));
                AddTopModelItems(ModelRegistry.Paid, Tr("Paid", "\u4ED8\u8D39", "\u4ED8\u8CBB"));
                RefreshTopModelSelection();
            }
            finally
            {
                updatingTopModelCombo = false;
            }
        }

        void AddTopModelItems(List<ModelInfo> models, string group)
        {
            foreach (var modelInfo in models)
                topModelCombo.Items.Add(new TopModelItem(modelInfo, TopModelLabel(modelInfo, group)));
        }

        string TopModelLabel(ModelInfo modelInfo, string group)
        {
            string configured = IsModelConfigured(modelInfo)
                ? ""
                : " - " + Tr("not configured", "\u672A\u914D\u7F6E", "\u672A\u914D\u7F6E");
            string vision = modelInfo.SupportsVision ? " - " + Tr("vision", "\u652F\u6301\u56FE\u7247", "\u652F\u63F4\u5716\u7247") : "";
            return "[" + group + "] " + modelInfo.DisplayName + configured + vision;
        }

        bool IsModelConfigured(ModelInfo modelInfo)
        {
            if (modelInfo == null) return false;
            if (!modelInfo.RequiresApiKey) return true;
            return !string.IsNullOrWhiteSpace(providerManager.GetApiKey(modelInfo));
        }

        void SelectTopModelFromCombo()
        {
            if (updatingTopModelCombo || topModelCombo == null) return;
            var item = topModelCombo.SelectedItem as TopModelItem;
            if (item == null || item.Model == null) return;
            SelectModelFromTop(item.Model);
        }

        void SelectModelFromTop(ModelInfo selected)
        {
            providerManager.SelectModel(selected);
            provider = selected.Endpoint;
            model = selected.Id;
            if (selected.Endpoint == "OpenRouter") openRouterModel = selected.Id;
            if (selected.Endpoint == "Local") localModel = selected.Id;
            SaveConfig();
            RefreshTopModelSwitcher();
            SetCurrentTaskStatus(currentTaskStatus, Tr("Model: ", "\u6A21\u578B\uFF1A", "\u6A21\u578B\uFF1A") + selected.DisplayName, false);
        }

        void RefreshTopModelSwitcher()
        {
            if (modelLabel != null) modelLabel.Text = Tr("Model", "\u6A21\u578B", "\u6A21\u578B");
            if (topModelCombo != null)
            {
                updatingTopModelCombo = true;
                try
                {
                    RefreshTopModelSelection();
                }
                finally
                {
                    updatingTopModelCombo = false;
                }
            }
            RefreshTopModelStatus();
            if (layoutTop != null) layoutTop();
        }

        int LayoutTopModelSwitcher(int modeLeft, int panelWidth)
        {
            if (topModelCombo == null || modelLabel == null) return modeLeft;

            int comboW = Math.Min(300, Math.Max(170, panelWidth / 4));
            topModelCombo.Width = comboW;
            topModelCombo.Left = modeLeft - topModelCombo.Width - 8;
            topModelCombo.Top = 10;
            modelLabel.SetBounds(topModelCombo.Left - 50, 12, 44, 22);
            if (topModelStatusLabel != null)
                topModelStatusLabel.SetBounds(topModelCombo.Left, 38, topModelCombo.Width, 18);

            bool showModelSwitcher = modelLabel.Left > 220;
            modelLabel.Visible = showModelSwitcher;
            topModelCombo.Visible = showModelSwitcher;
            if (topModelStatusLabel != null) topModelStatusLabel.Visible = showModelSwitcher;
            return showModelSwitcher ? modelLabel.Left : modeLeft;
        }

        void RefreshTopModelStatus()
        {
            if (topModelStatusLabel == null) return;
            var modelInfo = providerManager.CurrentModel;
            if (modelInfo == null)
            {
                topModelStatusLabel.Text = Tr("No model selected", "\u672A\u9009\u62E9\u6A21\u578B", "\u672A\u9078\u64C7\u6A21\u578B");
                return;
            }

            string keyState = IsModelConfigured(modelInfo)
                ? Tr("ready", "\u53EF\u7528", "\u53EF\u7528")
                : Tr("missing key", "\u7F3A\u5BC6\u94A5", "\u7F3A\u91D1\u9470");
            string location = modelInfo.RequiresApiKey
                ? Tr("cloud", "\u4E91\u7AEF", "\u96F2\u7AEF")
                : Tr("local", "\u672C\u5730", "\u672C\u6A5F");
            string vision = modelInfo.SupportsVision
                ? Tr("vision", "\u652F\u6301\u56FE\u7247", "\u652F\u63F4\u5716\u7247")
                : Tr("text", "\u6587\u672C", "\u6587\u672C");
            string ctx = modelInfo.ContextLength > 0 ? (modelInfo.ContextLength / 1000).ToString() + "K" : "?";

            topModelStatusLabel.Text = keyState + " | " + location + " | " + vision + " | " + ctx;
        }

        void RefreshTopModelSelection()
        {
            if (topModelCombo == null) return;
            ModelInfo current = providerManager.CurrentModel;
            for (int i = 0; i < topModelCombo.Items.Count; i++)
            {
                var item = topModelCombo.Items[i] as TopModelItem;
                if (item != null && current != null && string.Equals(item.Model.Id, current.Id, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.Model.Endpoint, current.Endpoint, StringComparison.OrdinalIgnoreCase))
                {
                    topModelCombo.SelectedIndex = i;
                    return;
                }
            }
            if (topModelCombo.Items.Count > 0 && topModelCombo.SelectedIndex < 0) topModelCombo.SelectedIndex = 0;
        }

        sealed class TopModelItem
        {
            public readonly ModelInfo Model;
            readonly string label;

            public TopModelItem(ModelInfo model, string label)
            {
                Model = model;
                this.label = label ?? "";
            }

            public override string ToString()
            {
                return label;
            }
        }
    }
}
