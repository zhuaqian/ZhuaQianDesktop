using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZhuaQianDesktopApp
{
    public partial class MainForm
    {
        void ShowPromptWorkbench()
        {
            using (var dlg = new ZhuaQianDesktopApp.Ui.PromptWorkbenchDialog(
                configDir,
                permGate,
                auditLogPath,
                input == null ? "" : input.Text,
                pendingLabels))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.AssembledPrompt))
                {
                    input.Text = dlg.AssembledPrompt;
                    LogAction("PromptWorkbench", "Inserted assembled prompt");
                    AppendChat("ZhuaQian", Tr("Prompt assembly inserted into the input box.",
                                               "Prompt 已组装并插入输入框。",
                                               "Prompt 已組裝並插入輸入框。"), Color.FromArgb(0, 130, 80));
                }
            }
        }
    }
}
