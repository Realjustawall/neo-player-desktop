using System.IO.Compression;
using NeoPlayer.Windows.Core;

namespace NeoPlayer.Windows.Services;

public sealed class BackupService
{
    public Task ExportAsync(string destination)
    {
        AppPaths.Ensure(); if (File.Exists(destination)) File.Delete(destination);
        using var zip = ZipFile.Open(destination, ZipArchiveMode.Create);
        if (File.Exists(AppPaths.Database)) zip.CreateEntryFromFile(AppPaths.Database, "neo.db", CompressionLevel.Optimal);
        if (File.Exists(AppPaths.Settings)) zip.CreateEntryFromFile(AppPaths.Settings, "settings.json", CompressionLevel.Optimal);
        return Task.CompletedTask;
    }

    public Task RestoreAsync(string source)
    {
        AppPaths.Ensure(); using var zip = ZipFile.OpenRead(source);
        foreach (var name in new[] { "neo.db", "settings.json" }) { var e = zip.GetEntry(name); if (e is null) continue; e.ExtractToFile(name == "neo.db" ? AppPaths.Database : AppPaths.Settings, true); }
        return Task.CompletedTask;
    }
}
