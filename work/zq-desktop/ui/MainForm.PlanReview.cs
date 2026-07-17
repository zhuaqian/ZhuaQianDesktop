using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ZhuaQianDesktopApp
{
    public partial class MainForm
    {
        void ShowPlanReview()
        {
            string initial = PlanReviewInitialText();
            using (var dlg = new ZhuaQianDesktopApp.Ui.PlanReviewDialog(initial))
            {
                DialogResult result = dlg.ShowDialog(this);
                if (result == DialogResult.Retry || dlg.RequestPlanPrompt)
                {
                    PrepareAgentPlan();
                    return;
                }
                if (result == DialogResult.Yes || dlg.RequestExecute)
                {
                    input.Text = dlg.PlanSource;
                    ExecutePlanDraft();
                    return;
                }
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.ReviewMarkdown))
                {
                    input.Text = dlg.ReviewMarkdown;
                    LogAction("PlanReview", "Inserted structured plan review");
                    AppendChat("ZhuaQian", "Structured plan review inserted into the input box.", Color.FromArgb(0, 130, 80));
                }
            }
        }

        string PlanReviewInitialText()
        {
            string value = input == null ? "" : input.Text.Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value;

            for (int i = messages.Count - 1; i >= 0; i--)
            {
                var msg = messages[i] as Dictionary<string, object>;
                if (msg == null) continue;
                string role = msg.ContainsKey("role") ? Convert.ToString(msg["role"]) : "";
                if (!string.Equals(role, "model", StringComparison.OrdinalIgnoreCase)) continue;
                if (!msg.ContainsKey("parts")) continue;
                var parts = msg["parts"] as ArrayList;
                if (parts == null) continue;
                foreach (var partObj in parts)
                {
                    var part = partObj as Dictionary<string, object>;
                    if (part != null && part.ContainsKey("text"))
                    {
                        value = Convert.ToString(part["text"]);
                        if (!string.IsNullOrWhiteSpace(value)) return value;
                    }
                }
            }
            return "";
        }
    }
}
