using System;
using System.IO;
using System.Threading.Tasks;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Ui;

namespace ZhuaQianDesktopApp
{
    public partial class MainForm
    {
        // Epic D3: user-triggered "Full Review" action. Runs the coding-agent
        // review (workspace scan + diff + build + test) and surfaces the full
        // Plan -> Command -> Diff -> Test -> Review report in a dialog. The
        // (potentially slow) build/test runs on a background thread so the UI
        // stays responsive; the dialog appears when the review completes.
        // This is the "separate action" complement to the read-only review that
        // ExecutePlanDraft already appends to the chat after execution.
        public async void ShowCodingAgentReport(AgentPlan plan)
        {
            if (plan == null) return;
            var session = new CodingAgentSession();
            session.RootDirectory = FindRepoRoot(Directory.GetCurrentDirectory());
            CodingAgentSessionReport report = await Task.Run(() => session.Run(plan));
            if (IsDisposed || Disposing) return;
            try
            {
                using (var dlg = new CodingAgentReportDialog(report))
                    dlg.ShowDialog(this);
            }
            catch { }
        }

        // Walk up from start looking for a git repo or build script so the scan
        // covers the source tree rather than the app's data folder.
        static string FindRepoRoot(string start)
        {
            string dir = string.IsNullOrWhiteSpace(start) ? Directory.GetCurrentDirectory() : start;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, "build.ps1")))
                    return dir;
                string parent = Path.GetDirectoryName(dir);
                if (parent == dir) break;
                dir = parent;
            }
            return start ?? Directory.GetCurrentDirectory();
        }
    }
}
