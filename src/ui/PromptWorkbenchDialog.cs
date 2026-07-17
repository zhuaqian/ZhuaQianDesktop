using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Core;

namespace ZhuaQianDesktopApp.Ui
{
    public class PromptWorkbenchDialog : Form
    {
        readonly PromptLibrary promptLibrary;
        readonly PermissionGate permissionGate;
        readonly AuditLog auditLog;
        readonly IEnumerable<string> attachmentLabels;

        ComboBox assemblyCombo;
        ListBox memoryList;
        TextBox taskBox;
        TextBox outputBox;
        TextBox memoryNameBox;
        TextBox memoryContentBox;
        TextBox permissionActionBox;
        Label permissionResultLabel;
        TextBox auditBox;

        public string AssembledPrompt { get; private set; }

        public PromptWorkbenchDialog(string configDir, PermissionGate permissionGate, string auditLogPath, string initialTask, IEnumerable<string> attachmentLabels)
        {
            this.promptLibrary = new PromptLibrary(configDir);
            this.permissionGate = permissionGate ?? new PermissionGate();
            this.auditLog = new AuditLog(auditLogPath);
            this.attachmentLabels = attachmentLabels ?? new List<string>();

            Text = "ZhuaQian Prompt Workbench";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(980, 680);
            MinimumSize = new Size(860, 560);
            Font = new Font("Microsoft YaHei UI", 9);

            BuildUi(initialTask ?? "");
            LoadAssemblies();
            LoadMemories();
            RefreshAudit();
        }

        void BuildUi(string initialTask)
        {
            var left = new Panel { Dock = DockStyle.Left, Width = 310, Padding = new Padding(12), BackColor = Color.FromArgb(246, 248, 251) };
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Color.White };
            Controls.Add(right);
            Controls.Add(left);

            var title = new Label { Text = "Prompt Registry / Assembly", Left = 12, Top = 12, Width = 260, Height = 22, Font = new Font(Font, FontStyle.Bold) };
            left.Controls.Add(title);

