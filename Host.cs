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
    internal RdpControl() : base("A0C63C30-F08D-4AB4-907C-34905D770C7D") { }
    internal int Connected => Convert.ToInt32(((dynamic)GetOcx()!).Connected);
    internal void ConnectChild()
    {
        dynamic rdp = GetOcx()!;
        object yes = true;
        rdp.Server = "localhost";
        rdp.DesktopWidth = 1920;
        rdp.DesktopHeight = 1080;
        rdp.ColorDepth = 32;
        rdp.AdvancedSettings7.EnableCredSspSupport = true;
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp");
        rdp.AdvancedSettings7.RDPPort = Convert.ToInt32(key?.GetValue("PortNumber", 3389) ?? 3389);
        rdp.AdvancedSettings7.SmartSizing = true;
        rdp.SecuredSettings2.KeyboardHookMode = 1;
        rdp.AdvancedSettings2.RedirectDrives = false;
        rdp.AdvancedSettings6.RedirectClipboard = false;
        ((IExtendedSettings)GetOcx()!).SetProperty("ConnectToChildSession", ref yes);
        GetOcx()!.GetType().InvokeMember("Connect", System.Reflection.BindingFlags.InvokeMethod, null, GetOcx(), null);
    }
}

internal sealed class Host : Form
{
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
