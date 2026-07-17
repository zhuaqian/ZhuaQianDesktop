using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Agent;

namespace ZhuaQianDesktopApp.Ui
{
    public class PlanReviewDialog : Form
    {
        readonly AgentPlanParser parser = new AgentPlanParser();

        TextBox sourceBox;
        DataGridView grid;
        TextBox reviewBox;

        public string ReviewMarkdown { get; private set; }
        public bool RequestPlanPrompt { get; private set; }
        public bool RequestExecute { get; private set; }
        public string PlanSource { get { return sourceBox == null ? "" : sourceBox.Text; } }

        public PlanReviewDialog(string initialPlan)
        {
            Text = "ZhuaQian Plan Review";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1040, 720);
            MinimumSize = new Size(900, 580);
            Font = new Font("Microsoft YaHei UI", 9);

            BuildUi(initialPlan ?? "");
            ParsePlan();
        }

        void BuildUi(string initialPlan)
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 170, Padding = new Padding(12), BackColor = Color.FromArgb(246, 248, 251) };
            Controls.Add(top);

            top.Controls.Add(new Label { Text = "Plan source", Left = 12, Top = 10, Width = 180, Height = 20, Font = new Font(Font, FontStyle.Bold) });
            sourceBox = new TextBox { Left = 12, Top = 34, Width = 790, Height = 118, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = initialPlan };
            top.Controls.Add(sourceBox);

            var parse = new Button { Text = "Parse", Left = 820, Top = 34, Width = 170, Height = 30 };
            parse.Click += (s, e) => ParsePlan();
            top.Controls.Add(parse);

            var makePrompt = new Button { Text = "Make Plan Prompt", Left = 820, Top = 72, Width = 170, Height = 30 };
            makePrompt.Click += (s, e) => { RequestPlanPrompt = true; DialogResult = DialogResult.Retry; Close(); };
            top.Controls.Add(makePrompt);

            var insert = new Button { Text = "Insert Review", Left = 820, Top = 110, Width = 170, Height = 30 };
            insert.Click += (s, e) => { if (string.IsNullOrWhiteSpace(ReviewMarkdown)) ParsePlan(); DialogResult = DialogResult.OK; Close(); };
            top.Controls.Add(insert);

            var execute = new Button { Text = "Execute Plan", Left = 820, Top = 140, Width = 170, Height = 30 };
            execute.Click += (s, e) => { ParsePlan(); RequestExecute = true; DialogResult = DialogResult.Yes; Close(); };
            top.Controls.Add(execute);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 310 };
            Controls.Add(split);

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false
            };
            grid.Columns.Add("stepId", "Step");
            grid.Columns.Add("title", "Title");
            grid.Columns.Add("actionType", "Action");
            grid.Columns.Add("target", "Target");
            grid.Columns.Add("riskLevel", "Risk");
            grid.Columns.Add("permission", "Permission");
            grid.Columns.Add("expectedOutput", "Expected output");
            grid.Columns.Add("rollback", "Rollback");
            grid.Columns.Add("status", "Status");
            split.Panel1.Controls.Add(grid);

            var bottom = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            split.Panel2.Controls.Add(bottom);
            reviewBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9), WordWrap = false };
            bottom.Controls.Add(reviewBox);

            var actions = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            bottom.Controls.Add(actions);
            var copy = new Button { Text = "Copy", Left = 0, Top = 8, Width = 90, Height = 30 };
            copy.Click += (s, e) => { if (!string.IsNullOrEmpty(reviewBox.Text)) Clipboard.SetText(reviewBox.Text); };
            actions.Controls.Add(copy);
            var close = new Button { Text = "Close", Left = 100, Top = 8, Width = 90, Height = 30 };
            close.Click += (s, e) => Close();
            actions.Controls.Add(close);
        }

        void ParsePlan()
        {
            AgentPlan plan = parser.Parse(sourceBox.Text);
            grid.Rows.Clear();
            foreach (AgentPlanStep step in plan.Steps)
            {
                grid.Rows.Add(
                    step.StepId,
                    step.Title,
                    step.CommandType,
                    step.Target,
                    step.RiskLevel,
                    step.Permission,
                    step.ExpectedOutput,
                    step.RollbackPossible ? "yes" : "no",
                    step.Status);
            }
            ReviewMarkdown = BuildReview(plan);
            reviewBox.Text = ReviewMarkdown;
        }

        string BuildReview(AgentPlan plan)
        {
            var sb = new StringBuilder();
            sb.AppendLine(plan.ToReviewMarkdown());
            sb.AppendLine();
            sb.AppendLine("Approval checklist:");
            sb.AppendLine("- Review each target path before execution.");
            sb.AppendLine("- Approve only the steps that match the user goal.");
            sb.AppendLine("- Reject or edit any step with unclear permission, target, or rollback.");
            return sb.ToString().Trim();
        }
    }
}
