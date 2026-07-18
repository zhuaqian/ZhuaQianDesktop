using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZhuaQianDesktopApp.Tools;

namespace ZhuaQianDesktopApp.Agent
{
    // Agent command that captures the desktop (or a region) to a PNG, gated by
    // permScreenshot. This is the "observe" half of the desktop control loop:
    // the agent screenshots, a vision/LLM policy reads it, then ComputerControl
    // (permAutomationInput) acts on what it saw.
    public sealed class ScreenCaptureExecutor : IAsyncCommandExecutor
    {
        readonly DesktopScreenCapture capturer;
        readonly string shotDir;

        public ScreenCaptureExecutor(DesktopScreenCapture capturer = null, string shotDir = null)
        {
            this.capturer = capturer ?? new DesktopScreenCapture();
            this.shotDir = shotDir ?? Path.Combine(Path.GetTempPath(), "zq-screen-shots");
        }

        public string CommandType { get { return "ScreenCapture"; } }

        public CommandResult Execute(IAgentCommand command)
        {
            try { return ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex) { return CommandResult.Failed(ex.Message); }
        }

        public Task<CommandResult> ExecuteAsync(IAgentCommand command, CancellationToken token)
        {
            try
            {
                Directory.CreateDirectory(shotDir);
                string path = Path.Combine(shotDir, "desktop_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                int x = GetInt(command, "x", -1);
                int y = GetInt(command, "y", -1);
                int w = GetInt(command, "w", -1);
                int h = GetInt(command, "h", -1);
                byte[] png = capturer.Capture(path, x, y, w, h);
                return Task.FromResult(CommandResult.Ok(path, false, null, "screen-capture", png.Length, "screenshot saved: " + path));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CommandResult.Failed("screen capture failed: " + ex.Message));
            }
        }

        static int GetInt(IAgentCommand c, string key, int def)
        {
            object v; if (c.Parameters != null && c.Parameters.TryGetValue(key, out v) && v != null) { int n; if (int.TryParse(v.ToString(), out n)) return n; }
            return def;
        }
    }
}
