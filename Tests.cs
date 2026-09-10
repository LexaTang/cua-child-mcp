namespace CuaChild;
internal static class Tests
{
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
