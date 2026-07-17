using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace ZhuaQianDesktopApp
{
    public class TaskInfo
    {
        public string Id;
        public string Title;
        public string Status;
        public string LastAction;
        public DateTime UpdatedAt;
        
        public override string ToString()
        {
            string title = string.IsNullOrWhiteSpace(Title) ? "Untitled task" : Title;
            return "[" + MainForm.TaskStatusLabel(Status) + "] " + title;
        }
    }
}
