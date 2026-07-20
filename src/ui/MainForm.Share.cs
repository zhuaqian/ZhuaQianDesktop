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

        class ShareOptions
        {
            public bool Encrypted;
            public string Password = "";
        }

        void ShareCurrentTask()
        {
            if (!EnsurePermission(Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"), permFileWrite, false, "Share")) return;
            SaveCurrentTask();
            string taskPath = TaskFile(currentTaskId);
            if (!File.Exists(taskPath))
            {
                MessageBox.Show(this, Tr("No task to share yet.", "还没有可分享的任务。", "還沒有可分享的任務。"), "Share");
                return;
            }

            var opts = PromptShareOptions();
            if (opts == null) return;

            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = Tr("Share project as .zqp", "分享项目为 .zqp", "分享專案為 .zqp");
                sfd.Filter = "ZhuaQian Package|*.zqp|All files|*.*";
                string safe = Regex.Replace(currentTaskTitle ?? "zq-project", "[\\\\/:*?\"<>|]+", "_").Trim();
                if (safe.Length > 40) safe = safe.Substring(0, 40);
                sfd.FileName = safe + ".zqp";
                if (sfd.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    byte[] bytes = BuildPackageBytes(opts);
                    if (bytes == null) { MessageBox.Show(this, Tr("No task to share yet.", "还没有可分享的任务。", "還沒有可分享的任務。"), "Share"); return; }
                    File.WriteAllBytes(sfd.FileName, bytes);
                    LogAction("Share", "Exported package: " + sfd.FileName + (opts.Encrypted ? " (encrypted)" : " (plain)"));
                    AppendChat("ZhuaQian",
                        (opts.Encrypted ? Tr("Encrypted package saved:", "已加密分享包已保存：", "已加密分享包已儲存：")
                                        : Tr("Package saved:", "分享包已保存：", "分享包已儲存：")) + "\r\n" + sfd.FileName,
                        ThemeManager.Success);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Share failed");
                }
            }
        }

        void ImportPackage()
        {
            if (!EnsurePermission(Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"), permFileWrite, false, "Import")) return;

            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = Tr("Import .zqp package", "导入 .zqp 分享包", "匯入 .zqp 分享包");
                ofd.Filter = "ZhuaQian Package|*.zqp|All files|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;

                PackageBuilder.ImportResult result = null;
                try
                {
                    result = PackageBuilder.Import(ofd.FileName, tasksDir, "");
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    string pw = PromptPasswordInput();
                    if (pw == null) return;
                    try { result = PackageBuilder.Import(ofd.FileName, tasksDir, pw); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Import failed"); }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Import failed");
                }

                if (result != null) FinishImport(result, ofd.FileName);
            }
        }

        void FinishImport(PackageBuilder.ImportResult result, string path)
        {
            LoadTasks();
            LoadTask(result.TaskId);
            LogAction("Import", "Imported package: " + path + (result.Encrypted ? " (decrypted)" : " (plain)"));
            AppendChat("ZhuaQian",
                Tr("Imported package:", "已导入分享包：", "已匯入分享包：") + " " + result.Title + "\r\n" + path,
                ThemeManager.Success);
        }

        byte[] BuildShareSettingsJson()
        {
            var s = new Dictionary<string, object>
            {
                { "workMode", workMode },
                { "uiLanguage", uiLanguage },
                { "permissions", permGate.ToJson() }
            };
            return Encoding.UTF8.GetBytes(json.Serialize(s));
        }

        byte[] BuildPackageBytes(ShareOptions opts)
        {
            SaveCurrentTask();
            string taskPath = TaskFile(currentTaskId);
            if (!File.Exists(taskPath)) return null;
            byte[] taskJson = File.ReadAllBytes(taskPath);
            byte[] settingsJson = BuildShareSettingsJson();
            byte[] knowledgeJson = File.Exists(indexPath) ? File.ReadAllBytes(indexPath) : null;
            return PackageBuilder.BuildToBytes(currentTaskTitle, Environment.UserName, taskJson, settingsJson, knowledgeJson, opts.Encrypted, opts.Password);
        }

        void ShareOverLan()
        {
            if (!EnsurePermission(Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"), permFileWrite, false, "Share over LAN")) return;
            var opts = PromptShareOptions();
            if (opts == null) return;

            byte[] bytes = BuildPackageBytes(opts);
            if (bytes == null) { MessageBox.Show(this, Tr("No task to share yet.", "还没有可分享的任务。", "還沒有可分享的任務。"), "Share"); return; }

            var server = new LanShareServer();
            try { server.Start(bytes); }
            catch (Exception ex)
            {
                MessageBox.Show(this, Tr("LAN share failed: ", "局域网分享启动失败：", "區域網分享啟動失敗：") + ex.Message, "LAN Share");
                return;
            }
            LogAction("ShareLAN", "Started LAN share: " + server.Url + (opts.Encrypted ? " (encrypted)" : " (plain)"));
            ShowLanShareDialog(server);
        }

        void ShowLanShareDialog(LanShareServer server)
        {
            using (var dlg = new Form())
            using (var urlBox = new TextBox())
            using (var copyBtn = new Button())
            using (var stopBtn = new Button())
            {
                dlg.Text = Tr("Sharing over LAN", "局域网分享中", "區域網分享中");
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.ClientSize = new Size(460, 150);
                dlg.Font = Font;

                urlBox.SetBounds(14, 16, 432, 24);
                urlBox.ReadOnly = true;
                urlBox.Text = server.Url;
                copyBtn.Text = Tr("Copy", "复制", "複製");
                copyBtn.SetBounds(14, 54, 90, 30);
                copyBtn.Click += (s, e) =>
                {
                    try { Clipboard.SetText(server.Url); AppendChat("ZhuaQian", Tr("LAN URL copied.", "局域网地址已复制。", "區域網址已複製。"), ThemeManager.Success); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Copy failed"); }
                };
                stopBtn.Text = Tr("Stop & Close", "停止并关闭", "停止並關閉");
                stopBtn.DialogResult = DialogResult.OK;
                stopBtn.SetBounds(120, 54, 140, 30);

                dlg.Controls.Add(urlBox);
                dlg.Controls.Add(copyBtn);
                dlg.Controls.Add(stopBtn);
                dlg.FormClosing += (s, e) => server.Stop();
                dlg.ShowDialog(this);
            }
        }

        void ImportFromUrl()
        {
            if (!EnsurePermission(Tr("Write/export files", "写入/导出文件", "寫入/匯出檔案"), permFileWrite, false, "Import from URL")) return;
            string url = PromptUrlInput();
            if (url == null) return;

            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, Tr("Only http/https URLs are allowed.", "仅允许 http/https 网址。", "僅允許 http/https 網址。"), "Import from URL");
                return;
            }

            try
            {
                byte[] data;
                using (var wc = new WebClient())
                    data = wc.DownloadData(url);
                PackageBuilder.ImportResult result = null;
                try { result = PackageBuilder.ImportBytes(data, tasksDir, ""); }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    string pw = PromptPasswordInput();
                    if (pw == null) return;
                    try { result = PackageBuilder.ImportBytes(data, tasksDir, pw); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Import failed"); }
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Import failed"); }
                if (result != null) FinishImport(result, url);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Tr("Download failed: ", "下载失败：", "下載失敗：") + ex.Message, "Import from URL");
            }
        }

        string PromptUrlInput()
        {
            using (var form = new Form())
            using (var label = new Label())
            using (var url = new TextBox())
            using (var ok = new Button())
            using (var cancel = new Button())
            {
                form.Text = Tr("Import from URL", "从网址导入", "從網址匯入");
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(440, 130);
                form.Font = Font;
                label.Text = Tr("Paste the LAN/relay .zqp URL:", "粘贴局域网/中继的 .zqp 网址：", "貼上區域網/中繼的 .zqp 網址：");
                label.SetBounds(14, 14, 410, 20);
                url.SetBounds(14, 40, 410, 24);
                ok.Text = Tr("Import", "导入", "匯入");
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(270, 84, 74, 26);
                cancel.Text = Tr("Cancel", "取消", "取消");
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(354, 84, 74, 26);
                form.Controls.Add(label);
                form.Controls.Add(url);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                form.Shown += (s, e) => url.Focus();
                return form.ShowDialog(this) == DialogResult.OK && url.Text.Trim().Length > 0 ? url.Text.Trim() : null;
            }
        }

        ShareOptions PromptShareOptions()
        {
            using (var form = new Form())
            using (var encBox = new CheckBox())
            using (var pwLabel = new Label())
            using (var pw = new TextBox())
            using (var confirmLabel = new Label())
            using (var confirm = new TextBox())
            using (var ok = new Button())
            using (var cancel = new Button())
            {
                form.Text = Tr("Share Project", "分享项目", "分享專案");
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(360, 200);
                form.Font = Font;

                encBox.Text = Tr("Password-protect this package (encrypt)", "用密码保护此分享包（加密）", "用密碼保護此分享包（加密）");
                encBox.SetBounds(16, 16, 320, 24);
                pwLabel.Text = Tr("Password:", "密码：", "密碼：");
                pwLabel.SetBounds(16, 52, 320, 20);
                pw.SetBounds(16, 74, 320, 24);
                pw.UseSystemPasswordChar = true;
                pw.Enabled = false;
                confirmLabel.Text = Tr("Confirm password:", "确认密码：", "確認密碼：");
                confirmLabel.SetBounds(16, 104, 320, 20);
                confirm.SetBounds(16, 126, 320, 24);
                confirm.UseSystemPasswordChar = true;
                confirm.Enabled = false;
                ok.Text = Tr("Share", "分享", "分享");
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(190, 164, 74, 26);
                cancel.Text = Tr("Cancel", "取消", "取消");
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(272, 164, 74, 26);

                encBox.CheckedChanged += (s, e) =>
                {
                    pw.Enabled = encBox.Checked;
                    confirm.Enabled = encBox.Checked;
                    if (!encBox.Checked) { pw.Text = ""; confirm.Text = ""; }
                };

                form.Controls.Add(encBox);
                form.Controls.Add(pwLabel);
                form.Controls.Add(pw);
                form.Controls.Add(confirmLabel);
                form.Controls.Add(confirm);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                if (form.ShowDialog(this) != DialogResult.OK) return null;
                if (encBox.Checked)
                {
                    if (pw.Text.Length == 0)
                    {
                        MessageBox.Show(this, Tr("Password cannot be empty.", "密码不能为空。", "密碼不能為空。"), "Share");
                        return null;
                    }
                    if (pw.Text != confirm.Text)
                    {
                        MessageBox.Show(this, Tr("Passwords do not match.", "两次密码不一致。", "兩次密碼不一致。"), "Share");
                        return null;
                    }
                    return new ShareOptions { Encrypted = true, Password = pw.Text };
                }
                return new ShareOptions { Encrypted = false };
            }
        }

        string PromptPasswordInput()
        {
            using (var form = new Form())
            using (var label = new Label())
            using (var pw = new TextBox())
            using (var ok = new Button())
            using (var cancel = new Button())
            {
                form.Text = Tr("Enter package password", "输入分享包密码", "輸入分享包密碼");
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(320, 130);
                form.Font = Font;
                label.Text = Tr("This package is encrypted:", "该分享包已加密：", "該分享包已加密：");
                label.SetBounds(16, 14, 290, 20);
                pw.SetBounds(16, 40, 290, 24);
                pw.UseSystemPasswordChar = true;
                ok.Text = Tr("OK", "确定", "確定");
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(160, 84, 74, 26);
                cancel.Text = Tr("Cancel", "取消", "取消");
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(242, 84, 74, 26);
                form.Controls.Add(label);
                form.Controls.Add(pw);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                form.Shown += (s, e) => pw.Focus();
                return form.ShowDialog(this) == DialogResult.OK ? pw.Text : null;
            }
        }

        void ShareViaRelay()
        {
            if (!EnsurePermission(Tr("Network upload", "网络上传", "網路上傳"), permNetworkUpload, false, "Share via Relay")) return;
            var opts = PromptShareOptions();
            if (opts == null) return;

            using (var form = new Form())
            using (var urlLabel = new Label())
            using (var urlBox = new TextBox())
            using (var uploadBtn = new Button())
            using (var linkLabel = new Label())
            using (var linkBox = new TextBox())
            using (var copyBtn = new Button())
            using (var closeBtn = new Button())
            {
                form.Text = Tr("Share via Relay", "通过中继分享", "通過中繼分享");
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(460, 220);
                form.Font = Font;

                urlLabel.Text = Tr("Relay URL (e.g. http://your-server:8080):", "中继地址（如 http://your-server:8080）：", "中繼位址（如 http://your-server:8080）：");
                urlLabel.SetBounds(14, 14, 430, 20);
                urlBox.SetBounds(14, 38, 430, 24);
                urlBox.Text = relayUrl ?? "";
                uploadBtn.Text = Tr("Upload & Get Link", "上传并获取链接", "上傳並取得連結");
                uploadBtn.SetBounds(14, 74, 160, 30);
                linkLabel.Text = Tr("Share link:", "分享链接：", "分享連結：");
                linkLabel.SetBounds(14, 116, 430, 20);
                linkBox.SetBounds(14, 140, 430, 24);
                linkBox.ReadOnly = true;
                copyBtn.Text = Tr("Copy", "复制", "複製");
                copyBtn.SetBounds(14, 178, 90, 28);
                closeBtn.Text = Tr("Close", "关闭", "關閉");
                closeBtn.DialogResult = DialogResult.OK;
                closeBtn.SetBounds(354, 178, 90, 28);

                uploadBtn.Click += (s, e) =>
                {
                    string baseUrl = urlBox.Text.Trim();
                    if (baseUrl.Length == 0) { MessageBox.Show(form, Tr("Enter the relay URL first.", "请先填写中继地址。", "請先填寫中繼位址。"), "Relay"); return; }
                    try
                    {
                        relayUrl = baseUrl;
                        SaveConfig();
                        byte[] bytes = BuildPackageBytes(opts);
                        if (bytes == null) { MessageBox.Show(form, Tr("No task to share yet.", "还没有可分享的任务。", "還沒有可分享的任務。"), "Relay"); return; }
                        string link = ShareClient.Upload(bytes, baseUrl);
                        linkBox.Text = link;
                        LogAction("ShareRelay", "Uploaded to relay: " + link + (opts.Encrypted ? " (encrypted)" : " (plain)"));
                        AppendChat("ZhuaQian", Tr("Relay link ready:", "中继链接已生成：", "中繼連結已產生：") + "\r\n" + link, ThemeManager.Success);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(form, Tr("Upload failed: ", "上传失败：", "上傳失敗：") + ex.Message, "Relay");
                    }
                };
                copyBtn.Click += (s, e) =>
                {
                    if (linkBox.Text.Length > 0)
                    {
                        try { Clipboard.SetText(linkBox.Text); } catch (Exception ex) { MessageBox.Show(form, ex.Message, "Copy failed"); }
                    }
                };

                form.Controls.Add(urlLabel);
                form.Controls.Add(urlBox);
                form.Controls.Add(uploadBtn);
                form.Controls.Add(linkLabel);
                form.Controls.Add(linkBox);
                form.Controls.Add(copyBtn);
                form.Controls.Add(closeBtn);
                form.AcceptButton = uploadBtn;
                form.CancelButton = closeBtn;
                form.ShowDialog(this);
            }
        }

    }
}
