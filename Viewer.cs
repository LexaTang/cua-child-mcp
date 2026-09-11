using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CuaChild;

internal sealed class Viewer : Form
{
    private readonly RdpControl rdp = new() { Dock = DockStyle.Fill };
    private readonly EventWaitHandle showEvent;
    private readonly Options options;
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1500 };
    private readonly ToolStrip toolbar = new() { Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(16, 10, 12, 10), BackColor = Color.White, ImageScalingSize = new Size(24, 24), CanOverflow = true };
    private readonly ToolStripMenuItem connectButton = new("连接桌面");
    private readonly ToolStripMenuItem fullScreenButton = new("全屏");
    private readonly StatusStrip statusbar = new() { Dock = DockStyle.Fill, SizingGrip = false, BackColor = Color.White, Padding = new Padding(14, 0, 12, 0) };
    private readonly ToolStripStatusLabel sessionStatus = new("会话 —");
    private readonly ToolStripStatusLabel workerStatus = new("Worker —");
    private readonly ToolStripStatusLabel displayStatus = new("未连接");
    private readonly Label notice = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(18, 0, 8, 0), ForeColor = Color.FromArgb(155, 53, 36), BackColor = Color.FromArgb(255, 242, 235) };
    private readonly Panel noticePanel = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
    private readonly Panel canvas = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 27, 39) };
    private readonly TableLayoutPanel empty = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.FromArgb(18, 27, 39) };
    private DesktopSettings settings;
    private bool busy, polling, exiting, fullScreen, connectedOnce;
    private Rectangle savedBounds;
    private FormWindowState savedWindowState;
    private readonly Stopwatch connecting = new();
    private static string EventName => @"Local\CuaChild-view-show-" + Program.Identity;

    private Viewer(EventWaitHandle showEvent, Options options)
    {
        this.showEvent = showEvent; this.options = options;
        Ui.Style(this, "Cua Child MCP", new Size(1320, 820));
        StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(800, 500);
        try { settings = DesktopSettings.Load(); } catch { settings = new(); }
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        toolbar.Font = Font;
        toolbar.Padding = new Padding(4, 0, 4, 0);
        toolbar.BackColor = SystemColors.Control;
        toolbar.RenderMode = ToolStripRenderMode.System;
        var commands = new ToolStripDropDownButton("菜单") { AccessibleName = "远程桌面菜单" };
        toolbar.Items.Add(commands);
        connectButton.Click += (_, _) => Manage(async () => { if (rdp.Connected == 0) Connect(); else await DisconnectDisplay(); });
        commands.DropDownItems.Add(connectButton);
        var session = new ToolStripMenuItem("会话");
        session.DropDownItems.Add("重新连接画面", null, (_, _) => Manage(async () => { await DisconnectDisplay(); Connect(); }));
        session.DropDownItems.Add(new ToolStripSeparator());
        session.DropDownItems.Add("注销子会话…", null, (_, _) => EndSession(false));
        session.DropDownItems.Add("重建子会话…", null, (_, _) => EndSession(true));
        session.DropDownItems.Add(new ToolStripSeparator());
        session.DropDownItems.Add("启用系统 Child Sessions…", null, (_, _) => EnableSystem());
        commands.DropDownItems.Add(session);
        var mcp = new ToolStripMenuItem("MCP");
        mcp.DropDownItems.Add("启动 Worker", null, (_, _) => Manage(StartWorker));
        mcp.DropDownItems.Add("停止 Worker…", null, (_, _) => ChangeWorker(false));
        mcp.DropDownItems.Add("重启 Worker…", null, (_, _) => ChangeWorker(true));
        mcp.DropDownItems.Add(new ToolStripSeparator());
        mcp.DropDownItems.Add("配置管理…", null, (_, _) => OpenMcpConfig());
        commands.DropDownItems.Add(mcp);
        commands.DropDownItems.Add("远程桌面设置…", null, (_, _) => OpenSettings());
        fullScreenButton.Click += (_, _) => ToggleFullScreen(); commands.DropDownItems.Add(fullScreenButton);
        commands.DropDownItems.Add(new ToolStripSeparator());
        var more = commands;
        more.DropDownItems.Add("打开日志目录", null, (_, _) => { Directory.CreateDirectory(Program.StateDir); Process.Start(new ProcessStartInfo("explorer.exe", Host.Quote(Program.StateDir)) { UseShellExecute = true }); });
        more.DropDownItems.Add("隐藏到托盘", null, (_, _) => Hide());
        more.DropDownItems.Add("退出控制器", null, (_, _) => { exiting = true; Close(); });
        empty.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); empty.RowStyles.Add(new RowStyle(SizeType.AutoSize)); empty.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var welcome = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Anchor = AnchorStyles.None, Padding = new Padding(28), BackColor = canvas.BackColor };
        canvas.BackColor = empty.BackColor = welcome.BackColor = SystemColors.AppWorkspace;
        welcome.Controls.Add(new Label { Text = "远程桌面", ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 16), AutoSize = true, Margin = new Padding(0, 0, 0, 8) });
        welcome.Controls.Add(new Label { Text = "未连接", ForeColor = Color.White, AutoSize = true });
        empty.Controls.Add(welcome, 0, 1);
        canvas.Controls.Add(rdp); canvas.Controls.Add(empty); rdp.Visible = false; empty.BringToFront();
        var dismiss = Ui.Button("关闭", () => { noticePanel.Visible = false; root.RowStyles[1].Height = 0; }); dismiss.Dock = DockStyle.Right;
        noticePanel.Controls.Add(notice); noticePanel.Controls.Add(dismiss);
        statusbar.Items.Add(displayStatus); statusbar.Items.Add(new ToolStripStatusLabel("  ·  ")); statusbar.Items.Add(sessionStatus); statusbar.Items.Add(new ToolStripStatusLabel("  ·  ")); statusbar.Items.Add(workerStatus);
        statusbar.Items.Add(new ToolStripStatusLabel { Spring = true }); statusbar.Items.Add(new ToolStripStatusLabel("Ctrl+Alt+Home 释放键盘") { ForeColor = Ui.Muted });
        root.Controls.Add(toolbar, 0, 0); root.Controls.Add(noticePanel, 0, 1); root.Controls.Add(canvas, 0, 2); root.Controls.Add(statusbar, 0, 3); Controls.Add(root);
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示桌面", null, (_, _) => Reveal());
        menu.Items.Add("退出控制器", null, (_, _) => { exiting = true; Close(); });
        tray = new NotifyIcon { Icon = AppBrand.Icon, Text = "Cua Child MCP", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Reveal(); timer.Tick += async (_, _) => await Poll();
        KeyPreview = true; KeyDown += (_, e) => { if (e.KeyCode == Keys.F11 && !rdp.ContainsFocus) { ToggleFullScreen(); e.Handled = true; } };
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); timer.Start(); _ = Poll(); if (Opacity > 0) Activate(); }
    private void Connect()
    {
        if (rdp.Connected != 0) return;
        Native.EnableForConnection(); connecting.Restart(); empty.Visible = false; rdp.Visible = true;
        rdp.ConnectChild(settings with { KeyboardMode = settings.KeyboardMode == 2 ? (fullScreen ? 1 : 0) : settings.KeyboardMode });
    }
    private async Task Poll()
    {
        if (polling || IsDisposed) return; polling = true;
        try
        {
            if (showEvent.WaitOne(0)) Reveal();
            var state = await Task.Run(() => (Child: Native.ChildId(), Ready: Program.Ready()));
            if (IsDisposed) return;
            int connection = rdp.Connected;
            displayStatus.Text = connection == 1 ? "● 已连接" : connection == 2 ? $"正在连接 · {connecting.Elapsed.TotalSeconds:F0}s" : "○ 未连接";
            displayStatus.ForeColor = connection == 1 ? Ui.Accent : Ui.Muted;
            sessionStatus.Text = "会话 " + (state.Child?.ToString() ?? "—"); workerStatus.Text = state.Ready ? "Worker 就绪" : "Worker 未就绪";
            workerStatus.ForeColor = state.Ready ? Ui.Accent : Ui.Muted;
            connectButton.Text = connection == 0 ? "连接桌面" : connection == 2 ? "取消连接" : "断开画面";
            rdp.Visible = connection != 0; empty.Visible = connection == 0;
            if (connection == 1 && !connectedOnce) { connectedOnce = true; if (ContainsFocus) rdp.Focus(); }
        }
        catch (Exception ex) { if (!IsDisposed) ShowNotice(ex.Message); }
        finally { polling = false; }
    }
    private void ShowNotice(string text) { notice.Text = text; root.RowStyles[1].Height = 54; noticePanel.Visible = true; }
    private async void Manage(Func<Task> action)
    {
        if (busy) return; busy = true; toolbar.Enabled = false; empty.Enabled = false;
        try { await action(); }
        catch (Exception ex) { if (!IsDisposed) ShowNotice(ex.Message); }
        finally { busy = false; if (!IsDisposed) { toolbar.Enabled = true; empty.Enabled = true; await Poll(); } }
    }
    private async Task DisconnectDisplay()
    {
        rdp.DisconnectChild(); var wait = Stopwatch.StartNew();
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
        if (busy || MessageBox.Show(this, "这会中断当前 MCP 调用。继续？", restart ? "重启 Worker" : "停止 Worker", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Manage(async () => { await Task.Run(() => SessionManager.Stop(false)); if (restart) await StartWorker(); });
    }
    private void EndSession(bool restart)
    {
        if (busy || MessageBox.Show(this, "子桌面内所有程序将关闭，未保存内容可能丢失。继续？", restart ? "重建子会话" : "注销子会话", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Manage(async () => { await DisconnectDisplay(); await Task.Run(() => SessionManager.Stop(true)); if (restart) Connect(); });
    }
    private void OpenSettings()
    {
        if (busy) return;
        using var dialog = new DesktopSettingsDialog(settings);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        settings = dialog.Value;
        if (dialog.Reconnect) Manage(async () => { await DisconnectDisplay(); Connect(); });
    }
    private void OpenMcpConfig() { if (busy) return; using var dialog = new McpConfigDialog(options); dialog.ShowDialog(this); }
    private void ToggleFullScreen()
    {
        try {
            if (!fullScreen) { savedBounds = Bounds; savedWindowState = WindowState; WindowState = FormWindowState.Normal; FormBorderStyle = FormBorderStyle.None; Bounds = Screen.FromControl(this).Bounds; }
            else { FormBorderStyle = FormBorderStyle.Sizable; Bounds = savedBounds; WindowState = savedWindowState; }
            fullScreen = !fullScreen; fullScreenButton.Text = fullScreen ? "退出全屏" : "全屏";
            if (rdp.Connected == 1 && settings.KeyboardMode == 2) rdp.SetKeyboardMode(fullScreen ? 1 : 0);
        } catch (Exception ex) { ShowNotice(ex.Message); }
    }
    private void EnableSystem() => Manage(async () =>
    {
        var info = Program.StartInfo(Environment.ProcessPath!, "enable"); info.UseShellExecute = true; info.Verb = "runas"; info.WindowStyle = ProcessWindowStyle.Hidden;
        using var process = Process.Start(info) ?? throw new IOException("无法启动系统启用工具。"); await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new IOException("系统启用操作失败。");
        MessageBox.Show(this, "Child Sessions 已启用。首次启用后请保存工作并注销 Windows 后重新登录。", "系统设置");
    });
    private void Reveal() { Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); }
    protected override void OnFormClosing(FormClosingEventArgs e) { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } base.OnFormClosing(e); }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); tray.Dispose(); } base.Dispose(disposing); }
    internal static int VerifyIdlePanel()
    {
        uint? before = Native.ChildId(); using var signal = new EventWaitHandle(false, EventResetMode.AutoReset); using var form = new Viewer(signal, Options.Parse([]));
        form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new(-32000, -32000); form.Opacity = 0; form.Show(); Application.DoEvents();
        if (form.rdp.Connected != 0 || Native.ChildId() != before) throw new Exception("Opening the control panel changed the child connection.");
        if (Environment.GetEnvironmentVariable("CUA_UI_PREVIEW_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            Snapshot(form, Path.Combine(directory, "desktop.png"));
            using var settings = new DesktopSettingsDialog(new());
            Snapshot(settings, Path.Combine(directory, "settings.png"));
            settings.ClientSize = new Size(460, 420);
            Snapshot(settings, Path.Combine(directory, "settings-small.png"));
            using var config = new McpConfigDialog(Options.Parse([]), Path.Combine(directory, "example.toml"));
            Snapshot(config, Path.Combine(directory, "mcp-config.png"));
        }
        form.exiting = true; form.Close(); Console.Error.WriteLine("Idle desktop view verified; no child connection created."); return 0;
    }
    private static void Snapshot(Form form, string file)
    {
        form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new(-32000, -32000); form.Opacity = 0;
        form.Show(); form.PerformLayout(); Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(file);
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
