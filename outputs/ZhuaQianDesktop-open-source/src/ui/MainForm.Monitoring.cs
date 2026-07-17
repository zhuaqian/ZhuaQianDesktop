using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Tools;

namespace ZhuaQianDesktopApp
{
    public partial class MainForm
    {
        void ShowMonitoringPanel()
        {
            string monitoringDir = Path.Combine(configDir, "monitoring");
            string eventsFile = Path.Combine(monitoringDir, "monitoring-events.jsonl");
            string casesFile = Path.Combine(monitoringDir, "monitoring-cases.jsonl");
            var collector = new ProcessSnapshotCollector(monitoringDir, eventsFile, casesFile);

            using (var dlg = new Form())
            using (var caseList = new ListBox())
            using (var eventList = new ListBox())
            using (var detailBox = new TextBox())
            using (var status = new Label())
            using (var collect = new Button())
            using (var refresh = new Button())
            using (var closeCase = new Button())
            using (var openFolder = new Button())
            using (var clearRecords = new Button())
            using (var close = new Button())
            {
                dlg.Text = Tr("Activity Monitor", "\u8fd0\u884c\u89c2\u5bdf\u53f0", "\u904b\u884c\u89c0\u5bdf\u53f0");
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Size = new Size(940, 640);
                dlg.Font = Font;
                dlg.BackColor = zqPanelBg;

                var intro = new Label
                {
                    Text = Tr("Read-only local diagnostics. It records process snapshots and review cases only when you ask it to. It does not block, hide, or upload.",
                              "\u672c\u5730\u6d3b\u52a8\u89c2\u5bdf\u4ec5\u5728\u7528\u6237\u70b9\u51fb\u65f6\u8bfb\u53d6\u8fdb\u7a0b\u5143\u6570\u636e\uff1b\u4e0d\u4f1a\u963b\u65ad\u3001\u4e0d\u4f1a\u9690\u85cf\u3001\u4e0d\u4f1a\u4e0a\u4f20\u3002",
                              "\u672c\u5730\u6d3b\u52d5\u89c0\u5bdf\u50c5\u5728\u4f7f\u7528\u8005\u9ede\u64ca\u6642\u8b80\u53d6\u8655\u7406\u7a0b\u5f0f\u5143\u8cc7\u6599\uff1b\u4e0d\u6703\u963b\u65b7\u3001\u4e0d\u6703\u96b1\u85cf\u3001\u4e0d\u6703\u4e0a\u50b3\u3002"),
                    Location = new Point(14, 12),
                    Size = new Size(890, 40),
                    ForeColor = zqMuted
                };
                dlg.Controls.Add(intro);

                var privacy = new Label
                {
                    Text = Tr("Fields: PID, process name, window title, module path when available, memory, session id, start time, and review hint. Records stay under: ",
                              "\u5b57\u6bb5\uff1aPID\u3001\u8fdb\u7a0b\u540d\u3001\u7a97\u53e3\u6807\u9898\u3001\u53ef\u7528\u65f6\u7684\u6a21\u5757\u8def\u5f84\u3001\u5185\u5b58\u3001\u4f1a\u8bdd\u3001\u542f\u52a8\u65f6\u95f4\u548c\u590d\u6838\u63d0\u793a\u3002\u8bb0\u5f55\u4fdd\u5b58\u5728\uff1a",
                              "\u6b04\u4f4d\uff1aPID\u3001\u8655\u7406\u7a0b\u5f0f\u540d\u7a31\u3001\u8996\u7a97\u6a19\u984c\u3001\u53ef\u7528\u6642\u7684\u6a21\u7d44\u8def\u5f91\u3001\u8a18\u61b6\u9ad4\u3001\u5de5\u4f5c\u968e\u6bb5\u3001\u555f\u52d5\u6642\u9593\u548c\u8907\u6838\u63d0\u793a\u3002\u8a18\u9304\u4fdd\u5b58\u5728\uff1a") + monitoringDir,
                    Location = new Point(14, 56),
                    Size = new Size(890, 44),
                    ForeColor = zqMuted
                };
                dlg.Controls.Add(privacy);

                status.SetBounds(14, 108, 890, 24);
                status.ForeColor = zqMuted;
                dlg.Controls.Add(status);

                caseList.SetBounds(14, 140, 310, 330);
                eventList.SetBounds(336, 140, 560, 210);
                detailBox.SetBounds(336, 360, 560, 110);
                detailBox.Multiline = true;
                detailBox.ReadOnly = true;
                detailBox.ScrollBars = ScrollBars.Vertical;
                StyleList(caseList);
                StyleList(eventList);
                StyleInput(detailBox);
                dlg.Controls.Add(caseList);
                dlg.Controls.Add(eventList);
                dlg.Controls.Add(detailBox);

                collect.Text = Tr("Collect Snapshot", "\u91c7\u96c6\u5feb\u7167", "\u63a1\u96c6\u5feb\u7167");
                collect.SetBounds(14, 498, 130, 32);
                refresh.Text = Tr("Refresh", "\u5237\u65b0", "\u91cd\u65b0\u6574\u7406");
                refresh.SetBounds(154, 498, 90, 32);
                closeCase.Text = Tr("Close Case", "\u5173\u95ed Case", "\u95dc\u9589 Case");
                closeCase.SetBounds(254, 498, 110, 32);
                openFolder.Text = Tr("Open Folder", "\u6253\u5f00\u76ee\u5f55", "\u958b\u555f\u76ee\u9304");
                openFolder.SetBounds(374, 498, 112, 32);
                clearRecords.Text = Tr("Clear Records", "\u6e05\u7a7a\u8bb0\u5f55", "\u6e05\u7a7a\u8a18\u9304");
                clearRecords.SetBounds(496, 498, 118, 32);
                close.Text = Tr("Close", "\u5173\u95ed", "\u95dc\u9589");
                close.SetBounds(806, 498, 90, 32);
                StyleButton(collect, ZqButtonRole.Primary);
                StyleButton(refresh, ZqButtonRole.Secondary);
                StyleButton(closeCase, ZqButtonRole.Warning);
                StyleButton(openFolder, ZqButtonRole.Secondary);
                StyleButton(clearRecords, ZqButtonRole.Warning);
                StyleButton(close, ZqButtonRole.Ghost);
                dlg.Controls.Add(collect);
                dlg.Controls.Add(refresh);
                dlg.Controls.Add(closeCase);
                dlg.Controls.Add(openFolder);
                dlg.Controls.Add(clearRecords);
                dlg.Controls.Add(close);

                Action reload = () =>
                {
                    caseList.Items.Clear();
                    eventList.Items.Clear();
                    detailBox.Text = "";
                    var snapshots = collector.Collect();
                    int suspicious = 0;
                    foreach (var s in snapshots)
                        if (!string.IsNullOrWhiteSpace(s.RiskHint)) suspicious++;
                    foreach (var c in collector.LoadCases(100))
                        caseList.Items.Add(c.caseId + " | " + c.severity + " | " + c.status + " | " + c.title);
                    foreach (var ev in collector.LoadEvents(200))
                        eventList.Items.Add(ev.at + " | " + ev.type + " | " + ev.detail + (string.IsNullOrWhiteSpace(ev.caseId) ? "" : " | " + ev.caseId));
                    status.Text = Tr("Processes: ", "\u8fdb\u7a0b\u6570\uff1a", "\u8655\u7406\u7a0b\u5f0f\u6578\uff1a") + snapshots.Count +
                        Tr("  Review hints: ", "  \u590d\u6838\u63d0\u793a\uff1a", "  \u8907\u6838\u63d0\u793a\uff1a") + suspicious + "  " + eventsFile;
                };

                collect.Click += (s, e) =>
                {
                    try
                    {
                        int written = collector.RecordSnapshot();
                        LogAction("Monitoring", "Collected process snapshot; events written=" + written);
                        RecordAction("Monitoring", "success", "Collected process snapshot; events written=" + written, eventsFile);
                        reload();
                        AppendChat("ZhuaQian", Tr("Activity snapshot recorded: ", "\u6d3b\u52a8\u5feb\u7167\u5df2\u8bb0\u5f55\uff1a", "\u6d3b\u52d5\u5feb\u7167\u5df2\u8a18\u9304\uff1a") + written + " event(s)\r\n" + monitoringDir, Color.FromArgb(0, 130, 80));
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Activity Monitor"); }
                };

                refresh.Click += (s, e) => reload();
                eventList.SelectedIndexChanged += (s, e) => detailBox.Text = Convert.ToString(eventList.SelectedItem ?? "");
                caseList.SelectedIndexChanged += (s, e) => detailBox.Text = Convert.ToString(caseList.SelectedItem ?? "");
                closeCase.Click += (s, e) =>
                {
                    string selected = Convert.ToString(caseList.SelectedItem ?? "");
                    string caseId = selected.Split('|')[0].Trim();
                    if (string.IsNullOrWhiteSpace(caseId)) return;
                    collector.CloseCase(caseId);
                    LogAction("Monitoring", "Closed monitoring case: " + caseId);
                    RecordAction("Monitoring", "closed", "Closed monitoring case: " + caseId, casesFile);
                    reload();
                };
                openFolder.Click += (s, e) =>
                {
                    Directory.CreateDirectory(monitoringDir);
                    Process.Start(new ProcessStartInfo(monitoringDir) { UseShellExecute = true });
                };
                clearRecords.Click += (s, e) =>
                {
                    string message = Tr("Clear local activity records? This deletes monitoring events and cases stored under the local folder.",
                                        "\u6e05\u7a7a\u672c\u5730\u6d3b\u52a8\u8bb0\u5f55\uff1f\u8fd9\u4f1a\u5220\u9664\u672c\u5730\u76ee\u5f55\u4e2d\u7684\u89c2\u5bdf\u4e8b\u4ef6\u548c case\u3002",
                                        "\u6e05\u7a7a\u672c\u5730\u6d3b\u52d5\u8a18\u9304\uff1f\u9019\u6703\u522a\u9664\u672c\u5730\u76ee\u9304\u4e2d\u7684\u89c0\u5bdf\u4e8b\u4ef6\u548c case\u3002");
                    if (MessageBox.Show(this, message, Tr("Clear Records", "\u6e05\u7a7a\u8bb0\u5f55", "\u6e05\u7a7a\u8a18\u9304"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;
                    collector.ClearRecords();
                    LogAction("Monitoring", "Cleared local activity monitor records");
                    RecordAction("Monitoring", "cleared", "Cleared local activity monitor records", monitoringDir);
                    reload();
                };
                close.Click += (s, e) => dlg.Close();
                reload();
                dlg.ShowDialog(this);
            }
        }
    }
}
