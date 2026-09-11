using System.Text.Json.Nodes;
using Tomlyn;
using Tomlyn.Model;

namespace CuaChild;

internal static class ConfigTests
{
    internal static void Run()
    {
        var entry = new McpEntry("cua.child 中文", @"C:\a b\cua-child.exe", ["mcp", "quote\"", @"C:\尾部\"], 120, 180);
        const string json = "{\"theme\":\"dark\",\"mcpServers\":{\"other\":{\"url\":\"https://example.invalid\"},\"cua.child 中文\":{\"command\":\"old\",\"env\":{\"KEEP\":\"yes\"}}}}";
        string merged = McpConfigStore.Merge(json, false, entry);
        var parsed = JsonNode.Parse(merged)!;
        if (parsed["theme"]!.GetValue<string>() != "dark" || parsed["mcpServers"]!["other"]!["url"]!.GetValue<string>() != "https://example.invalid" || parsed["mcpServers"]![entry.Name]!["env"]!["KEEP"]!.GetValue<string>() != "yes") throw new Exception("JSON sibling preservation");
        var read = McpConfigStore.Read(merged, false, entry.Name);
        if (read.Command != entry.Command || !read.Args.SequenceEqual(entry.Args)) throw new Exception("JSON path/argument roundtrip");
        const string toml = "# keep comment\nmodel = 'test-model'\n[mcp_servers.other]\nurl = 'https://example.invalid'\n[mcp_servers.\"cua.child 中文\"]\ncommand = 'old'\nenabled = false\nstartup_timeout_ms = 10000\n[mcp_servers.\"cua.child 中文\".env]\nKEEP = 'yes'\n";
        merged = McpConfigStore.Merge(toml, true, entry);
        var root = Toml.ToModel(merged); var servers = (TomlTable)root["mcp_servers"]; var target = (TomlTable)servers[entry.Name];
        if ((string)root["model"] != "test-model" || (string)((TomlTable)servers["other"])["url"] != "https://example.invalid" || (string)((TomlTable)target["env"])["KEEP"] != "yes" || (bool)target["enabled"] || target.ContainsKey("startup_timeout_ms")) throw new Exception("TOML preservation");
        read = McpConfigStore.Read(merged, true, entry.Name);
        if (read.Command != entry.Command || !read.Args.SequenceEqual(entry.Args) || read.ToolTimeout != 180) throw new Exception("TOML roundtrip");
        Reject(() => McpConfigStore.Merge("not json", false, entry));
        Reject(() => McpConfigStore.Merge("invalid toml", true, entry));
        Reject(() => McpConfigStore.Merge(json, false, entry with { Name = "other" }));
        Reject(() => McpConfigStore.Merge(toml, true, entry with { Name = "other" }));
        string directory = Path.Combine(Path.GetTempPath(), "cua-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "mcp.json");
            if (McpConfigStore.Save(path, null, json) is not null || File.ReadAllText(path) != json) throw new Exception("Create config");
            string? backup = McpConfigStore.Save(path, json, merged);
            if (backup is null || File.ReadAllText(backup) != json) throw new Exception("Backup preservation");
            File.WriteAllText(path, "externally changed");
            Reject(() => McpConfigStore.Save(path, merged, "overwrite"));
            if (File.ReadAllText(path) != "externally changed") throw new Exception("Concurrent edit was overwritten");
            Reject(() => McpConfigStore.Save(path, null, "overwrite"));
        }
        finally { foreach (string file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
        Console.Error.WriteLine("MCP config merge, escaping, preservation, backups and external-edit checks passed.");
    }
    private static void Reject(Action action)
    {
        bool rejected = false; try { action(); } catch { rejected = true; }
        if (!rejected) throw new Exception("Unsafe or malformed config accepted");
    }
}
