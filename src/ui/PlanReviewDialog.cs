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
        readonly Func<string, string, string, string> tr;
        readonly string uiLanguage;

        TextBox sourceBox;
        DataGridView grid;
        TextBox reviewBox;

        public string ReviewMarkdown { get; private set; }
        public bool RequestPlanPrompt { get; private set; }
        public bool RequestExecute { get; private set; }
        public string PlanSource { get { return sourceBox == null ? "" : sourceBox.Text; } }

        // Optional hook invoked by the "Full Review" button: runs the coding-agent
        // review (workspace scan + diff + build + test) for the current plan and
        // shows the Plan -> Command -> Diff -> Test -> Review report. Wired by
        // MainForm.PlanReview.ShowPlanReview so this dialog stays UI-only.
        public Action<AgentPlan> FullReviewCallback { get; set; }

        public PlanReviewDialog(string initialPlan, Func<string, string, string, string> translator = null, string languageCode = "zh-Hans")
        {
            tr = translator ?? ((en, zhHans, zhHant) => en);
            uiLanguage = string.IsNullOrWhiteSpace(languageCode) ? "zh-Hans" : languageCode;
            Text = T("ZhuaQian Plan Review", "抓钱计划审查", "抓錢計畫審查");
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1040, 720);
            MinimumSize = new Size(900, 580);
            Font = new Font(IsEnglish() ? "Segoe UI" : "Microsoft YaHei UI", 9);

            BuildUi(initialPlan ?? "");
            ParsePlan();
        }

        void BuildUi(string initialPlan)
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 170, Padding = new Padding(12), BackColor = Color.FromArgb(246, 248, 251) };
            Controls.Add(top);

            top.Controls.Add(new Label { Text = T("Plan source", "计划来源", "計畫來源"), Left = 12, Top = 10, Width = 180, Height = 20, Font = new Font(Font, FontStyle.Bold) });
            sourceBox = new TextBox { Left = 12, Top = 34, Width = 790, Height = 118, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = initialPlan };
            top.Controls.Add(sourceBox);

            var parse = new Button { Text = T("Parse", "解析", "解析"), Left = 820, Top = 34, Width = 170, Height = 30 };
            parse.Click += (s, e) => ParsePlan();
            top.Controls.Add(parse);

            var makePrompt = new Button { Text = T("Make Plan Prompt", "生成计划提示词", "產生計畫提示詞"), Left = 820, Top = 72, Width = 170, Height = 30 };
            makePrompt.Click += (s, e) => { RequestPlanPrompt = true; DialogResult = DialogResult.Retry; Close(); };
            top.Controls.Add(makePrompt);

            var insert = new Button { Text = T("Insert Review", "插入审查结果", "插入審查結果"), Left = 820, Top = 110, Width = 170, Height = 30 };
            insert.Click += (s, e) => { if (string.IsNullOrWhiteSpace(ReviewMarkdown)) ParsePlan(); DialogResult = DialogResult.OK; Close(); };
            top.Controls.Add(insert);

            var execute = new Button { Text = T("Execute Plan", "执行计划", "執行計畫"), Left = 820, Top = 140, Width = 170, Height = 30 };
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
            grid.Columns.Add("stepId", T("Step", "步骤", "步驟"));
            grid.Columns.Add("title", T("Title", "标题", "標題"));
            grid.Columns.Add("actionType", T("Action", "动作", "動作"));
            grid.Columns.Add("target", T("Target", "目标", "目標"));
            grid.Columns.Add("riskLevel", T("Risk", "风险", "風險"));
            grid.Columns.Add("permission", T("Permission", "权限", "權限"));
            grid.Columns.Add("expectedOutput", T("Expected output", "预期输出", "預期輸出"));
            grid.Columns.Add("rollback", T("Rollback", "回滚", "回滾"));
            grid.Columns.Add("status", T("Status", "状态", "狀態"));
            split.Panel1.Controls.Add(grid);

            var bottom = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            split.Panel2.Controls.Add(bottom);
            reviewBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9), WordWrap = false };
            bottom.Controls.Add(reviewBox);

            var actions = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            bottom.Controls.Add(actions);
            var copy = new Button { Text = T("Copy", "复制", "複製"), Left = 0, Top = 8, Width = 90, Height = 30 };
            copy.Click += (s, e) => { if (!string.IsNullOrEmpty(reviewBox.Text)) Clipboard.SetText(reviewBox.Text); };
            actions.Controls.Add(copy);
            var close = new Button { Text = T("Close", "关闭", "關閉"), Left = 100, Top = 8, Width = 90, Height = 30 };
            close.Click += (s, e) => Close();
            actions.Controls.Add(close);

            var full = new Button { Text = T("Full Review", "完整审查", "完整審查"), Left = 200, Top = 8, Width = 120, Height = 30 };
            full.Click += (s, e) => { if (FullReviewCallback != null) FullReviewCallback(parser.Parse(sourceBox.Text)); };
            actions.Controls.Add(full);
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
            sb.AppendLine(T("Approval checklist:", "审批清单：", "審批清單："));
            sb.AppendLine(T("- Review each target path before execution.", "- 执行前检查每个目标路径。", "- 執行前檢查每個目標路徑。"));
            sb.AppendLine(T("- Approve only the steps that match the user goal.", "- 只批准符合用户目标的步骤。", "- 只批准符合使用者目標的步驟。"));
            sb.AppendLine(T("- Reject or edit any step with unclear permission, target, or rollback.", "- 拒绝或编辑权限、目标或回滚不清晰的步骤。", "- 拒絕或編輯權限、目標或回滾不清楚的步驟。"));
            return sb.ToString().Trim();
        }

        bool IsEnglish()
        {
            return string.Equals(uiLanguage, "en", StringComparison.OrdinalIgnoreCase);
        }

        string T(string en, string zhHans, string zhHant)
        {
            return tr(en, zhHans, zhHant);
        }
    }
}
