using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ZhuaQianDesktopApp
{
    // Coordinates task lifecycle: create / load / save / status change and the
    // left-side task list. Business logic only - WinForms event wiring stays in MainForm.
    // Spec: docs/NEXT_STEP_PLAN_2026-07-11.md (extract TaskCoordinator first).
    public class TaskCoordinator
    {
        readonly MainForm form;
        readonly string tasksDir;
        readonly JavaScriptSerializer json;

        public TaskCoordinator(MainForm form, string tasksDir, JavaScriptSerializer json)
        {
            this.form = form;
            this.tasksDir = tasksDir;
            this.json = json;
        }

        string TaskFile(string id)
        {
            return Path.Combine(tasksDir, id + ".json");
        }

        public void LoadTasks()
        {
            Directory.CreateDirectory(tasksDir);
            form.tasks.Clear();

            foreach (var file in Directory.GetFiles(tasksDir, "*.json"))
            {
                try
                {
                    var data = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(file, Encoding.UTF8));
                    var info = new TaskInfo();
                    info.Id = data.ContainsKey("id") ? Convert.ToString(data["id"]) : Path.GetFileNameWithoutExtension(file);
                    info.Title = data.ContainsKey("title") ? Convert.ToString(data["title"]) : "Untitled task";
                    info.Status = MainForm.NormalizeTaskStatus(data.ContainsKey("status") ? Convert.ToString(data["status"]) : "draft");
                    info.LastAction = data.ContainsKey("lastAction") ? Convert.ToString(data["lastAction"]) : "";
                    DateTime updated;
                    if (data.ContainsKey("updatedAt") && DateTime.TryParse(Convert.ToString(data["updatedAt"]), out updated))
                        info.UpdatedAt = updated;
                    else
                        info.UpdatedAt = File.GetLastWriteTime(file);
                    form.tasks.Add(info);
                }
                catch (Exception _ex) { form.LogAction("Warning", "LoadTask: " + _ex.Message); }
            }

            SortTasks();
            RefreshTaskList();

            if (form.tasks.Count == 0)
                CreateNewTask(false);
            else
                LoadTask(form.tasks[0].Id, false);
        }

        public void RefreshTaskList()
        {
            if (form.taskList == null) return;
            string filter = form.taskSearchBox == null ? "" : form.taskSearchBox.Text.Trim();
            form.suppressTaskSelection = true;
            form.taskList.Items.Clear();
            string lastStatus = "";
            foreach (var task in form.tasks)
            {
                bool match = filter.Length == 0
                    || (task.Title ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                    || (task.LastAction ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                    || MainForm.TaskStatusLabel(task.Status).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                if (match)
                {
                    string status = MainForm.NormalizeTaskStatus(task.Status);
                    if (status != lastStatus)
                    {
                        form.taskList.Items.Add("-- " + MainForm.TaskStatusLabel(status) + " --");
                        lastStatus = status;
                    }
                    form.taskList.Items.Add(task);
                }
            }
            for (int i = 0; i < form.taskList.Items.Count; i++)
            {
                var item = form.taskList.Items[i] as TaskInfo;
                if (item != null && item.Id == form.currentTaskId)
                {
                    form.taskList.SelectedIndex = i;
                    break;
                }
            }
            form.suppressTaskSelection = false;
        }

        public void CreateNewTask()
        {
            CreateNewTask(true);
        }

        public void CreateNewTask(bool saveBefore)
        {
            if (saveBefore) SaveCurrentTask();
            form.currentTaskId = Guid.NewGuid().ToString("N");
            form.currentTaskTitle = "New task";
            form.currentTaskStatus = "draft";
            form.currentTaskLastAction = "Created";
            form.messages.Clear();
            form.pendingParts.Clear();
            form.pendingLabels.Clear();
            form.chat.Clear();
            UpdateCurrentTaskHeader();
            form.AppendChat("ZhuaQian", form.Tr("New task created. Upload a file or ask a question. Press Enter to send, Shift+Enter for a new line.",
                                                       "新任务已创建。可以上传文件或直接提问。按 Enter 发送，Shift+Enter 换行。",
                                                       "新任務已建立。可以上傳檔案或直接提問。按 Enter 傳送，Shift+Enter 換行。"), Color.FromArgb(0, 130, 80));
            SaveCurrentTask();
            RefreshTaskList();
        }

        public void LoadTask(string id)
        {
            LoadTask(id, true);
        }

        public void LoadTask(string id, bool saveBefore)
        {
            if (saveBefore) SaveCurrentTask(false);
            string file = TaskFile(id);
            if (!File.Exists(file)) return;

            try
            {
                var data = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(file, Encoding.UTF8));
                form.currentTaskId = data.ContainsKey("id") ? Convert.ToString(data["id"]) : id;
                form.currentTaskTitle = data.ContainsKey("title") ? Convert.ToString(data["title"]) : "Untitled task";
                form.currentTaskStatus = MainForm.NormalizeTaskStatus(data.ContainsKey("status") ? Convert.ToString(data["status"]) : "draft");
                form.currentTaskLastAction = data.ContainsKey("lastAction") ? Convert.ToString(data["lastAction"]) : "";
                form.messages.Clear();
                if (data.ContainsKey("messages"))
                {
                    var loaded = form.ToObjectList(data["messages"]);
                    if (loaded != null)
                    {
                        foreach (var msg in loaded) form.messages.Add(msg);
                    }
                }
                form.pendingParts.Clear();
                form.pendingLabels.Clear();
                UpdateCurrentTaskHeader();
                form.RenderMessages();
                form.RefreshAttachLabel();
                RefreshTaskList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(form, ex.Message, "Failed to load task");
            }
        }

        public void SaveCurrentTask()
        {
            SaveCurrentTask(true);
        }

        public void SaveCurrentTask(bool bumpUpdatedAt)
        {
            if (string.IsNullOrWhiteSpace(form.currentTaskId)) return;
            Directory.CreateDirectory(tasksDir);
            if (form.currentTaskTitle == "New task")
                form.currentTaskTitle = GenerateTaskTitle();
            var existing = form.tasks.Find(t => t.Id == form.currentTaskId);
            var now = DateTime.Now;
            DateTime updatedAt = now;
            if (!bumpUpdatedAt && existing != null && existing.UpdatedAt != DateTime.MinValue)
                updatedAt = existing.UpdatedAt;
            var data = new Dictionary<string, object>
            {
                { "id", form.currentTaskId },
                { "title", form.currentTaskTitle },
                { "status", form.currentTaskStatus },
                { "lastAction", form.currentTaskLastAction },
                { "createdAt", now.ToString("o") },
                { "updatedAt", updatedAt.ToString("o") },
                { "provider", form.provider },
                { "model", form.model },
                { "openRouterModel", form.openRouterModel },
                { "messages", form.messages }
            };
            File.WriteAllText(TaskFile(form.currentTaskId), json.Serialize(data), Encoding.UTF8);

            if (existing == null)
            {
                existing = new TaskInfo { Id = form.currentTaskId };
                form.tasks.Add(existing);
            }
            existing.Title = form.currentTaskTitle;
            existing.Status = form.currentTaskStatus;
            existing.LastAction = form.currentTaskLastAction;
            existing.UpdatedAt = updatedAt;
            if (bumpUpdatedAt) SortTasks();
            UpdateCurrentTaskHeader();
            RefreshTaskList();
        }

        public void SortTasks()
        {
            form.tasks.Sort((a, b) =>
            {
                int rank = MainForm.TaskStatusRank(a.Status).CompareTo(MainForm.TaskStatusRank(b.Status));
                if (rank != 0) return rank;
                return b.UpdatedAt.CompareTo(a.UpdatedAt);
            });
        }

        public void SetCurrentTaskStatus(string status, string action, bool save)
        {
            form.currentTaskStatus = MainForm.NormalizeTaskStatus(status);
            if (action != null) form.currentTaskLastAction = action;
            UpdateCurrentTaskHeader();
            if (save) SaveCurrentTask();
        }

        public string GenerateTaskTitle()
        {
            foreach (var msgObj in form.messages)
            {
                var msg = msgObj as Dictionary<string, object>;
                if (msg == null || !msg.ContainsKey("role")) continue;
                if (Convert.ToString(msg["role"]) != "user") continue;
                string text = form.PartsToText(msg.ContainsKey("parts") ? msg["parts"] : null);
                text = Regex.Replace(text ?? "", "\\s+", " ").Trim();
                if (text.Length == 0) continue;
                if (text.Length > 30) text = text.Substring(0, 30) + "...";
                return text;
            }
            return "New task";
        }

        public void RenameCurrentTask()
        {
            string value = form.PromptText("Rename task", "Task title:", form.currentTaskTitle);
            if (value == null) return;
            value = value.Trim();
            if (value.Length == 0) return;
            form.currentTaskTitle = value;
            SaveCurrentTask();
        }

        public void UpdateCurrentTaskHeader()
        {
            if (form.currentTaskLabel != null)
            {
                string suffix = string.IsNullOrWhiteSpace(form.currentTaskLastAction) ? "" : " | " + form.currentTaskLastAction;
                form.currentTaskLabel.Text = form.currentTaskTitle + "  [" + MainForm.TaskStatusLabel(form.currentTaskStatus) + "]" + suffix;
            }
            if (form.modelLabel != null) form.modelLabel.Text = form.CurrentModelLabel();
            if (form.layoutTop != null) form.layoutTop();
        }
    }
}
