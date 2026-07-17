using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ZhuaQianDesktopApp.Tools
{
    // Reusable approval dialog that replaces ad-hoc MessageBox confirmations for
    // cloud upload, folder organize, and plugin runs. Writes nothing itself; the
    // caller records the decision through the OnDecision callback.
    // Spec: docs/NEXT_STEP_EXECUTION_PLAN.md (phase 0.1.4, Approval Card).
    public enum ApprovalDecision
    {
        Approved,
        Edited,
        Cancelled
    }

    public class ApprovalCard : Form
    {
        ApprovalDecision _Decision = ApprovalDecision.Cancelled;
        string _EditedDetail = "";

        public ApprovalDecision Decision { get { return _Decision; } }
        public string EditedDetail { get { return _EditedDetail; } }

        readonly TextBox detailBox;
        readonly TextBox editBox;
        readonly Func<string, string, string, string> tr;

        public ApprovalCard(string title, string mode, List<string> requiredPermissions,
            List<string> affectedPaths, string risk, string output, string auditNote,
            Func<string, string, string, string> translator)
        {
            tr = translator ?? ((en, zhHans, zhHant) => en);
            _Decision = ApprovalDecision.Cancelled;
            _EditedDetail = "";
            Text = "ZhuaQian - " + (title ?? T("Approval", "审批", "審批"));
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Size = new Size(560, 520);

            int y = 14;
            y = AddLabel(T("Action", "动作", "動作"), title ?? "", y, true);
            y = AddLabel(T("Mode", "模式", "模式"), mode ?? "", y, false);
            y = AddLabel(T("Required permissions", "所需权限", "所需權限"), EmptyText(string.Join(", ", requiredPermissions ?? new List<string>())), y, false);
            y = AddLabel(T("Affected paths", "影响路径", "影響路徑"), EmptyText(string.Join("\n", affectedPaths ?? new List<string>())), y, false);
            y = AddLabel(T("Risk", "风险", "風險"), risk ?? T("low", "低", "低"), y, false);
            y = AddLabel(T("Output", "输出", "輸出"), string.IsNullOrEmpty(output) ? T("(none)", "（无）", "（無）") : output, y, false);

            var detailLbl = new Label { Left = 14, Top = y, Width = 520, Height = 18, Text = T("Details (copyable)", "详情（可复制）", "詳情（可複製）") };
            Controls.Add(detailLbl);
            y += 20;
            detailBox = new TextBox
            {
                Left = 14,
                Top = y,
                Width = 520,
                Height = 110,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Text = BuildDetail(auditNote, affectedPaths, risk, output)
            };
            Controls.Add(detailBox);
            y += 118;

            var editLbl = new Label { Left = 14, Top = y, Width = 520, Height = 18, Text = T("Edit note before approving (optional)", "批准前可填写备注（可选）", "核准前可填寫備註（可選）") };
            Controls.Add(editLbl);
            y += 20;
            editBox = new TextBox
            {
                Left = 14,
                Top = y,
                Width = 520,
                Height = 40,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Text = ""
            };
            Controls.Add(editBox);
            y += 48;

            var approve = new Button { Text = T("Approve", "批准", "核准"), Left = 14, Top = y, Width = 120, Height = 32, DialogResult = DialogResult.OK };
            var edit = new Button { Text = T("Approve with note", "带备注批准", "帶備註核准"), Left = 150, Top = y, Width = 150, Height = 32 };
            var cancel = new Button { Text = T("Cancel", "取消", "取消"), Left = 316, Top = y, Width = 120, Height = 32, DialogResult = DialogResult.Cancel };
            var copy = new Button { Text = T("Copy", "复制", "複製"), Left = 442, Top = y, Width = 92, Height = 32 };

            approve.Click += (s, e) => { _Decision = ApprovalDecision.Approved; _EditedDetail = editBox.Text; Close(); };
            edit.Click += (s, e) => { _Decision = ApprovalDecision.Edited; _EditedDetail = editBox.Text; Close(); };
            cancel.Click += (s, e) => { _Decision = ApprovalDecision.Cancelled; Close(); };
            copy.Click += (s, e) => { try { Clipboard.SetText(detailBox.Text); } catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("ApprovalCard copy: " + _ex.Message); } };

            Controls.Add(approve);
            Controls.Add(edit);
            Controls.Add(cancel);
            Controls.Add(copy);

            AcceptButton = approve;
            CancelButton = cancel;
        }

        int AddLabel(string caption, string value, int y, bool bold)
        {
            var cap = new Label { Left = 14, Top = y, Width = 520, Height = 16, Text = caption, ForeColor = Color.Gray };
            Controls.Add(cap);
            y += 16;
            var val = new Label
            {
                Left = 14,
                Top = y,
                Width = 520,
                Height = (value.Contains("\n") ? 32 : 16),
                Text = value,
                Font = bold ? new Font(Font, FontStyle.Bold) : Font
            };
            Controls.Add(val);
            return y + (value.Contains("\n") ? 38 : 22);
        }

        string BuildDetail(string auditNote, List<string> affectedPaths, string risk, string output)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(T("Risk: ", "风险：", "風險：") + (risk ?? T("low", "低", "低")));
            if (!string.IsNullOrEmpty(output)) sb.AppendLine(T("Output: ", "输出：", "輸出：") + output);
            sb.AppendLine(T("Affected paths:", "影响路径：", "影響路徑："));
            if (affectedPaths != null && affectedPaths.Count > 0)
                foreach (var p in affectedPaths) sb.AppendLine("  - " + p);
            else sb.AppendLine("  " + T("(none)", "（无）", "（無）"));
            if (!string.IsNullOrEmpty(auditNote))
            {
                sb.AppendLine("");
                sb.AppendLine(auditNote);
            }
            return sb.ToString();
        }

        string T(string en, string zhHans, string zhHant)
        {
            return tr(en, zhHans, zhHant);
        }

        string EmptyText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? T("(none)", "（无）", "（無）") : value;
        }

        // Convenience helper; returns the decision and the optional edit note.
        public static ApprovalDecision Show(IWin32Window owner, string title, string mode,
            List<string> requiredPermissions, List<string> affectedPaths, string risk, string output, string auditNote,
            Func<string, string, string, string> translator,
            out string editNote)
        {
            using (var card = new ApprovalCard(title, mode, requiredPermissions, affectedPaths, risk, output, auditNote, translator))
            {
                card.ShowDialog(owner);
                editNote = card.EditedDetail;
                return card.Decision;
            }
        }
    }
}
