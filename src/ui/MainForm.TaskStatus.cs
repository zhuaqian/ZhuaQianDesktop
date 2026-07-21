using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Agent;
using ZhuaQianDesktopApp.Core;
using ZhuaQianDesktopApp.Providers;
namespace ZhuaQianDesktopApp
{
    partial class MainForm
    {
        static int TaskStatusRank(string status)
        {
            status = NormalizeTaskStatus(status);
            if (status == "needs_input") return 0;
            if (status == "running") return 1;
            if (status == "ready_for_review") return 2;
            if (status == "failed") return 3;
            if (status == "draft") return 4;
            if (status == "done") return 5;
            return 6;
        }

        static string NormalizeTaskStatus(string status)
        {
            status = (status ?? "").Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
            if (status == "pending" || status == "created") return "draft";
            if (status == "review" || status == "ready" || status == "readyforreview") return "ready_for_review";
            if (status == "complete" || status == "completed") return "done";
            if (status == "input" || status == "needsinput") return "needs_input";
            if (status == "running" || status == "failed" || status == "done" || status == "draft" || status == "needs_input" || status == "ready_for_review")
                return status;
            return "draft";
        }

        public static string TaskStatusLabel(string status)
        {
            status = NormalizeTaskStatus(status);
            if (status == "needs_input") return "Needs input";
            if (status == "running") return "Running";
            if (status == "ready_for_review") return "Ready";
            if (status == "failed") return "Failed";
            if (status == "done") return "Done";
            return "Draft";
        }
    }
}
