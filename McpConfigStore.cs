using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tomlyn;
using Tomlyn.Model;

namespace CuaChild;

internal sealed record McpEntry(string Name, string Command, string[] Args, int StartupTimeout = 120, int ToolTimeout = 120);
internal static class McpConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal static string CodexPath => Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home ? home : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"), "config.toml");
    private static JsonObject Json(string source) => string.IsNullOrWhiteSpace(source) ? new() : JsonNode.Parse(source) as JsonObject ?? throw new FormatException("配置根节点必须是 JSON 对象。");
    private static TomlTable TomlRoot(string source) => string.IsNullOrWhiteSpace(source) ? new() : Toml.ToModel(source);
    internal static string[] Names(string source, bool toml)
    {
        if (toml) { var root = TomlRoot(source); if (!root.TryGetValue("mcp_servers", out var servers)) return []; return (servers as TomlTable ?? throw new FormatException("mcp_servers 必须是表。")).Keys.ToArray(); }
        var json = Json(source); if (json["mcpServers"] is null) return []; return (json["mcpServers"] as JsonObject ?? throw new FormatException("mcpServers 必须是对象。")).Select(p => p.Key).ToArray();
    }
    internal static McpEntry Read(string source, bool toml, string name)
    {
        if (toml)
        {
            var root = TomlRoot(source);
            var entry = ((TomlTable)root["mcp_servers"])[name] as TomlTable ?? throw new FormatException("服务必须是 TOML 表。");
            if (entry.ContainsKey("url")) throw new InvalidOperationException("此编辑器仅编辑 stdio 服务；HTTP 服务会原样保留。");
            return new(name, entry.TryGetValue("command", out var command) ? (string)command : "", entry.TryGetValue("args", out var args) ? ((TomlArray)args).Select(x => x as string ?? throw new FormatException("参数必须是字符串。")).ToArray() : [], entry.TryGetValue("startup_timeout_sec", out var startup) ? Convert.ToInt32(startup) : 120, entry.TryGetValue("tool_timeout_sec", out var tool) ? Convert.ToInt32(tool) : 120);
        }
        var obj = Json(source)["mcpServers"]?[name] as JsonObject ?? throw new FormatException("服务必须是 JSON 对象。");
        if (obj.ContainsKey("url")) throw new InvalidOperationException("此编辑器仅编辑 stdio 服务；HTTP 服务会原样保留。");
        return new(name, obj["command"]?.GetValue<string>() ?? "", obj["args"]?.Deserialize<string[]>() ?? []);
    }
    internal static string Merge(string source, bool toml, McpEntry value)
    {
        if (string.IsNullOrWhiteSpace(value.Name) || value.Name.Any(char.IsControl) || string.IsNullOrWhiteSpace(value.Command)) throw new ArgumentException("请填写有效的服务名称和启动命令。");
        if (value.StartupTimeout < 1 || value.ToolTimeout < 1) throw new ArgumentException("超时必须大于零。");
        if (toml)
        {
            var root = TomlRoot(source);
            if (!root.TryGetValue("mcp_servers", out var existing)) { existing = new TomlTable(); root["mcp_servers"] = existing; }
            var servers = existing as TomlTable ?? throw new FormatException("mcp_servers 必须是表。");
            var entry = servers.TryGetValue(value.Name, out var current) ? current as TomlTable ?? throw new FormatException("服务必须是表。") : new TomlTable();
            if (entry.ContainsKey("url")) throw new InvalidOperationException("不能用 stdio 配置覆盖 HTTP 服务，请选择其他名称。");
            entry["command"] = value.Command;
            var args = new TomlArray(); foreach (var arg in value.Args) args.Add(arg);
            entry["args"] = args; entry["startup_timeout_sec"] = (long)value.StartupTimeout; entry["tool_timeout_sec"] = (long)value.ToolTimeout;
            entry.Remove("startup_timeout_ms");
            servers[value.Name] = entry;
            return Toml.FromModel(root);
        }
        var json = Json(source);
        var jsonServers = json["mcpServers"] as JsonObject;
        if (json["mcpServers"] is not null && jsonServers is null) throw new FormatException("mcpServers 必须是对象。");
        if (jsonServers is null) { jsonServers = new(); json["mcpServers"] = jsonServers; }
        var jsonEntry = jsonServers[value.Name] as JsonObject;
        if (jsonServers[value.Name] is not null && jsonEntry is null) throw new FormatException("服务必须是对象。");
        if (jsonEntry is null) { jsonEntry = new(); jsonServers[value.Name] = jsonEntry; }
        if (jsonEntry.ContainsKey("url")) throw new InvalidOperationException("不能用 stdio 配置覆盖 HTTP 服务，请选择其他名称。");
        jsonEntry["command"] = value.Command; jsonEntry["args"] = JsonSerializer.SerializeToNode(value.Args);
        return json.ToJsonString(JsonOptions) + Environment.NewLine;
    }
    // expected == null means the destination did not exist when previewed.
    internal static string? Save(string path, string? expected, string content)
    {
        path = Path.GetFullPath(path);
        if ((File.Exists(path) ? File.ReadAllText(path) : null) != expected) throw new IOException("配置文件已被其他程序修改，请重新读取并预览。");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string? backup = null;
        try
        {
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (expected is not null)
            {
                backup = path + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
                File.Replace(temp, path, backup);
            }
            else File.Move(temp, path); // fail if another writer created it in the meantime
            return backup;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
