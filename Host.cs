using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace CuaChild;

// Windows SDK IMsRdpExtendedSettings (IUnknown, put precedes get).
[ComImport, Guid("302D8188-0052-4807-806A-362B628F9AC5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExtendedSettings
{
    void SetProperty([MarshalAs(UnmanagedType.BStr)] string name, [In, MarshalAs(UnmanagedType.Struct)] ref object value);
    [return: MarshalAs(UnmanagedType.Struct)] object GetProperty([MarshalAs(UnmanagedType.BStr)] string name);
}
internal sealed class RdpControl : AxHost
{
    private static readonly Guid EventsId = new("336D5562-EFA8-482E-8CB3-C5C0FC7A7DB6");
    private readonly List<(int Id, Delegate Handler)> eventHandlers = new();
    private object? eventSource;
    private int? lastRecordedState;
    internal string? LastLoginEvent { get; private set; }
    internal string DiagnosticFile { get; } = Path.Combine(Program.StateDir, $"rdp-{Environment.ProcessId}.log");
    internal RdpControl() : base("A0C63C30-F08D-4AB4-907C-34905D770C7D") { }
    internal int Connected
    {
        get
        {
            int state = Convert.ToInt32(((dynamic)GetOcx()!).Connected);
            if (state != lastRecordedState) { Record($"Connected state={state} (0=disconnected, 1=connected, 2=connecting)"); lastRecordedState = state; }
            return state;
        }
    }
    internal void AttachDiagnosticEvents()
    {
        if (eventSource is not null) return;
        eventSource = GetOcx()!;
        try
        {
            Subscribe(1, (Action)(() => Record("OnConnecting")));
            Subscribe(2, (Action)(() => Record("OnConnected (transport only)")));
            Subscribe(3, (Action)(() => { LastLoginEvent = "Windows 登录已完成"; Record("OnLoginComplete"); }));
            Subscribe(4, (Action<int>)(reason => Record($"OnDisconnected reason={reason}")));
            Subscribe(10, (Action<int>)(code => Record($"OnFatalError code={code} (0x{code:X8})")));
            Subscribe(22, (Action<int>)(code =>
            {
                LastLoginEvent = $"登录事件 {code} (0x{code:X8})";
                Record($"OnLogonError code={code} (0x{code:X8})");
            }));
            Record("RDP event subscriptions attached");
        }
        catch { RemoveDiagnosticEvents(); throw; }
    }
    private void Subscribe(int id, Delegate handler)
    {
        ComEventsHelper.Combine(eventSource!, EventsId, id, handler);
        eventHandlers.Add((id, handler));
    }
    private void Record(string message)
    {
        // Diagnostics must never break COM callbacks or capture credentials.
        try
        {
            Directory.CreateDirectory(Program.StateDir);
            if (File.Exists(DiagnosticFile) && new FileInfo(DiagnosticFile).Length > 1024 * 1024)
                File.Move(DiagnosticFile, DiagnosticFile + ".previous", true);
            File.AppendAllText(DiagnosticFile, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private void RemoveDiagnosticEvents()
    {
        if (eventSource is null) return;
        foreach (var (id, handler) in eventHandlers)
        {
            try { ComEventsHelper.Remove(eventSource, EventsId, id, handler); }
            catch (COMException) { }
            catch (InvalidComObjectException) { }
        }
        eventHandlers.Clear();
        eventSource = null;
    }
    protected override void DetachSink()
    {
        RemoveDiagnosticEvents();
        base.DetachSink();
    }
    internal void DisconnectChild()
    {
        if (Connected != 0) ((dynamic)GetOcx()!).Disconnect();
    }
    internal void SetKeyboardMode(int mode) => ((dynamic)GetOcx()!).SecuredSettings2.KeyboardHookMode = mode;
    internal void ConnectChild(DesktopSettings? settings = null)
    {
        settings ??= DesktopSettings.Load();
        settings.Validate();
        // Keep COM event instrumentation opt-in until real-connection compatibility
        // has been verified. Default connections use the original no-sink path.
        bool traceEvents = Environment.GetEnvironmentVariable("CUA_CHILD_RDP_EVENTS") == "1";
        if (traceEvents) AttachDiagnosticEvents();
        LastLoginEvent = null;
        dynamic rdp = GetOcx()!;
        object yes = true;
        rdp.Server = "localhost";
        rdp.DesktopWidth = settings.Width;
        rdp.DesktopHeight = settings.Height;
        rdp.ColorDepth = settings.ColorDepth;
        rdp.AdvancedSettings7.EnableCredSspSupport = true;
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp");
        rdp.AdvancedSettings7.RDPPort = Convert.ToInt32(key?.GetValue("PortNumber", 3389) ?? 3389);
        rdp.AdvancedSettings7.SmartSizing = settings.SmartSizing;
        rdp.SecuredSettings2.KeyboardHookMode = settings.KeyboardMode;
        rdp.SecuredSettings2.AudioRedirectionMode = settings.AudioMode;
        rdp.AdvancedSettings2.RedirectDrives = settings.Drives;
        rdp.AdvancedSettings6.RedirectClipboard = settings.Clipboard;
        ((IExtendedSettings)GetOcx()!).SetProperty("ConnectToChildSession", ref yes);
        Record($"ConnectChild: exe={Environment.ProcessPath}, pid={Environment.ProcessId}, os={Environment.OSVersion.Version}, parent={Process.GetCurrentProcess().SessionId}, server=localhost, configuredRdpPort={rdp.AdvancedSettings7.RDPPort}, ConnectToChildSession set=true, CredSSP=true, eventTrace={traceEvents}. Configured port is not proof of the child transport's actual endpoint.");
        GetOcx()!.GetType().InvokeMember("Connect", System.Reflection.BindingFlags.InvokeMethod, null, GetOcx(), null);
    }
}

internal sealed class Host : Form
{
    private readonly EventWaitHandle stopRequest = new(false, EventResetMode.AutoReset, SessionManager.StopHostEvent);
    private readonly Options options;
    private readonly RdpControl rdp = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    private readonly Stopwatch startup = Stopwatch.StartNew();
    private bool workerLaunched;
    private bool workerSeen;
    private Exception? launchError;
    private string? taskName;
    private Host(Options options)
    {
        this.options = options;
        Text = "Cua Child Session Host";
        Icon = AppBrand.Icon;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Location = new(-32000, -32000);
        ClientSize = new(1920, 1080);
        Controls.Add(rdp);
        timer.Tick += Tick;
    }
    protected override bool ShowWithoutActivation => true;
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            // Reuse an existing child (including one hosted by another application).
            if (Native.ChildId() is not uint existing || !Native.Active(existing)) { Native.EnableForConnection(); rdp.ConnectChild(); }
            timer.Start();
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void Tick(object? sender, EventArgs e)
    {
        try
        {
            if (stopRequest.WaitOne(0)) { timer.Stop(); CleanupTask(); Close(); return; }
            if (Program.Ready())
            {
                workerSeen = true;
                CleanupTask();
                return;
            }
            if (workerSeen) throw new IOException("Child worker or session stopped. Restart the MCP client to reconnect.");
            if (startup.Elapsed.TotalSeconds > options.Timeout) throw new TimeoutException("RDP/worker startup timed out. " + Native.CredentialHelp + " Check host-error.txt and Windows Task Scheduler.", launchError);
            if (!workerLaunched && Native.ChildId() is uint child && Native.Active(child))
            {
                taskName = "CuaChild-worker-" + Program.Identity;
                try { LaunchWorker(options.Driver, child, taskName); workerLaunched = true; }
                catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) { launchError = ex; }
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    // Task Scheduler's documented session-directed interactive launch avoids credentials
    // and avoids WTSQueryUserToken's LocalSystem/SeTcbPrivilege requirement.
    private static void LaunchWorker(string driver, uint sessionId, string name)
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        service.Connect();
        dynamic root = service.GetFolder(@"\");
        dynamic definition = service.NewTask(0);
        string user = WindowsIdentity.GetCurrent().Name;
        definition.Principal.UserId = user;
        definition.Principal.LogonType = 3; // TASK_LOGON_INTERACTIVE_TOKEN
        definition.Principal.RunLevel = 0;
        definition.Settings.Enabled = true;
        definition.Settings.AllowDemandStart = true;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = "PT0S";
        dynamic action = definition.Actions.Create(0);
        action.Path = driver;
        action.Arguments = "serve --socket " + Quote(Program.Socket);
        action.WorkingDirectory = Path.GetDirectoryName(driver)!;
        dynamic task = root.RegisterTaskDefinition(name, definition, 6, user, null, 3, null);
        task.RunEx(null, 4, checked((int)sessionId), ""); // TASK_RUN_USE_SESSION_ID
    }
    internal static string Quote(string value) => "\"" + System.Text.RegularExpressions.Regex.Replace(value, "(\\\\*)\"", "$1$1\\\"").TrimEnd('\\') + new string('\\', value.Reverse().TakeWhile(c => c == '\\').Count() * 2) + "\"";
    private void CleanupTask()
    {
        if (taskName is null) return;
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        service.Connect();
        service.GetFolder(@"\").DeleteTask(taskName, 0);
        taskName = null;
    }
    private void Fail(Exception ex)
    {
        timer.Stop();
        Directory.CreateDirectory(Program.StateDir);
        File.WriteAllText(Path.Combine(Program.StateDir, "host-error.txt"), ex.ToString());
        try { CleanupTask(); } catch { }
        Close();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); stopRequest.Dispose(); }
        base.Dispose(disposing);
    }
    internal static int Run(Options options)
    {
        using var mutex = new Mutex(false, @"Local\CuaChild-host-" + Program.Identity);
        bool owned;
        try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        if (!owned) return 0;
        try { Application.EnableVisualStyles(); Application.Run(new Host(options)); return 0; }
        finally { mutex.ReleaseMutex(); }
    }
}
