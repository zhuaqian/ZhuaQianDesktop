﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Providers;
using ZhuaQianDesktopApp.Knowledge;
using ZhuaQianDesktopApp.Tools;
using ZhuaQianDesktopApp.Documents;

namespace ZhuaQianDesktopApp
{
    public partial class MainForm : Form
    {

        void StartLiveSession()
        {
            if (!EnsurePermission(Tr("Network upload", "网络上传", "網路上傳"), permNetworkUpload, false, "Start Live Session")) return;
            if (liveActive) { MessageBox.Show(this, Tr("A live session is already active.", "已有一个进行中的实时会话。", "已有一個進行中的即時會話。"), "Live"); return; }
            if (string.IsNullOrWhiteSpace(relayUrl)) { MessageBox.Show(this, Tr("Set a relay URL first (Share via Relay).", "请先设置中继地址（通过中继分享）。", "請先設定中繼位址（透過中繼分享）。"), "Live"); return; }
            SaveCurrentTask();
            liveSessionId = RandomSessionId();
            liveRelay = relayUrl.Trim();
            byte[] snap = SnapshotTaskJson();
            try { ShareClient.PublishSession(liveRelay, liveSessionId, snap); }
            catch (Exception ex) { MessageBox.Show(this, Tr("Failed to publish session: ", "发布会话失败：", "發佈會話失敗：") + ex.Message, "Live"); return; }
            liveLastHash = SnapshotHash(snap);
            StartLiveTimer();
            LogAction("Live", "Started live session: " + ShareClient.BuildSessionUrl(liveRelay, liveSessionId));
            ShowLiveSessionDialog();
        }

        void JoinLiveSession()
        {
            if (!EnsurePermission(Tr("Network upload", "网络上传", "網路上傳"), permNetworkUpload, false, "Join Live Session")) return;
            if (liveActive) { MessageBox.Show(this, Tr("A live session is already active.", "已有一个进行中的实时会话。", "已有一個進行中的即時會話。"), "Live"); return; }
            string input = PromptUrlInput();
            if (input == null) return;
            string r; string sid;
            if (!ShareClient.TryParseSessionUrl(input, out r, out sid)) { MessageBox.Show(this, Tr("Invalid live session URL.", "实时会话链接无效。", "即時會話連結無效。"), "Live"); return; }
            try
            {
                byte[] snap = ShareClient.FetchSession(r, sid);
                ApplyRemoteSnapshot(snap, true);
                liveRelay = r; liveSessionId = sid;
                liveLastHash = SnapshotHash(snap);
                StartLiveTimer();
                LogAction("Live", "Joined live session: " + input);
                AppendChat("ZhuaQian", Tr("Joined live session. Syncing...", "已加入实时会话，同步中...", "已加入即時會話，同步中..."), ThemeManager.Success);
                ShowLiveSessionDialog();
            }
            catch (Exception ex) { MessageBox.Show(this, Tr("Failed to join session: ", "加入会话失败：", "加入會話失敗：") + ex.Message, "Live"); }
        }

        void StartLiveTimer()
        {
            liveActive = true;
            if (liveTimer == null)
            {
                liveTimer = new Timer();
                liveTimer.Interval = 4000;
                liveTimer.Tick += (s, e) => LiveTick();
            }
            liveTimer.Start();
        }

        void StopLiveSession()
        {
            if (!liveActive) return;
            liveActive = false;
            if (liveTimer != null) liveTimer.Stop();
            liveSessionId = ""; liveRelay = ""; liveLastHash = "";
            LogAction("Live", "Stopped live session");
            AppendChat("ZhuaQian", Tr("Live session stopped.", "实时会话已停止。", "即時會話已停止。"), ThemeManager.Success);
        }

        void LiveTick()
        {
            if (!liveActive || string.IsNullOrWhiteSpace(liveSessionId)) return;
            try
            {
                SaveCurrentTask();
                byte[] local = SnapshotTaskJson();
                string localHash = SnapshotHash(local);
                if (localHash != liveLastHash)
                {
                    ShareClient.PublishSession(liveRelay, liveSessionId, local);
                    liveLastHash = localHash;
                }
                else
                {
                    byte[] remote = ShareClient.FetchSession(liveRelay, liveSessionId);
                    string remoteHash = SnapshotHash(remote);
                    if (remoteHash != liveLastHash)
                    {
                        ApplyRemoteSnapshot(remote, false);
                        liveLastHash = remoteHash;
                        AppendChat("ZhuaQian", Tr("Synced from collaborator.", "已从协作者同步。", "已從協作者同步。"), ThemeManager.Success);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LiveTick error: " + ex.Message);
            }
        }

        void ShowLiveSessionDialog()
        {
            using (var dlg = new Form())
            using (var urlBox = new TextBox())
            using (var copyBtn = new Button())
            using (var stopBtn = new Button())
            {
                dlg.Text = Tr("Live Session", "实时会话", "即時會話");
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MinimizeBox = false; dlg.MaximizeBox = false;
                dlg.ClientSize = new Size(460, 150);
                dlg.Font = Font;
                urlBox.SetBounds(14, 16, 432, 24); urlBox.ReadOnly = true;
                urlBox.Text = ShareClient.BuildSessionUrl(liveRelay, liveSessionId);
                copyBtn.Text = Tr("Copy", "复制", "複製"); copyBtn.SetBounds(14, 54, 90, 30);
                copyBtn.Click += (s, e) => { try { Clipboard.SetText(urlBox.Text); } catch (Exception ex) { MessageBox.Show(dlg, ex.Message, "Copy failed"); } };
                stopBtn.Text = Tr("Stop Session", "停止会话", "停止會話"); stopBtn.DialogResult = DialogResult.OK; stopBtn.SetBounds(120, 54, 140, 30);
                dlg.Controls.Add(urlBox); dlg.Controls.Add(copyBtn); dlg.Controls.Add(stopBtn);
                dlg.FormClosing += (s, e) => StopLiveSession();
                dlg.ShowDialog(this);
            }
        }

        byte[] SnapshotTaskJson()
        {
            SaveCurrentTask();
            string p = TaskFile(currentTaskId);
            return File.Exists(p) ? File.ReadAllBytes(p) : new byte[0];
        }

        string SnapshotHash(byte[] data)
        {
            try
            {
                var d = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(data));
                if (d != null && d.ContainsKey("messages"))
                    return ShaText(json.Serialize(d["messages"]));
            }
            catch (Exception _ex) { System.Diagnostics.Debug.WriteLine("LiveSession hash fallback: " + _ex.Message); }
            return ShaText(Encoding.UTF8.GetString(data));
        }

        string ShaText(string s)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").ToLowerInvariant();
        }

        string ApplyRemoteSnapshot(byte[] data, bool asNewTask)
        {
            var d = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(data));
            if (d == null) return currentTaskId;
            if (asNewTask) currentTaskId = Guid.NewGuid().ToString("N");
            d["id"] = currentTaskId;
            File.WriteAllText(TaskFile(currentTaskId), json.Serialize(d), Encoding.UTF8);
            LoadTasks();
            LoadTask(currentTaskId);
            return currentTaskId;
        }

        string RandomSessionId()
        {
            var cs = "0123456789abcdef";
            var sb = new StringBuilder();
            using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider())
            {
                var b = new byte[12];
                rng.GetBytes(b);
                foreach (byte x in b) sb.Append(cs[x % cs.Length]);
            }
            return sb.ToString();
        }

    }
}