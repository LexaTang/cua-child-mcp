namespace CuaChild;

internal sealed class DesktopSettingsDialog : Form
{
    private readonly NumericUpDown width = new() { Minimum = 640, Maximum = 7680, Increment = 160 };
    private readonly NumericUpDown height = new() { Minimum = 480, Maximum = 4320, Increment = 90 };
    private readonly ComboBox colors = Ui.Combo("32 位", "16 位");
    private readonly ComboBox audio = Ui.Combo("在本机播放", "在子桌面播放", "静音");
    private readonly ComboBox keyboard = Ui.Combo("发送到本机", "发送到子桌面", "仅全屏时发送到子桌面");
    private readonly CheckBox sizing = new() { Text = "缩放画面以适应窗口", AutoSize = true };
    private readonly CheckBox clipboard = new() { Text = "共享剪贴板", AutoSize = true };
    private readonly CheckBox drives = new() { Text = "重定向本机磁盘", AutoSize = true };
    internal DesktopSettings Value { get; private set; }
    internal bool Reconnect { get; private set; }
    internal DesktopSettingsDialog(DesktopSettings settings)
    {
        Value = settings;
        Ui.Style(this, "远程桌面设置", new Size(560, 660));
        MinimumSize = new Size(440, 420);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var fields = Ui.Fields(); scroll.Controls.Add(fields);
        var preset = Ui.Combo("自定义", "1280 × 720", "1920 × 1080", "2560 × 1440", "3840 × 2160");
        preset.SelectedIndexChanged += (_, _) => { (int W, int H)[] sizes = [(1280,720),(1920,1080),(2560,1440),(3840,2160)]; if (preset.SelectedIndex > 0) { var s = sizes[preset.SelectedIndex-1]; width.Value = s.W; height.Value = s.H; } };
        Ui.Field(fields, "分辨率预设", preset);
        var dimensions = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true };
        dimensions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); dimensions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28)); dimensions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        width.Dock = height.Dock = DockStyle.Fill;
        dimensions.Controls.Add(width); dimensions.Controls.Add(new Label { Text = "×", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter }); dimensions.Controls.Add(height);
        Ui.Field(fields, "宽度 × 高度（像素）", dimensions);
        Ui.Field(fields, "颜色深度", colors);
        fields.Controls.Add(sizing);
        Ui.Field(fields, "音频", audio);
        Ui.Field(fields, "Windows 组合键", keyboard);
        fields.Controls.Add(Ui.Label("重定向")); fields.Controls.Add(clipboard); fields.Controls.Add(drives);
        fields.Controls.Add(Ui.Label("显示与重定向设置在下一次连接时生效。"));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        actions.Controls.Add(Ui.Button("保存并重连", () => Save(true), true));
        actions.Controls.Add(Ui.Button("保存", () => Save(false)));
        actions.Controls.Add(Ui.Button("取消", Close));
        root.Controls.Add(scroll, 0, 0); root.Controls.Add(actions, 0, 1); Controls.Add(root);
        width.Value = settings.Width; height.Value = settings.Height; colors.SelectedIndex = settings.ColorDepth == 32 ? 0 : 1;
        audio.SelectedIndex = settings.AudioMode; keyboard.SelectedIndex = settings.KeyboardMode;
        sizing.Checked = settings.SmartSizing; clipboard.Checked = settings.Clipboard; drives.Checked = settings.Drives;
    }
    private void Save(bool reconnect)
    {
        try
        {
            var value = new DesktopSettings { Width = (int)width.Value, Height = (int)height.Value, ColorDepth = colors.SelectedIndex == 0 ? 32 : 16, AudioMode = audio.SelectedIndex, KeyboardMode = keyboard.SelectedIndex, SmartSizing = sizing.Checked, Clipboard = clipboard.Checked, Drives = drives.Checked };
            value.Save(); Value = value; Reconnect = reconnect; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法保存设置"); }
    }
}
