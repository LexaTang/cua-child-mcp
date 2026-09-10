namespace CuaChild;
internal static class Tests
{
    internal static int RunRdp()
    {
        // Activate and release the real COM event sink without connecting to a desktop.
        for (int i = 0; i < 3; i++)
        {
            using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new(-32000, -32000), Opacity = 0 };
            var control = new RdpControl();
            form.Controls.Add(control);
            form.Show();
            control.AttachDiagnosticEvents();
            control.AttachDiagnosticEvents(); // attaching twice must be harmless
            if (!File.ReadAllText(control.DiagnosticFile).Contains("RDP event subscriptions attached"))
                throw new Exception("RDP event subscription diagnostic missing");
        }
        Console.Error.WriteLine("RDP COM event attach/dispose checks passed; no desktop connection made.");
        return 0;
    }
    internal static int Run()
    {
        if (Options.Parse([]).Command != "mcp") throw new Exception("Default command");
        if (Options.Parse(["--timeout", "15"]).Timeout != 15) throw new Exception("Timeout");
        foreach (string[] bad in new[] { new[] { "--wat" }, new[] { "--timeout", "0" }, new[] { "--driver" } })
        {
            bool failed = false;
            try { Options.Parse(bad); } catch { failed = true; }
            if (!failed) throw new Exception("Invalid options accepted");
        }
        if (Host.Quote(@"a b\") != "\"a b\\\\\"") throw new Exception("Trailing slash quoting");
        if (Host.Quote("a\"b") != "\"a\\\"b\"") throw new Exception("Embedded quote");
        Console.Error.WriteLine("6 configuration/quoting checks passed.");
        return 0;
    }
}
