using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ZhuaQianDesktopApp
{
    // Two built-in skins (Light = white/clean, Dark) plus a recursive theming
    // engine. The app already centralizes its look through the `zq*` color
    // fields and `StyleButton`/`StyleInput`/`StyleList` helpers on MainForm; this
    // manager complements (does not replace) that by re-applying semantic colors
    // to the whole control tree at load time and whenever the user switches,
    // including status/menu strips (via a custom ProfessionalColorTable) and
    // chat bubbles. All named colors are static properties so call sites (notably
    // AppendChat and the recurring semantic message colors) stay theme-aware
    // without per-control branching.
    public enum ThemeName { Light, Dark }

    public static class ThemeManager
    {
        // ---- semantic palette (read by code) ----
        public static Color FormBack { get; private set; }
        public static Color PanelBack { get; private set; }
        public static Color ControlBack { get; private set; }
        public static Color ControlFore { get; private set; }
        public static Color TextFore { get; private set; }
        public static Color MutedFore { get; private set; }
        public static Color Border { get; private set; }
        public static Color Accent { get; private set; }
        public static Color InputBack { get; private set; }
        public static Color InputFore { get; private set; }

        // chat bubble + body colors
        public static Color ChatBack { get; private set; }
        public static Color UserBubble { get; private set; }
        public static Color AssistantBubble { get; private set; }
        public static Color ErrorBubble { get; private set; }
        public static Color ChatBody { get; private set; }

        // semantic message colors (header text in AppendChat calls)
        public static Color Success { get; private set; }
        public static Color Error { get; private set; }
        public static Color UserAccent { get; private set; }

        // extra palette tokens mirrored from the original `zq*` look
        public static Color SideBg { get; private set; }
        public static Color Warning { get; private set; }
        public static Color Danger { get; private set; }

        public static ThemeName Current { get; private set; }

        public static event EventHandler ThemeChanged;

        static ThemeManager() { Load(); ApplyPalette(); }

        static void ApplyPalette()
        {
            if (Current == ThemeName.Dark) SetDark(); else SetLight();
        }

        static void SetLight()
        {
            FormBack = Color.FromArgb(244, 244, 242);
            PanelBack = Color.FromArgb(250, 250, 248);
            ControlBack = Color.FromArgb(255, 255, 253);
            ControlFore = Color.FromArgb(31, 35, 40);
            TextFore = Color.FromArgb(31, 35, 40);
            MutedFore = Color.FromArgb(93, 99, 106);
            Border = Color.FromArgb(218, 218, 212);
            Accent = Color.FromArgb(35, 91, 255);
            InputBack = Color.FromArgb(255, 255, 253);
            InputFore = Color.FromArgb(31, 35, 40);

            ChatBack = Color.FromArgb(252, 252, 250);
            UserBubble = Color.FromArgb(232, 243, 255);
            AssistantBubble = Color.FromArgb(232, 248, 239);
            ErrorBubble = Color.FromArgb(255, 235, 235);
            ChatBody = Color.FromArgb(30, 30, 30);

            Success = Color.FromArgb(0, 130, 80);
            Error = Color.FromArgb(190, 40, 40);
            UserAccent = Color.FromArgb(30, 90, 180);
            SideBg = Color.FromArgb(239, 239, 235);
            Warning = Color.FromArgb(181, 112, 24);
            Danger = Color.FromArgb(181, 54, 43);
        }

        static void SetDark()
        {
            FormBack = Color.FromArgb(32, 34, 38);
            PanelBack = Color.FromArgb(38, 40, 45);
            ControlBack = Color.FromArgb(45, 48, 54);
            ControlFore = Color.FromArgb(222, 224, 228);
            TextFore = Color.FromArgb(224, 226, 230);
            MutedFore = Color.FromArgb(150, 154, 162);
            Border = Color.FromArgb(60, 63, 70);
            Accent = Color.FromArgb(95, 150, 240);
            InputBack = Color.FromArgb(28, 30, 34);
            InputFore = Color.FromArgb(222, 224, 228);

            ChatBack = Color.FromArgb(28, 30, 34);
            UserBubble = Color.FromArgb(44, 58, 84);
            AssistantBubble = Color.FromArgb(38, 58, 48);
            ErrorBubble = Color.FromArgb(70, 42, 44);
            ChatBody = Color.FromArgb(214, 216, 222);

            Success = Color.FromArgb(95, 200, 145);
            Error = Color.FromArgb(240, 120, 120);
            UserAccent = Color.FromArgb(120, 175, 255);
            SideBg = Color.FromArgb(34, 36, 40);
            Warning = Color.FromArgb(210, 160, 70);
            Danger = Color.FromArgb(235, 115, 105);
        }

        public static void SetTheme(ThemeName name)
        {
            Current = name;
            ApplyPalette();
            Save();
            if (ThemeChanged != null) ThemeChanged(null, EventArgs.Empty);
        }

        // ---- recursive apply ----
        public static void Apply(Control root)
        {
            if (root == null) return;
            ApplyControl(root);
            foreach (Control c in root.Controls)
                Apply(c);
        }

        static void ApplyControl(Control c)
        {
            if (c == null) return;

            // tool strips are not always in the Controls collection
            if (c is MenuStrip || c is ToolStrip || c is StatusStrip || c is ContextMenuStrip)
            {
                ApplyStrip((ToolStrip)c);
                return;
            }

            if (c is Form)
            {
                c.BackColor = FormBack;
                c.ForeColor = TextFore;
            }
            else if (c is Panel || c is GroupBox || c is TabPage ||
                     c is SplitContainer || c is TableLayoutPanel || c is FlowLayoutPanel ||
                     c is UserControl)
            {
                c.BackColor = PanelBack;
                c.ForeColor = TextFore;
            }
            else if (c is Label || c is CheckBox || c is RadioButton)
            {
                c.BackColor = PanelBack;
                c.ForeColor = (c is CheckBox || c is RadioButton) ? TextFore : TextFore;
            }
            else if (c is LinkLabel)
            {
                c.BackColor = PanelBack;
                c.ForeColor = Accent;
            }
            else if (c is TextBox || c is ComboBox || c is ListBox ||
                     c is TreeView || c is ListView)
            {
                c.BackColor = InputBack;
                c.ForeColor = InputFore;
            }
            else if (c is RichTextBox)
            {
                c.BackColor = ChatBack;
                c.ForeColor = ChatBody;
            }
            else if (c is DataGridView)
            {
                var dgv = (DataGridView)c;
                dgv.BackgroundColor = InputBack;
                dgv.GridColor = Border;
                dgv.ForeColor = InputFore;
                if (dgv.DefaultCellStyle != null)
                {
                    dgv.DefaultCellStyle.BackColor = InputBack;
                    dgv.DefaultCellStyle.ForeColor = InputFore;
                }
                if (dgv.ColumnHeadersDefaultCellStyle != null)
                {
                    dgv.ColumnHeadersDefaultCellStyle.BackColor = PanelBack;
                    dgv.ColumnHeadersDefaultCellStyle.ForeColor = TextFore;
                }
                if (dgv.RowHeadersDefaultCellStyle != null)
                {
                    dgv.RowHeadersDefaultCellStyle.BackColor = PanelBack;
                    dgv.RowHeadersDefaultCellStyle.ForeColor = TextFore;
                }
            }
            else
            {
                c.BackColor = PanelBack;
                c.ForeColor = TextFore;
            }

            // context menu strips are attached, not in Controls
            if (c.ContextMenuStrip != null)
                ApplyStrip(c.ContextMenuStrip);
        }

        static void ApplyStrip(ToolStrip strip)
        {
            if (strip == null) return;
            strip.BackColor = PanelBack;
            strip.ForeColor = TextFore;
            try { strip.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable()); }
            catch { }
            foreach (ToolStripItem item in strip.Items)
            {
                item.BackColor = PanelBack;
                item.ForeColor = TextFore;
                var dd = item as ToolStripDropDownItem;
                if (dd != null && dd.DropDown != null)
                {
                    dd.DropDown.BackColor = PanelBack;
                    dd.DropDown.ForeColor = TextFore;
                }
            }
        }

        // Custom color table so MenuStrip / ToolStrip / StatusStrip / ContextMenuStrip
        // render in the active theme instead of the OS default.
        class ThemeColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return ThemeManager.PanelBack; } }
            public override Color MenuStripGradientBegin { get { return ThemeManager.PanelBack; } }
            public override Color MenuStripGradientEnd { get { return ThemeManager.PanelBack; } }
            public override Color MenuItemSelected { get { return ThemeManager.ControlBack; } }
            public override Color MenuItemSelectedGradientBegin { get { return ThemeManager.ControlBack; } }
            public override Color MenuItemSelectedGradientEnd { get { return ThemeManager.ControlBack; } }
            public override Color MenuItemBorder { get { return ThemeManager.Border; } }
            public override Color ButtonSelectedGradientBegin { get { return ThemeManager.ControlBack; } }
            public override Color ButtonSelectedGradientEnd { get { return ThemeManager.ControlBack; } }
            public override Color ButtonCheckedGradientBegin { get { return ThemeManager.ControlBack; } }
            public override Color ButtonCheckedGradientEnd { get { return ThemeManager.ControlBack; } }
            public override Color ToolStripGradientBegin { get { return ThemeManager.PanelBack; } }
            public override Color ToolStripGradientEnd { get { return ThemeManager.PanelBack; } }
            public override Color ToolStripGradientMiddle { get { return ThemeManager.PanelBack; } }
            public override Color ToolStripContentPanelGradientBegin { get { return ThemeManager.PanelBack; } }
            public override Color ToolStripContentPanelGradientEnd { get { return ThemeManager.PanelBack; } }
            public override Color ToolStripPanelGradientBegin { get { return ThemeManager.PanelBack; } }
            public override Color ToolStripPanelGradientEnd { get { return ThemeManager.PanelBack; } }
            public override Color ImageMarginGradientBegin { get { return ThemeManager.PanelBack; } }
            public override Color ImageMarginGradientEnd { get { return ThemeManager.PanelBack; } }
            public override Color ImageMarginGradientMiddle { get { return ThemeManager.PanelBack; } }
            public override Color SeparatorDark { get { return ThemeManager.Border; } }
            public override Color SeparatorLight { get { return ThemeManager.Border; } }
            public override Color StatusStripGradientBegin { get { return ThemeManager.PanelBack; } }
            public override Color StatusStripGradientEnd { get { return ThemeManager.PanelBack; } }
        }

        // ---- persistence (separate small file beside config.json) ----
        static string FilePath
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZhuaQianDesktop");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Path.Combine(dir, "theme.json");
            }
        }

        public static void Save()
        {
            try { File.WriteAllText(FilePath, "\"" + Current.ToString() + "\""); }
            catch { }
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = File.ReadAllText(FilePath).Trim().Trim('"').Trim();
                    Current = (s == "Dark") ? ThemeName.Dark : ThemeName.Light;
                    return;
                }
            }
            catch { }
            Current = ThemeName.Light;
        }
    }
}
