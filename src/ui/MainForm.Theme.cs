using System;
using System.Windows.Forms;

namespace ZhuaQianDesktopApp
{
    // Theming entry point for the main form. OnLoad applies the active skin to the
    // whole control tree (panels, inputs, labels, status/menu strips, chat box) and
    // re-applies whenever the user switches skins from Settings. Button roles are
    // owned by StyleButton/StyleInput/StyleList (which read the themed `zq*` fields);
    // because ThemeManager.Apply does NOT recolor Button controls, RestyleTree runs
    // afterwards and re-runs StyleButton for every button (capturing its role via
    // Tag) plus StyleInput/StyleList for inputs and lists, so role colors follow the
    // active palette on switch.
    public partial class MainForm
    {
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ThemeManager.Apply(this);
            RestyleTree(this);
            ThemeManager.ThemeChanged += (s, ev) =>
            {
                ThemeManager.Apply(this);
                RestyleTree(this);
            };
        }

        void RestyleTree(Control root)
        {
            if (root == null) return;
            if (root is Button btn)
            {
                var role = (btn.Tag is ZqButtonRole r) ? r : ZqButtonRole.Secondary;
                StyleButton(btn, role);
            }
            else if (root is TextBox || root is ComboBox)
            {
                StyleInput(root);
            }
            else if (root is ListBox lb)
            {
                StyleList(lb);
            }
            foreach (Control c in root.Controls)
                RestyleTree(c);
        }
    }
}
