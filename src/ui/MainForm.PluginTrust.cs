using System;
using System.Text;
using System.Windows.Forms;
using ZhuaQianDesktopApp.Plugins;

namespace ZhuaQianDesktopApp
{
    public partial class MainForm
    {
        // "Phone app permission prompt" for plugins (roadmap 1.4). Lists the
        // capabilities a plugin declares and its trust status, then asks the user.
        // Reached from the plugin run path via the PluginRunExecutor.CapabilityConfirm
        // delegate, so the existing trusted-plugin UX gains a per-run consent step
        // without any change to the main form's line budget.
        public static bool ShowPluginCapabilityPrompt(PluginManifest m)
        {
            if (m == null) return false;
            var sb = new StringBuilder();
            sb.AppendLine("Plugin: " + (m.Name ?? m.Id ?? "(unnamed)"));
            if (!string.IsNullOrWhiteSpace(m.Publisher))
                sb.AppendLine("Publisher: " + m.Publisher + (m.Trusted ? "  [signature verified]" : "  [unverified]"));
            else
                sb.AppendLine("Publisher: (unsigned / untrusted)");
            sb.AppendLine();
            sb.AppendLine("This plugin declares it needs:");
            if (m.RequiredPermissions != null && m.RequiredPermissions.Count > 0)
                foreach (var p in m.RequiredPermissions) sb.AppendLine("  - " + p);
            else
                sb.AppendLine("  - (no permissions declared)");
            if (m.Hooks != null && m.Hooks.Count > 0)
            {
                sb.AppendLine("Observed hooks:");
                foreach (var h in m.Hooks) sb.AppendLine("  - " + h);
            }
            sb.AppendLine();
            sb.AppendLine("Allow this plugin to run with these capabilities?");
            var r = MessageBox.Show(sb.ToString(), "Plugin Permissions", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            return r == DialogResult.Yes;
        }
    }
}