            left.Controls.Add(new Label { Text = "Assembly", Left = 12, Top = 48, Width = 260, Height = 18 });
            assemblyCombo = new ComboBox { Left = 12, Top = 68, Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            left.Controls.Add(assemblyCombo);

            left.Controls.Add(new Label { Text = "Memory", Left = 12, Top = 104, Width = 260, Height = 18 });
            memoryList = new ListBox { Left = 12, Top = 124, Width = 260, Height = 96, SelectionMode = SelectionMode.MultiExtended };
            left.Controls.Add(memoryList);

            left.Controls.Add(new Label { Text = "Memory name", Left = 12, Top = 238, Width = 260, Height = 18 });
            memoryNameBox = new TextBox { Left = 12, Top = 258, Width = 260, Text = "writing-style" };
            left.Controls.Add(memoryNameBox);

            left.Controls.Add(new Label { Text = "Memory content", Left = 12, Top = 290, Width = 260, Height = 18 });
            memoryContentBox = new TextBox { Left = 12, Top = 310, Width = 260, Height = 72, Multiline = true, ScrollBars = ScrollBars.Vertical };
            left.Controls.Add(memoryContentBox);

            var saveMemory = new Button { Text = "Save Memory", Left = 12, Top = 390, Width = 125, Height = 30 };
            saveMemory.Click += (s, e) => SaveMemory();
            left.Controls.Add(saveMemory);

            var refreshMemory = new Button { Text = "Refresh", Left = 147, Top = 390, Width = 125, Height = 30 };
            refreshMemory.Click += (s, e) => LoadMemories();
            left.Controls.Add(refreshMemory);

            left.Controls.Add(new Label { Text = "Permission action", Left = 12, Top = 438, Width = 260, Height = 18 });
            permissionActionBox = new TextBox { Left = 12, Top = 458, Width = 260, Text = "upload attached file" };
            left.Controls.Add(permissionActionBox);

            var checkPermission = new Button { Text = "Check Permission", Left = 12, Top = 490, Width = 160, Height = 30 };
            checkPermission.Click += (s, e) => CheckPermission();
            left.Controls.Add(checkPermission);

            permissionResultLabel = new Label { Text = "Decision: -", Left = 12, Top = 528, Width = 260, Height = 52, ForeColor = Color.FromArgb(80, 80, 80) };
            left.Controls.Add(permissionResultLabel);

            var close = new Button { Text = "Close", Left = 182, Top = 592, Width = 90, Height = 32, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            close.Click += (s, e) => Close();
            left.Controls.Add(close);

            var taskLabel = new Label { Text = "Task / user instruction", Dock = DockStyle.Top, Height = 20, Font = new Font(Font, FontStyle.Bold) };
            right.Controls.Add(taskLabel);

            taskBox = new TextBox { Dock = DockStyle.Top, Height = 90, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = initialTask };
            right.Controls.Add(taskBox);

            var actionPanel = new Panel { Dock = DockStyle.Top, Height = 42 };
            var assemble = new Button { Text = "Assemble Prompt", Left = 0, Top = 7, Width = 140, Height = 30 };
            assemble.Click += (s, e) => AssemblePrompt();
            var insert = new Button { Text = "Insert To Chat", Left = 150, Top = 7, Width = 130, Height = 30 };
            insert.Click += (s, e) => { if (string.IsNullOrWhiteSpace(AssembledPrompt)) AssemblePrompt(); DialogResult = DialogResult.OK; Close(); };
            var copy = new Button { Text = "Copy", Left = 290, Top = 7, Width = 80, Height = 30 };
            copy.Click += (s, e) => { if (!string.IsNullOrEmpty(outputBox.Text)) Clipboard.SetText(outputBox.Text); };
            actionPanel.Controls.Add(assemble);
            actionPanel.Controls.Add(insert);
            actionPanel.Controls.Add(copy);
            right.Controls.Add(actionPanel);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 330 };
            outputBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9), WordWrap = false };
            auditBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 8), ReadOnly = true };
            split.Panel1.Controls.Add(outputBox);
            split.Panel2.Controls.Add(auditBox);
            right.Controls.Add(split);
        }

        void LoadAssemblies()
        {
            assemblyCombo.Items.Clear();
            foreach (var item in promptLibrary.ListAssemblies())
                assemblyCombo.Items.Add(item);
            if (assemblyCombo.Items.Count > 0) assemblyCombo.SelectedIndex = 0;
        }

        void LoadMemories()
        {
            memoryList.Items.Clear();
            foreach (var item in promptLibrary.ListMemories())
                memoryList.Items.Add(item);
        }

        void SaveMemory()
        {
            string path = promptLibrary.WriteMemory(memoryNameBox.Text, memoryContentBox.Text);
            auditLog.Log("PromptMemoryWrite", path, "user", "", "ok");
            auditLog.Flush();
            LoadMemories();
            RefreshAudit();
        }

        void CheckPermission()
        {
            string action = GuessPermissionAction(permissionActionBox.Text);
            PermissionDecision decision = permissionGate.Check(action, permissionActionBox.Text);
            permissionResultLabel.Text = "Decision: " + decision + Environment.NewLine + "Action: " + action;
            auditLog.Log("PromptPermissionCheck", action + " | " + decision + " | " + permissionActionBox.Text, "user", "", "ok");
            auditLog.Flush();
            RefreshAudit();
        }

        void AssemblePrompt()
        {
            var selected = assemblyCombo.SelectedItem as PromptAssemblyInfo;
            string id = selected == null ? "programmer_bugfix" : selected.Id;
            var memoryNames = new List<string>();
            foreach (var item in memoryList.SelectedItems)
            {
                var mem = item as PromptMemoryItem;
                if (mem != null) memoryNames.Add(mem.Name);
            }

            AssembledPrompt = promptLibrary.Assemble(id, taskBox.Text, memoryNames, attachmentLabels);
            outputBox.Text = AssembledPrompt;
            auditLog.Log("PromptAssemble", id + " | memories=" + memoryNames.Count, "user", "", "ok");
            auditLog.Flush();
            RefreshAudit();
        }

        void RefreshAudit()
        {
            var rows = auditLog.List(80);
            var sb = new StringBuilder();
            foreach (var row in rows)
            {
                sb.AppendLine(row.Timestamp + " | " + row.Action + " | " + row.Status + " | " + row.Detail);
            }
            auditBox.Text = sb.ToString();
        }

        string GuessPermissionAction(string text)
        {
            text = (text ?? "").ToLowerInvariant();
            if (text.Contains("upload") || text.Contains("send") || text.Contains("publish")) return "permNetworkUpload";
            if (text.Contains("delete") || text.Contains("move")) return "permFileMoveDelete";
            if (text.Contains("write") || text.Contains("save") || text.Contains("export")) return "permFileWrite";
            if (text.Contains("plugin") || text.Contains("script")) return "permPluginRun";
            if (text.Contains("process") || text.Contains("pid")) return "permProcessManage";
            if (text.Contains("screenshot")) return "permScreenshot";
            if (text.Contains("clipboard")) return "permClipboard";
            return "permFileRead";
        }
    }
}
