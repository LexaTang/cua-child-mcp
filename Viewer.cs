using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CuaChild;

// A visible RDP client for the same Windows Child Session. No screenshots are
// streamed through MCP: native RDP delivers the user's mouse and keyboard.
internal sealed class Viewer : Form
{
    internal static int VerifyIdlePanel()
    {
        uint? before = Native.ChildId();
        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset);
        using var form = new Viewer(signal, Options.Parse([]));
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new(-32000, -32000);
        form.Opacity = 0;
        form.Show();
        Application.DoEvents();
        if (form.rdp.Connected != 0 || Native.ChildId() != before)
            throw new Exception("Opening the control panel changed the child connection.");
        form.exiting = true;
        form.Close();
        Console.Error.WriteLine("Idle control panel verified: no RDP connection or child-session creation.");
        return 0;
    }
    private readonly RdpControl rdp = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { AutoSize = true, Padding = new Padding(8) };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly EventWaitHandle showEvent;
    private readonly NotifyIcon tray;
    private readonly Options options;
    private readonly FlowLayoutPanel panel = new() { Dock = DockStyle.Left, Width = 300, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12), BackColor = Color.FromArgb(244, 246, 249) };
    private readonly NumericUpDown desktopWidth = new() { Minimum = 640, Maximum = 7680, Increment = 160, Width = 250 };
    private readonly NumericUpDown desktopHeight = new() { Minimum = 480, Maximum = 4320, Increment = 90, Width = 250 };
    private readonly CheckBox smartSizing = new() { Text = "画面适应窗口", AutoSize = true };
    private readonly CheckBox clipboard = new() { Text = "共享剪贴板", AutoSize = true };
    private readonly CheckBox drives = new() { Text = "重定向本机磁盘", AutoSize = true };
    private readonly ComboBox audio = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
    private readonly ComboBox keyboard = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
    private readonly ComboBox colors = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
    private bool busy, fullScreen;
    private Rectangle savedBounds;
    private FormWindowState savedWindowState;
    private bool exiting;
    private bool connectedOnce;
    private readonly Stopwatch connecting = new();
    private static string EventName => @"Local\CuaChild-view-show-" + Program.Identity;

    private Viewer(EventWaitHandle showEvent, Options options)
    {
        this.showEvent = showEvent;
        this.options = options;
        Text = "Cua Child MCP · 控制中心";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new(1440, 900);
        MinimumSize = new(1000, 650);
        Icon = AppBrand.Icon;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        var focus = new Button { Text = "控制子桌面", AutoSize = true };
        focus.Click += (_, _) => { rdp.Focus(); };
        var reconnect = new Button { Text = "重新连接", AutoSize = true };
        reconnect.Click += (_, _) => Manage(async () => { await DisconnectDisplay(); Connect(); });
        var hide = new Button { Text = "隐藏到托盘", AutoSize = true };
        hide.Click += (_, _) => Hide();
        var logs = new Button { Text = "打开诊断日志", AutoSize = true };
        logs.Click += (_, _) =>
        {
            Directory.CreateDirectory(Program.StateDir);
            Process.Start(new ProcessStartInfo("explorer.exe", Host.Quote(Program.StateDir)) { UseShellExecute = true });
        };
        bar.Controls.AddRange([focus, reconnect, hide, logs, status]);
        var hint = new Label { Dock = DockStyle.Bottom, Height = 30, TextAlign = ContentAlignment.MiddleLeft,
            Text = "  点击画面后即可操作子桌面 · Ctrl+Alt+Home 释放键盘 · 关闭窗口只隐藏，应用与 MCP 继续运行" };
        Controls.Add(rdp);
        Controls.Add(panel);
        Controls.Add(bar);
        Controls.Add(hint);
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示子桌面", null, (_, _) => Reveal());
        menu.Items.Add("隐藏窗口（保持连接）", null, (_, _) => Hide());
        menu.Items.Add("退出控制器（断开显示，不注销子桌面）", null, (_, _) => { exiting = true; Close(); });
        tray = new NotifyIcon { Icon = AppBrand.Icon, Text = "Cua 子桌面控制", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Reveal();
        timer.Tick += (_, _) => Poll();
        Section("子会话（打开面板不会自动启动）");
        AddButton("启动 / 连接子桌面", () => Connect());
        AddButton("断开画面（保留会话）", () => Manage(DisconnectDisplay));
        AddButton("注销子会话…", () => EndSession(false));
        AddButton("重建子会话…", () => EndSession(true));
        Section("MCP Worker");
        AddButton("启动 Worker", () => Manage(StartWorker));
        AddButton("停止 Worker…", () => ChangeWorker(false));
        AddButton("重启 Worker…", () => ChangeWorker(true));
        AddButton("复制 MCP 配置", CopyConfig);
        Section("显示设置（保存后重连生效）");
        panel.Controls.Add(new Label { Text = "宽度 / 高度（像素）", AutoSize = true });
        panel.Controls.AddRange([desktopWidth, desktopHeight]);
        var presets = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        presets.Items.AddRange(["选择分辨率预设", "1280 × 720", "1920 × 1080", "2560 × 1440", "3840 × 2160"]);
        presets.SelectedIndex = 0;
        presets.SelectedIndexChanged += (_, _) => { (int W, int H)[] sizes = [(1280,720),(1920,1080),(2560,1440),(3840,2160)]; if (presets.SelectedIndex > 0) { var s = sizes[presets.SelectedIndex - 1]; desktopWidth.Value = s.W; desktopHeight.Value = s.H; } };
        panel.Controls.Add(presets);
        colors.Items.AddRange(["32 位颜色", "16 位颜色"]);
        audio.Items.AddRange(["音频：本机播放", "音频：子桌面播放", "音频：静音"]);
        keyboard.Items.AddRange(["组合键：本机", "组合键：子桌面", "组合键：仅窗口全屏"]);
        panel.Controls.AddRange([colors, smartSizing, clipboard, drives, audio, keyboard]);
        AddButton("保存设置", () => Manage(() => { Settings().Save(); return Task.CompletedTask; }));
        AddButton("保存并重新连接画面", () => Manage(async () => { Settings().Save(); await DisconnectDisplay(); Connect(); }));
        AddButton("切换全屏 / 窗口", ToggleFullScreen);
        AddButton("启用系统 Child Sessions…", EnableSystem);
        DesktopSettings settings;
        try { settings = DesktopSettings.Load(); } catch { settings = new(); }
        desktopWidth.Value = settings.Width; desktopHeight.Value = settings.Height;
        colors.SelectedIndex = settings.ColorDepth == 32 ? 0 : 1;
        smartSizing.Checked = settings.SmartSizing; clipboard.Checked = settings.Clipboard; drives.Checked = settings.Drives;
        audio.SelectedIndex = settings.AudioMode; keyboard.SelectedIndex = settings.KeyboardMode;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.F11 && !rdp.ContainsFocus) { ToggleFullScreen(); e.Handled = true; } };
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        timer.Start();
        Poll();
        if (Opacity > 0) Activate();
    }
    private void Connect()
    {
        try
        {
            if (rdp.Connected != 0) return;
            Native.EnableForConnection();
            Settings().Save();
            status.Text = "正在连接子桌面…";
            connecting.Restart();
            var settings = Settings();
            rdp.ConnectChild(settings with { KeyboardMode = settings.KeyboardMode == 2 ? (fullScreen ? 1 : 0) : settings.KeyboardMode });
        }
        catch (Exception ex) { status.Text = "连接失败：" + ex.Message; }
    }
    private void Poll()
    {
        if (showEvent.WaitOne(0)) Reveal();
        try
        {
            uint? child = Native.ChildId();
            int connectionState = rdp.Connected;
            if (connectionState == 1)
            {
                status.Text = $"子会话 {child} · {rdp.LastLoginEvent ?? "RDP 已连接"} · { (Program.Ready() ? "MCP 就绪" : "MCP 未就绪") }";
                if (!connectedOnce) { connectedOnce = true; rdp.Focus(); }
            }
            else if (connectionState == 2)
                status.Text = $"正在建立 RDP 连接 · 已等待 {connecting.Elapsed.TotalSeconds:F0} 秒";
            else
                status.Text = $"子会话 {child?.ToString() ?? "未创建"} · 画面未连接 · {(Program.Ready() ? "Worker 就绪" : "Worker 未就绪")}";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
    private void Reveal()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        rdp.Focus();
    }
    private void Section(string title) => panel.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 12, 0, 6) });
    private void AddButton(string title, Action action)
    {
        var button = new Button { Text = title, Width = 250, Height = 30 };
        button.Click += (_, _) => { if (!busy) action(); };
        panel.Controls.Add(button);
    }
    private DesktopSettings Settings() => new() { Width = (int)desktopWidth.Value, Height = (int)desktopHeight.Value, ColorDepth = colors.SelectedIndex == 0 ? 32 : 16, SmartSizing = smartSizing.Checked, Clipboard = clipboard.Checked, Drives = drives.Checked, AudioMode = audio.SelectedIndex, KeyboardMode = keyboard.SelectedIndex };
    private async void Manage(Func<Task> action)
    {
        if (busy) return;
        busy = true; panel.Enabled = false;
        try { await action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { busy = false; if (!IsDisposed) { panel.Enabled = true; Poll(); } }
    }
    private async Task DisconnectDisplay()
    {
        rdp.DisconnectChild();
        var wait = Stopwatch.StartNew();
        while (rdp.Connected != 0) { if (wait.Elapsed.TotalSeconds > 15) throw new TimeoutException("显示连接未能及时断开。"); await Task.Delay(100); }
        connectedOnce = false;
    }
    private async Task StartWorker()
    {
        if (!File.Exists(options.Driver)) throw new FileNotFoundException("未找到 Driver，请使用完整便携包。", options.Driver);
        if (Native.ChildId() is not uint child || !Native.Active(child)) throw new InvalidOperationException("请先连接子桌面并完成 Windows 登录，再启动 Worker。");
        await Task.Run(() => Program.Ensure(options));
    }
    private void ChangeWorker(bool restart)
    {
        if (MessageBox.Show(this, "这会中断当前 MCP 调用，但不注销子桌面。继续？", "管理 Worker", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Manage(async () => { await Task.Run(() => SessionManager.Stop(false)); if (restart) await StartWorker(); });
    }
    private void EndSession(bool restart)
    {
        if (MessageBox.Show(this, "这会关闭子桌面中的所有程序，未保存内容可能丢失。不会注销主桌面。继续？", restart ? "重建子会话" : "注销子会话", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Manage(async () => { await DisconnectDisplay(); await Task.Run(() => SessionManager.Stop(true)); if (restart) Connect(); });
    }
    private void CopyConfig()
    {
        var config = new { mcpServers = new Dictionary<string, object> { ["cua-child"] = new { command = Environment.ProcessPath, args = new[] { "mcp", "--driver", options.Driver, "--timeout", options.Timeout.ToString() } } } };
        Clipboard.SetText(System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    private void ToggleFullScreen()
    {
        if (!fullScreen) { savedBounds = Bounds; savedWindowState = WindowState; WindowState = FormWindowState.Normal; FormBorderStyle = FormBorderStyle.None; Bounds = Screen.FromControl(this).Bounds; }
        else { FormBorderStyle = FormBorderStyle.Sizable; Bounds = savedBounds; WindowState = savedWindowState; }
        fullScreen = !fullScreen;
        if (rdp.Connected == 1 && Settings().KeyboardMode == 2) rdp.SetKeyboardMode(fullScreen ? 1 : 0);
    }
    private void EnableSystem() => Manage(async () =>
    {
        var info = Program.StartInfo(Environment.ProcessPath!, "enable");
        info.UseShellExecute = true; info.Verb = "runas"; info.WindowStyle = ProcessWindowStyle.Hidden;
        using var process = Process.Start(info) ?? throw new IOException("无法启动系统启用工具。");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new IOException("系统启用操作失败。");
        MessageBox.Show(this, "Child Sessions 已启用。首次启用后请保存工作并注销 Windows 后重新登录。", "系统设置");
    });
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); tray.Dispose(); }
        base.Dispose(disposing);
    }
    internal static int Launch(Options options)
    {
        if (EventWaitHandle.TryOpenExisting(EventName, out var existing))
        {
            using (existing) existing.Set();
            return 0;
        }
        var info = Program.StartInfo(Environment.ProcessPath!, "viewer-host", "--driver", options.Driver, "--timeout", options.Timeout.ToString());
        info.UseShellExecute = true;
        info.WindowStyle = ProcessWindowStyle.Hidden; // hide the console; the form explicitly shows itself
        using var process = Process.Start(info) ?? throw new IOException("Cannot start viewer");
        // The Windows hidden-console startup flag can also suppress the first
        // form show. Signal explicitly once the UI event exists.
        var wait = Stopwatch.StartNew();
        while (wait.Elapsed.TotalSeconds < 15)
        {
            if (EventWaitHandle.TryOpenExisting(EventName, out var ready))
            {
                using (ready) ready.Set();
                return 0;
            }
            if (process.HasExited) throw new IOException("Viewer exited before opening its window");
            Thread.Sleep(100);
        }
        throw new TimeoutException("Viewer did not become ready within 15 seconds");
    }
    [DllImport("kernel32.dll")] private static extern bool FreeConsole();
    internal static int Run(Options options)
    {
        using var gate = new Mutex(false, @"Local\CuaChild-view-" + Program.Identity);
        bool owned;
        try { owned = gate.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            if (EventWaitHandle.TryOpenExisting(EventName, out var existing)) { using (existing) existing.Set(); }
            return 0;
        }
        try
        {
            FreeConsole();
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            Application.EnableVisualStyles();
            Application.Run(new Viewer(show, options));
            return 0;
        }
        finally { gate.ReleaseMutex(); }
    }
}
