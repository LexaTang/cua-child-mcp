using System.Text.Json;

namespace CuaChild;

internal sealed record DesktopSettings
{
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public int ColorDepth { get; init; } = 32;
    public bool SmartSizing { get; init; } = true;
    public bool Clipboard { get; init; }
    public bool Drives { get; init; }
    public int AudioMode { get; init; } = 2;
    public int KeyboardMode { get; init; } = 1;
    internal static string FileName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CuaChild", "view-settings.json");
    internal void Validate()
    {
        if (Width < 640 || Width > 7680 || Height < 480 || Height > 4320 ||
            ColorDepth is not (16 or 32) || AudioMode is < 0 or > 2 || KeyboardMode is < 0 or > 2)
            throw new ArgumentException("显示设置超出支持范围。");
    }
    internal static DesktopSettings Load()
    {
        if (!File.Exists(FileName)) return new();
        var value = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(FileName)) ?? new();
        value.Validate();
        return value;
    }
    internal void Save()
    {
        Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(FileName)!);
        string temp = FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FileName, true);
    }
}
