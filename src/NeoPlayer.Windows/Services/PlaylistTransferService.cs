using System.Text;
using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.Services;

public sealed class PlaylistTransferService
{
    private readonly NeoDatabase _database;
    private readonly LibraryCollectionsService _collections;

    public PlaylistTransferService(NeoDatabase database, LibraryCollectionsService collections)
    {
        _database = database;
        _collections = collections;
    }

    public async Task ExportM3u8Async(Playlist playlist, string outputPath)
    {
        var songs = await _collections.GetPlaylistSongsAsync(playlist.Id);
        var builder = new StringBuilder("#EXTM3U\n");
        foreach (var song in songs)
        {
            var seconds = Math.Max(0, (int)Math.Round(song.DurationMs / 1000d));
            builder.Append("#EXTINF:").Append(seconds).Append(',').Append(song.Artist).Append(" - ").Append(song.Title).Append('\n');
            builder.Append(song.Path).Append('\n');
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await File.WriteAllTextAsync(outputPath, builder.ToString(), new UTF8Encoding(false));
    }

    public async Task<Playlist> ImportM3u8Async(string inputPath, string? playlistName = null)
    {
        if (!File.Exists(inputPath)) throw new FileNotFoundException("Playlist file was not found.", inputPath);
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Environment.CurrentDirectory;
        var paths = new List<string>();
        foreach (var raw in await File.ReadAllLinesAsync(inputPath, Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string? candidate = null;
            if (Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.IsFile) candidate = uri.LocalPath;
            else if (Path.IsPathRooted(line)) candidate = line;
            else candidate = Path.Combine(baseDirectory, line);
            try { paths.Add(Path.GetFullPath(candidate)); } catch { }
        }

        var name = string.IsNullOrWhiteSpace(playlistName) ? Path.GetFileNameWithoutExtension(inputPath) : playlistName.Trim();
        var id = await _database.CreatePlaylistAsync(string.IsNullOrWhiteSpace(name) ? "Imported playlist" : name);
        var playlist = (await _database.GetPlaylistsAsync()).First(x => x.Id == id);
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var song = await _collections.GetSongByPathAsync(path);
            if (song is not null) await _collections.AddToPlaylistAsync(id, song.Id);
        }
        return playlist;
    }
}
