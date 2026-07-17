using System;

namespace ZhuaQianDesktopApp
{
    public class TaskTimelineItem
    {
        public string Title { get; set; }
        public string Status { get; set; }
        public DateTime? TimeStamp { get; set; }
        public int Step { get; set; }
        public string Details { get; set; }
        public string OutputFile { get; set; }
    }
}
