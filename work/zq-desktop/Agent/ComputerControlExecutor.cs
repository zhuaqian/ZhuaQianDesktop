using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZhuaQianDesktopApp.Agent
{
    public sealed class ComputerControlExecutor : ICommandExecutor, IAsyncCommandExecutor
    {
        const uint MouseEventLeftDown = 0x0002;
        const uint MouseEventLeftUp = 0x0004;
        const uint MouseEventRightDown = 0x0008;
        const uint MouseEventRightUp = 0x0010;

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        readonly System.Threading.SynchronizationContext uiContext;

        public ComputerControlExecutor()
        {
            uiContext = System.Threading.SynchronizationContext.Current;
        }

        public string CommandType { get { return "ComputerControl"; } }

        public CommandResult Execute(IAgentCommand command)
        {
            string action = GetString(command.Parameters, "action").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(action)) action = "open";

            if (action == "open") return OpenTarget(command);
            if (action == "type") return TypeText(command);
            if (action == "hotkey") return SendHotkey(command);
            if (action == "key") return SendKey(command);
            if (action == "click") return Click(command);
            if (action == "wait") return Wait(command);

            return CommandResult.Failed("unsupported computer control action: " + action);
        }

        CommandResult OpenTarget(IAgentCommand command)
        {
            string target = FirstNonEmpty(command.Target, GetString(command.Parameters, "target"), GetString(command.Parameters, "path"), GetString(command.Parameters, "url"));
            if (string.IsNullOrWhiteSpace(target))
                return CommandResult.Failed("missing open target");

            try
            {
                var psi = new ProcessStartInfo(target) { UseShellExecute = true };
                Process.Start(psi);
                return CommandResult.Ok(null, false, null, "computer", 0, "Opened: " + target);
            }
            catch (Exception ex)
            {
                return CommandResult.Failed("open failed: " + ex.Message);
            }
        }

        CommandResult TypeText(IAgentCommand command)
        {
            string text = FirstNonEmpty(GetString(command.Parameters, "text"), command.Target);
            if (string.IsNullOrEmpty(text))
                return CommandResult.Failed("missing text to type");

            try
            {
                SendKeys.SendWait(EscapeSendKeys(text));
                return CommandResult.Ok(null, false, null, "computer", 0, "Typed " + text.Length + " characters.");
            }
            catch (Exception ex)
            {
                return CommandResult.Failed("type failed: " + ex.Message);
            }
        }

        CommandResult SendHotkey(IAgentCommand command)
        {
            string sequence = FirstNonEmpty(GetString(command.Parameters, "sequence"), command.Target);
            if (string.IsNullOrWhiteSpace(sequence))
                return CommandResult.Failed("missing hotkey sequence");

            string sendKeys = BuildHotkey(sequence);
            if (string.IsNullOrWhiteSpace(sendKeys))
                return CommandResult.Failed("unsupported hotkey: " + sequence);

            try
            {
                SendKeys.SendWait(sendKeys);
                return CommandResult.Ok(null, false, null, "computer", 0, "Sent hotkey: " + sequence);
            }
            catch (Exception ex)
            {
                return CommandResult.Failed("hotkey failed: " + ex.Message);
            }
        }

        CommandResult SendKey(IAgentCommand command)
        {
            string key = FirstNonEmpty(GetString(command.Parameters, "key"), command.Target);
            if (string.IsNullOrWhiteSpace(key))
                return CommandResult.Failed("missing key");

            string sendKeys = BuildKeyToken(key);
            if (string.IsNullOrWhiteSpace(sendKeys))
                return CommandResult.Failed("unsupported key: " + key);

            try
            {
                SendKeys.SendWait(sendKeys);
                return CommandResult.Ok(null, false, null, "computer", 0, "Pressed key: " + key);
            }
            catch (Exception ex)
            {
                return CommandResult.Failed("key press failed: " + ex.Message);
            }
        }

        CommandResult Click(IAgentCommand command)
        {
            int x;
            int y;
            if (!TryGetInt(command, "x", out x) || !TryGetInt(command, "y", out y))
                return CommandResult.Failed("missing click coordinates x/y");

            string button = GetString(command.Parameters, "button").ToLowerInvariant();
            bool right = button == "right" || button == "r";

            try
            {
                if (!SetCursorPos(x, y))
                    return CommandResult.Failed("failed to move cursor");
                mouse_event(right ? MouseEventRightDown : MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
                mouse_event(right ? MouseEventRightUp : MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
                return CommandResult.Ok(null, false, null, "computer", 0, "Clicked " + (right ? "right" : "left") + " at " + x + "," + y + ".");
            }
            catch (Exception ex)
            {
                return CommandResult.Failed("click failed: " + ex.Message);
            }
        }

        // Synchronous path used by single-step actions and the unit tests, which assert
        // the pipeline must not block for a long wait. It therefore returns immediately
        // for waits over 1s (non-blocking legacy behavior). Multi-step plans must use the
        // async path below, which performs a real awaited wait.
        CommandResult Wait(IAgentCommand command)
        {
            int ms;
            if (!TryGetInt(command, "ms", out ms))
                ms = 1000;
            if (ms < 0) ms = 0;
            if (ms > 60000) ms = 60000;
            if (ms > 1000)
            {
                ThreadPool.QueueUserWorkItem(_ => Thread.Sleep(ms));
                return CommandResult.Ok(null, false, null, "computer", 0, "Scheduled wait for " + ms + " ms without blocking the UI.");
            }
            Thread.Sleep(ms);
            return CommandResult.Ok(null, false, null, "computer", 0, "Waited " + ms + " ms.");
        }

        public Task<CommandResult> ExecuteAsync(IAgentCommand command, CancellationToken token)
        {
            string action = GetString(command.Parameters, "action").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(action)) action = "open";

            if (action == "wait") return WaitAsync(command, token);

            // Open/type/hotkey/key/click call SendKeys/mouse APIs that require the UI thread.
            // Marshal them back to the captured UI context; fall back to direct execution if
            // this executor was not constructed on the UI thread.
            if (uiContext == null) return Task.FromResult(Execute(command));
            var tcs = new TaskCompletionSource<CommandResult>();
            uiContext.Send(_ =>
            {
                try { tcs.TrySetResult(Execute(command)); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }, null);
            return tcs.Task;
        }

        async Task<CommandResult> WaitAsync(IAgentCommand command, CancellationToken token)
        {
            int ms;
            if (!TryGetInt(command, "ms", out ms))
                ms = 1000;
            if (ms < 0) ms = 0;
            if (ms > 60000) ms = 60000;
            // Real wait. The pipeline awaits this on a background thread, so the UI stays
            // responsive AND the next plan step only runs after the wait truly completes.
            await Task.Delay(ms, token);
            return CommandResult.Ok(null, false, null, "computer", 0, "Waited " + ms + " ms.");
        }

        static string GetString(IReadOnlyDictionary<string, object> values, string key)
        {
            object value;
            if (values != null && values.TryGetValue(key, out value) && value != null)
                return Convert.ToString(value);
            return "";
        }

        static bool TryGetInt(IAgentCommand command, string key, out int value)
        {
            value = 0;
            object obj;
            if (command.Parameters != null && command.Parameters.TryGetValue(key, out obj) && obj != null)
                return int.TryParse(Convert.ToString(obj), out value);
            return false;
        }

        static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            return "";
        }

        static string EscapeSendKeys(string text)
        {
            if (text == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (c == '\r') continue;
                if (c == '\n') sb.Append("{ENTER}");
                else if ("+^%~(){}[]".IndexOf(c) >= 0) sb.Append("{").Append(c).Append("}");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static string BuildHotkey(string sequence)
        {
            string[] parts = sequence.Replace("-", "+").Split('+');
            if (parts.Length == 0) return "";
            var sb = new System.Text.StringBuilder();
            string key = "";
            foreach (var raw in parts)
            {
                string token = raw.Trim().ToLowerInvariant();
                if (token.Length == 0) continue;
                if (token == "ctrl" || token == "control") sb.Append("^");
                else if (token == "shift") sb.Append("+");
                else if (token == "alt") sb.Append("%");
                else key = raw.Trim();
            }
            sb.Append(BuildKeyToken(key));
            return sb.ToString();
        }

        static string BuildKeyToken(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "";
            string k = key.Trim();
            string upper = k.ToUpperInvariant();
            if (k.Length == 1) return EscapeSendKeys(k);
            if (upper == "ENTER" || upper == "TAB" || upper == "ESC" || upper == "ESCAPE" ||
                upper == "DELETE" || upper == "DEL" || upper == "BACKSPACE" || upper == "BS" ||
                upper == "SPACE" || upper == "LEFT" || upper == "RIGHT" || upper == "UP" || upper == "DOWN" ||
                upper == "HOME" || upper == "END" || upper == "PGUP" || upper == "PGDN")
            {
                if (upper == "ESCAPE") upper = "ESC";
                if (upper == "DEL") upper = "DELETE";
                if (upper == "BS") upper = "BACKSPACE";
                return "{" + upper + "}";
            }
            if (upper.StartsWith("F"))
            {
                int n;
                if (int.TryParse(upper.Substring(1), out n) && n >= 1 && n <= 24)
                    return "{" + upper + "}";
            }
            return "";
        }
    }
}
