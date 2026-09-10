using System.Reflection;
namespace CuaChild;
internal static class AppBrand
{
    // One icon shared for the process lifetime by the window and tray.
    internal static readonly Icon Icon = Load();
    private static Icon Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CuaChild.AppIcon")
            ?? throw new InvalidOperationException("Application icon resource missing");
        using var icon = new Icon(stream, new Size(32, 32));
        return (Icon)icon.Clone();
    }
}
