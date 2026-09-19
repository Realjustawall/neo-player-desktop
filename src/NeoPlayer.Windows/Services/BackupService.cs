using NeoPlayer.Windows.Data;
using System.IO.Compression;
using System.Text.Json;

namespace NeoPlayer.Windows.Services;

public sealed class BackupService(NeoDatabase db, SettingsService settings)
{
    sealed record Manifest(int Version, string CreatedUtc, string AssetsRoot, string ModelsRoot, string AppVersion);

    public async Task<string> CreateAsync(string? destination = null, CancellationToken ct = default)
    {
        AppPaths.Ensure();
        destination ??= Path.Combine(AppPaths.Backups, $"NEO-{DateTime.Now:yyyyMMdd-HHmmss}.neobackup");
        var temp = Path.Combine(Path.GetTempPath(), "neo-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            ct.ThrowIfCancellationRequested();
            await db.ExportSnapshotAsync(Path.Combine(temp, "neo.db"));
            await settings.SaveAsync(ct);
            File.Copy(AppPaths.Settings, Path.Combine(temp, "settings.json"), true);
            CopyDir(AppPaths.Assets, Path.Combine(temp, "track-assets"), ct);
            CopyDir(AppPaths.Models, Path.Combine(temp, "speech-models"), ct);
            var manifest = new Manifest(2, DateTimeOffset.UtcNow.ToString("O"), AppPaths.Assets, AppPaths.Models,
                typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "0.0.0");
            await File.WriteAllTextAsync(Path.Combine(temp, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), ct);
            if (File.Exists(destination)) File.Delete(destination);
            ZipFile.CreateFromDirectory(temp, destination, CompressionLevel.Optimal, false);
            return destination;
        }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }

    public async Task RestoreAsync(string backup, CancellationToken ct = default)
    {
        var temp = Path.Combine(Path.GetTempPath(), "neo-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            ZipFile.ExtractToDirectory(backup, temp, true);
            ct.ThrowIfCancellationRequested();
            Manifest? manifest = null;
            var manifestPath = Path.Combine(temp, "manifest.json");
            if (File.Exists(manifestPath))
                try { manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(manifestPath, ct)); } catch { }

            var database = Path.Combine(temp, "neo.db");
            if (!File.Exists(database)) throw new InvalidDataException("Backup does not contain neo.db");
            await db.ImportSnapshotAsync(database);

            var st = Path.Combine(temp, "settings.json");
            if (File.Exists(st)) File.Copy(st, AppPaths.Settings, true);

            RestoreDir(Path.Combine(temp, "track-assets"), AppPaths.Assets, ct);
            RestoreDir(Path.Combine(temp, "speech-models"), AppPaths.Models, ct);
            await db.RebaseLocalPathsAsync(manifest?.AssetsRoot, AppPaths.Assets, manifest?.ModelsRoot, AppPaths.Models);
            await settings.LoadAsync();
        }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }

    static void RestoreDir(string src, string dst, CancellationToken ct)
    {
        if (!Directory.Exists(src)) return;
        if (Directory.Exists(dst)) Directory.Delete(dst, true);
        CopyDir(src, dst, ct);
    }

    static void CopyDir(string src, string dst, CancellationToken ct)
    {
        if (!Directory.Exists(src)) return;
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var to = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to, true);
        }
    }
}
