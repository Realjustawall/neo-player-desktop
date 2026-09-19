namespace NeoPlayer.Windows;
public static class AppPaths
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEOPlayer");
    public static string Database => Path.Combine(Root, "neo.db");
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Cache => Path.Combine(Root, "cache");
    public static string ArtworkCache => Path.Combine(Cache, "artwork");
    public static string AnalysisCache => Path.Combine(Cache, "analysis");
    public static string Models => Path.Combine(Root, "models");
    public static string Assets => Path.Combine(Root, "track-assets");
    public static string Backups => Path.Combine(Root, "backups");
    public static void Ensure(){ foreach(var p in new[]{Root,Cache,ArtworkCache,AnalysisCache,Models,Assets,Backups}) Directory.CreateDirectory(p); }
}