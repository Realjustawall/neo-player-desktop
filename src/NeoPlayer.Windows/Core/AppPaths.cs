namespace NeoPlayer.Windows.Core;

public static class AppPaths
{
    private static string Root => Environment.GetEnvironmentVariable("NEO_DATA_DIR") is { Length: > 0 } custom
        ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEO Player");

    public static string Data => Root;
    public static string Database => Path.Combine(Root, "neo.db");
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Cache => Path.Combine(Root, "cache");
    public static string Artwork => Path.Combine(Cache, "artwork");
    public static string Backups => Path.Combine(Root, "backups");

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Artwork);
        Directory.CreateDirectory(Backups);
    }
}
