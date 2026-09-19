using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;
using TagLib;

namespace NeoPlayer.Windows.Services;

public sealed class LibraryScanner(NeoDatabase db, SettingsService settings)
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".m4a", ".aac", ".wav", ".wma", ".flac", ".ogg", ".opus" };
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    public event EventHandler<int>? Progress;

    public async Task<int> ScanAsync(CancellationToken cancellationToken = default)
    {
        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            var files = settings.Value.SourceFolders.Where(Directory.Exists).SelectMany(EnumerateSafe).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var done = 0;
            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsExcluded(path)) continue;
                var song = ReadMetadata(path);
                if (song.DurationMs >= settings.Value.MinimumDurationSeconds * 1000L) await db.UpsertSongAsync(song);
                done++; Progress?.Invoke(this, files.Length == 0 ? 100 : (int)(done * 100.0 / files.Length));
            }
            return done;
        }
        finally { _scanGate.Release(); }
    }

    public static Song ReadMetadata(string path)
    {
        try
        {
            using var f = TagLib.File.Create(path);
            var t = f.Tag; var p = f.Properties;
            return new Song(0, path, string.IsNullOrWhiteSpace(t.Title) ? Path.GetFileNameWithoutExtension(path) : t.Title, t.FirstPerformer ?? "Unknown Artist", t.Album ?? "Unknown Album", t.FirstGenre ?? "", (long)p.Duration.TotalMilliseconds, (int)t.Track, (int)t.Year, null, false, false, new DateTimeOffset(System.IO.File.GetCreationTimeUtc(path)).ToUnixTimeSeconds(), 0, 0);
        }
        catch
        {
            return new Song(0, path, Path.GetFileNameWithoutExtension(path), "Unknown Artist", "Unknown Album", "", 0, 0, 0, null, false, false, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 0, 0);
        }
    }

    private IEnumerable<string> EnumerateSafe(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            if (IsExcluded(dir)) continue;
            string[] files = []; string[] dirs = [];
            try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); } catch { }
            foreach (var f in files) if (Extensions.Contains(Path.GetExtension(f))) yield return f;
            foreach (var d in dirs)
            {
                try { if ((System.IO.File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0) pending.Push(d); } catch { }
            }
        }
    }

    private bool IsExcluded(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return settings.Value.ExcludedFolders.Any(x => full.StartsWith(Path.GetFullPath(x).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }
}
