using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace CuaChild;

internal static class Native
{
    [DllImport("wtsapi32.dll", SetLastError = true)] internal static extern bool WTSGetChildSessionId(out uint id);
    [DllImport("wtsapi32.dll", SetLastError = true)] internal static extern bool WTSIsChildSessionsEnabled(out bool enabled);
    [DllImport("wtsapi32.dll", SetLastError = true)] internal static extern bool WTSEnableChildSessions(bool enabled);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetNamedPipeServerSessionId(SafePipeHandle pipe, out uint id);
    internal static uint? ChildId()
    {
        if (!WTSGetChildSessionId(out uint id)) { int error = Marshal.GetLastWin32Error(); if (error == 1168) return null; throw new Win32Exception(error); }
        return id == uint.MaxValue ? null : id;
    }
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSQuerySessionInformation(IntPtr server, uint id, int kind, out IntPtr buffer, out uint bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr buffer);
    internal static bool Active(uint id)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, id, 8, out var buffer, out _)) return false;
        try { return Marshal.ReadInt32(buffer) == 0; } finally { WTSFreeMemory(buffer); }
    }
    internal static void Enable()
    {
        if (!WTSIsChildSessionsEnabled(out bool enabled)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!enabled && !WTSEnableChildSessions(true))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Enabling Windows Child Sessions requires an administrator. Run cua-child enable once from an elevated terminal.");
    }
}

internal sealed record Options(string Command, string Driver, int Timeout)
{
    internal static Options Parse(string[] args)
    {
        string command = "mcp", driver = Path.Combine(AppContext.BaseDirectory, "driver", "cua-driver.exe");
        int timeout = 90;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "mcp" or "host" or "view" or "viewer-host" or "enable" or "status" or "self-test": command = args[i]; break;
                case "--help" or "-h": command = "help"; break;
                case "--driver": driver = Path.GetFullPath(args[++i]); break;
                case "--timeout": timeout = int.Parse(args[++i]); break;
                default: throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }
        if (timeout < 1 || timeout > 600) throw new ArgumentException("--timeout must be 1..600 seconds");
        return new(command, driver, timeout);
    }
}

internal static class Program
{
    internal static readonly string Identity = WindowsIdentity.GetCurrent().User!.Value + "-" + Process.GetCurrentProcess().SessionId;
    internal static readonly string PipeName = "cua-child-" + Identity;
    internal static readonly string Socket = @"\\.\pipe\" + PipeName;
    internal static readonly string StateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CuaChild", Identity);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            if (options.Command == "help")
            {
                Console.WriteLine("cua-child [mcp|view|status|enable] [--driver <cua-driver.exe>] [--timeout <seconds>]\nDefault: ensure a Windows Child Session worker, then proxy upstream stdio MCP.\nThe child session stays alive after MCP disconnects. enable may require elevation.");
                return 0;
            }
            if (options.Command == "self-test") return Tests.Run();
            if (options.Command == "enable") { Native.Enable(); Console.Error.WriteLine("Child Sessions enabled."); return 0; }
            if (options.Command == "status")
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { childSessionId = Native.ChildId(), workerReady = Ready(), socket = Socket }));
                return 0;
            }
            if (!File.Exists(options.Driver)) throw new FileNotFoundException("Bundled driver missing; run package.ps1 or pass --driver.", options.Driver);
            if (Process.GetCurrentProcess().SessionId == 0) throw new InvalidOperationException("Start cua-child from a logged-in interactive parent session, not Session 0.");
            if (options.Command == "view") return Viewer.Launch(options);
            if (options.Command == "viewer-host") return Viewer.Run(options);
            if (options.Command == "host") return Host.Run(options);
            Ensure(options);
            return Proxy(options.Driver);
        }
        catch (Exception ex) { Console.Error.WriteLine($"cua-child: {ex.Message}"); return 1; }
    }

    // Readiness proves the endpoint belongs to this parent's actual child, not the console.
    internal static bool Ready()
    {
        uint? child = Native.ChildId();
        if (child is null || !Native.Active(child.Value)) return false;
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(100);
            if (!Native.GetNamedPipeServerSessionId(pipe.SafePipeHandle, out uint actual))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (actual != child || actual == Process.GetCurrentProcess().SessionId)
                throw new InvalidOperationException("Refusing a worker outside the expected Child Session.");
            return true;
        }
        catch (TimeoutException) { return false; }
        catch (IOException) { return false; }
    }

    internal static void Ensure(Options options)
    {
        if (Ready()) return;
        using var gate = new Mutex(false, @"Local\CuaChild-bootstrap-" + Identity);
        bool acquired;
        try { acquired = gate.WaitOne(TimeSpan.FromSeconds(options.Timeout)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new TimeoutException("Another client is still starting the child worker.");
        try
        {
            if (Ready()) return;
            Directory.CreateDirectory(StateDir);
            string errorFile = Path.Combine(StateDir, "host-error.txt");
            if (File.Exists(errorFile)) File.Delete(errorFile);
            // A separate host owns RDP, so stdin EOF never tears down the desktop.
            var hostInfo = StartInfo(Environment.ProcessPath!, "host", "--driver", options.Driver, "--timeout", options.Timeout.ToString());
            hostInfo.UseShellExecute = true;
            hostInfo.WindowStyle = ProcessWindowStyle.Hidden;
            using var host = Process.Start(hostInfo)
                ?? throw new IOException("Cannot start child session host.");
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed.TotalSeconds < options.Timeout)
            {
                if (Ready()) return;
                if (File.Exists(errorFile)) throw new InvalidOperationException(File.ReadAllText(errorFile));
                Thread.Sleep(200);
            }
            throw new TimeoutException($"Child worker did not become ready. See {StateDir}");
        }
        finally { gate.ReleaseMutex(); }
    }

    internal static ProcessStartInfo StartInfo(string executable, params string[] args)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return info;
    }

    internal static int Proxy(string driver)
    {
        var info = StartInfo(driver, "mcp", "--socket", Socket);
        info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = true;
        using var proxy = Process.Start(info) ?? throw new IOException("Cannot start upstream MCP proxy.");
        // Byte streams preserve UTF-8, IDs, unknown methods, notifications and image payloads.
        var output = proxy.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
        var errors = proxy.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
        _ = Task.Run(async () =>
        {
            try { await Console.OpenStandardInput().CopyToAsync(proxy.StandardInput.BaseStream); proxy.StandardInput.Close(); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        });
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; try { proxy.Kill(true); } catch (InvalidOperationException) { } };
        Console.CancelKeyPress += cancel;
        try { proxy.WaitForExit(); Task.WaitAll(output, errors); return proxy.ExitCode; }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
