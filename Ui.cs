namespace CuaChild;

internal static class Ui
{
    internal static readonly Color Ink = Color.FromArgb(29, 39, 53);
    internal static readonly Color Muted = Color.FromArgb(100, 116, 139);
    internal static readonly Color Accent = Color.FromArgb(15, 118, 110);
    internal static void Style(Form form, string title, Size size)
    {
        form.Text = title;
        form.Font = new Font("Microsoft YaHei UI", 9F);
        form.BackColor = Color.FromArgb(248, 250, 252);
        form.ForeColor = Ink;
        form.Icon = AppBrand.Icon;
        form.StartPosition = FormStartPosition.CenterParent;
        form.ClientSize = size;
        form.AutoScaleMode = AutoScaleMode.Dpi;
    }
    internal static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(96, 36), Padding = new Padding(12, 4, 12, 4), FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand, Margin = new Padding(6) };
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(215, 222, 230);
        button.Click += (_, _) => action();
        return button;
    }
    internal static Label Label(string text) => new() { Text = text, AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 12, 0, 6) };
    internal static TableLayoutPanel Fields() => new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Top, Padding = new Padding(20), BackColor = Color.White };
    internal static void Field(TableLayoutPanel layout, string name, Control control)
    {
        layout.Controls.Add(Label(name));
        control.Dock = DockStyle.Top;
        control.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(control);
    }
    internal static ComboBox Combo(params string[] items)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        box.Items.AddRange(items); box.SelectedIndex = 0;
        return box;
    }
}
