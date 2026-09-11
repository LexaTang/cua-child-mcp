using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CuaChild;

internal static class SessionManager
{
    internal static string StopHostEvent => @"Local\CuaChild-stop-host-" + Program.Identity;
    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSLogoffSession(IntPtr server, uint session, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);

    internal static void ValidateTarget(uint target, uint? currentChild, int parent)
    {
        if (target == 0 || target == uint.MaxValue || target == parent || target != currentChild)
            throw new InvalidOperationException("拒绝操作：目标不是当前主桌面的子会话。");
    }
    private static void StopHost()
    {
        using var host = new Mutex(false, @"Local\CuaChild-host-" + Program.Identity);
        bool owned;
        try { owned = host.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            if (!EventWaitHandle.TryOpenExisting(StopHostEvent, out var stop))
                throw new InvalidOperationException("旧版会话宿主仍在运行，无法安全停止。请退出旧版宿主后再试。");
            using (stop) stop.Set();
            try { owned = host.WaitOne(TimeSpan.FromSeconds(10)); } catch (AbandonedMutexException) { owned = true; }
            if (!owned) throw new TimeoutException("会话宿主未在 10 秒内停止。");
        }
        host.ReleaseMutex();
    }
    private static Process? FindWorker(uint child)
    {
        using var pipe = new NamedPipeClientStream(".", Program.PipeName, PipeDirection.InOut);
        try { pipe.Connect(200); } catch (TimeoutException) { return null; } catch (IOException) { return null; }
        if (!Native.GetNamedPipeServerSessionId(pipe.SafePipeHandle, out uint session) || !GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint pid))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        ValidateTarget(session, child, Process.GetCurrentProcess().SessionId);
        var process = Process.GetProcessById(checked((int)pid));
        try
        {
            _ = process.Handle; // pin the process identity before rechecking its session
            if (process.SessionId != child || !process.ProcessName.Equals("cua-driver", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("管道服务端不是预期的子会话 Worker。");
            return process;
        }
        catch { process.Dispose(); throw; }
    }
    internal static void Stop(bool logoff)
    {
        using var gate = new Mutex(false, @"Local\CuaChild-bootstrap-" + Program.Identity);
        bool owned;
        try { owned = gate.WaitOne(TimeSpan.FromSeconds(15)); } catch (AbandonedMutexException) { owned = true; }
        if (!owned) throw new TimeoutException("MCP 正在启动，请等待启动结束后再操作。");
        try
        {
            uint? child = Native.ChildId();
            using var worker = child.HasValue ? FindWorker(child.Value) : null;
            StopHost();
            if (child.HasValue) ValidateTarget(child.Value, Native.ChildId(), Process.GetCurrentProcess().SessionId);
            if (worker is not null && !worker.HasExited)
            {
                worker.Kill(); // stop only the verified pipe server, never desktop applications
                if (!worker.WaitForExit(5000)) throw new TimeoutException("Worker 未停止。");
            }
            if (!logoff || child is null) return;
            ValidateTarget(child.Value, Native.ChildId(), Process.GetCurrentProcess().SessionId);
            if (!WTSLogoffSession(IntPtr.Zero, child.Value, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var elapsed = Stopwatch.StartNew();
            while (Native.Exists(child.Value))
            {
                if (elapsed.Elapsed.TotalSeconds > 30) throw new TimeoutException("Windows 仍在注销子会话，未创建新会话。");
                Thread.Sleep(200);
            }
        }
        finally { gate.ReleaseMutex(); }
    }
}
