using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CuaChild;

// A visible RDP client for the same Windows Child Session. No screenshots are
// streamed through MCP: native RDP delivers the user's mouse and keyboard.
internal sealed class Viewer : Form
{
    private readonly RdpControl rdp = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { AutoSize = true, Padding = new Padding(8) };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly EventWaitHandle showEvent;
    private readonly NotifyIcon tray;
    private readonly uint? originalSession;
    private bool exiting;
    private bool connectedOnce;
    private readonly Stopwatch connecting = new();
    private static string EventName => @"Local\CuaChild-view-show-" + Program.Identity;

    private Viewer(EventWaitHandle showEvent)
    {
        this.showEvent = showEvent;
        originalSession = Native.ChildId();
        Text = "Cua 子桌面控制";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new(1280, 800);
        MinimumSize = new(800, 500);
        Icon = AppBrand.Icon;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        var focus = new Button { Text = "控制子桌面", AutoSize = true };
        focus.Click += (_, _) => { rdp.Focus(); };
        var reconnect = new Button { Text = "重新连接", AutoSize = true };
        reconnect.Click += (_, _) => Connect();
        var hide = new Button { Text = "隐藏到托盘", AutoSize = true };
        hide.Click += (_, _) => Hide();
        bar.Controls.AddRange([focus, reconnect, hide, status]);
        var hint = new Label { Dock = DockStyle.Bottom, Height = 30, TextAlign = ContentAlignment.MiddleLeft,
            Text = "  点击画面后即可操作子桌面 · Ctrl+Alt+Home 释放键盘 · 关闭窗口只隐藏，应用与 MCP 继续运行" };
        Controls.Add(rdp);
        Controls.Add(bar);
        Controls.Add(hint);
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示子桌面", null, (_, _) => Reveal());
        menu.Items.Add("隐藏窗口（保持连接）", null, (_, _) => Hide());
        menu.Items.Add("退出控制器（断开显示，不注销子桌面）", null, (_, _) => { exiting = true; Close(); });
        tray = new NotifyIcon { Icon = AppBrand.Icon, Text = "Cua 子桌面控制", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Reveal();
        timer.Tick += (_, _) => Poll();
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Connect();
        timer.Start();
        Activate();
    }
    private void Connect()
    {
        try
        {
            if (rdp.Connected != 0) return;
            Native.Enable();
            status.Text = "正在连接子桌面…";
            connecting.Restart();
            rdp.ConnectChild();
        }
        catch (Exception ex) { status.Text = "连接失败：" + ex.Message; }
    }
    private void Poll()
    {
        if (showEvent.WaitOne(0)) Reveal();
        try
        {
            uint? child = Native.ChildId();
            if (rdp.Connected == 1)
            {
                if (originalSession.HasValue && child != originalSession)
                {
                    status.Text = "会话已变化，请检查当前桌面";
                    return;
                }
                status.Text = $"子会话 {child} · 已连接 · { (Program.Ready() ? "MCP 就绪" : "MCP 未就绪") }";
                if (!connectedOnce) { connectedOnce = true; rdp.Focus(); }
            }
            else if (connecting.Elapsed.TotalSeconds > 90 || connectedOnce)
                status.Text = "连接已断开，可点击“重新连接”";
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
        var info = Program.StartInfo(Environment.ProcessPath!, "viewer-host", "--driver", options.Driver);
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
            Application.Run(new Viewer(show));
            return 0;
        }
        finally { gate.ReleaseMutex(); }
    }
}
