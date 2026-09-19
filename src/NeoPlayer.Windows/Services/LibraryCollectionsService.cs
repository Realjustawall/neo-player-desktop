using Microsoft.Data.Sqlite;
using NeoPlayer.Windows.Core;
using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.Services;

public sealed class LibraryCollectionsService
{
    private readonly string _connectionString;

    public LibraryCollectionsService(string? databasePath = null)
    {
        var path = databasePath ?? AppPaths.Database;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return c;
    }

    public async Task<List<CollectionSummary>> GetCollectionsAsync(string kind)
    {
        var normalized = kind.Trim().ToLowerInvariant();
        var expression = normalized switch
        {
            "album" => "album",
            "artist" => "artist",
            "genre" => "genre",
            "folder" => "substr(path,1,length(path)-length(replace(path,'\\',''))+0)",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        await using var c = Open();
        await using var cmd = c.CreateCommand();
        if (normalized == "folder")
        {
            cmd.CommandText = "SELECT path FROM songs WHERE hidden=0 ORDER BY path COLLATE NOCASE";
            var folders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var folder = Path.GetDirectoryName(reader.GetString(0)) ?? "";
                if (folder.Length == 0) continue;
                folders[folder] = folders.TryGetValue(folder, out var count) ? count + 1 : 1;
            }
            return folders.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new CollectionSummary("Folder", x.Key, x.Value)).ToList();
        }

        cmd.CommandText = $"SELECT {expression},COUNT(*) FROM songs WHERE hidden=0 AND trim({expression})<>'' GROUP BY {expression} ORDER BY {expression} COLLATE NOCASE";
        var result = new List<CollectionSummary>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                result.Add(new CollectionSummary(char.ToUpperInvariant(normalized[0]) + normalized[1..], reader.GetString(0), reader.GetInt32(1)));
        }
        return result;
    }

    public async Task<List<Song>> GetSongsForCollectionAsync(CollectionSummary collection)
    {
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        var where = collection.Kind.ToLowerInvariant() switch
        {
            "album" => "album=$v",
            "artist" => "artist=$v",
            "genre" => "genre=$v",
            "folder" => "path LIKE $prefix",
            _ => throw new ArgumentOutOfRangeException(nameof(collection))
        };
        cmd.CommandText = $"SELECT id,path,title,artist,album,genre,duration_ms,track_number,year,artwork_path,favorite,hidden,date_added,last_played,play_count FROM songs WHERE hidden=0 AND {where} ORDER BY track_number,title COLLATE NOCASE";
        if (collection.Kind.Equals("Folder", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = Path.GetFullPath(collection.Name).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            cmd.Parameters.AddWithValue("$prefix", prefix.Replace("%", "[%]").Replace("_", "[_]") + "%");
        }
        else cmd.Parameters.AddWithValue("$v", collection.Name);
        return await ReadSongsAsync(cmd);
    }

    public async Task<List<Song>> GetHiddenSongsAsync()
    {
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,path,title,artist,album,genre,duration_ms,track_number,year,artwork_path,favorite,hidden,date_added,last_played,play_count FROM songs WHERE hidden=1 ORDER BY title COLLATE NOCASE";
        return await ReadSongsAsync(cmd);
    }

    public async Task<Song?> GetSongByPathAsync(string path)
    {
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,path,title,artist,album,genre,duration_ms,track_number,year,artwork_path,favorite,hidden,date_added,last_played,play_count FROM songs WHERE path=$p LIMIT 1";
        cmd.Parameters.AddWithValue("$p", path);
        var songs = await ReadSongsAsync(cmd);
        return songs.FirstOrDefault();
    }

    public async Task SetHiddenAsync(long songId, bool hidden)
        => await ExecAsync("UPDATE songs SET hidden=$v WHERE id=$id", ("$v", hidden ? 1 : 0), ("$id", songId));

    public async Task UpdateMetadataAsync(long songId, string title, string artist, string album, string genre, string? artworkPath)
    {
        title = string.IsNullOrWhiteSpace(title) ? "Unknown title" : title.Trim();
        artist = string.IsNullOrWhiteSpace(artist) ? "Unknown artist" : artist.Trim();
        album = string.IsNullOrWhiteSpace(album) ? "Unknown album" : album.Trim();
        genre = string.IsNullOrWhiteSpace(genre) ? "Unknown" : genre.Trim();

        await using var c = Open();
        await using var tx = await c.BeginTransactionAsync();
        await using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "UPDATE songs SET title=$t,artist=$ar,album=$al,genre=$g,artwork_path=$aw WHERE id=$id";
            cmd.Parameters.AddWithValue("$t", title);
            cmd.Parameters.AddWithValue("$ar", artist);
            cmd.Parameters.AddWithValue("$al", album);
            cmd.Parameters.AddWithValue("$g", genre);
            cmd.Parameters.AddWithValue("$aw", (object?)artworkPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", songId);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "INSERT INTO metadata_overrides(song_id,title,artist,album,genre,artwork_path) VALUES($id,$t,$ar,$al,$g,$aw) ON CONFLICT(song_id) DO UPDATE SET title=excluded.title,artist=excluded.artist,album=excluded.album,genre=excluded.genre,artwork_path=excluded.artwork_path";
            cmd.Parameters.AddWithValue("$id", songId);
            cmd.Parameters.AddWithValue("$t", title);
            cmd.Parameters.AddWithValue("$ar", artist);
            cmd.Parameters.AddWithValue("$al", album);
            cmd.Parameters.AddWithValue("$g", genre);
            cmd.Parameters.AddWithValue("$aw", (object?)artworkPath ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public async Task<List<PlaylistFolder>> GetPlaylistFoldersAsync()
    {
        var result = new List<PlaylistFolder>();
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,parent_id,sort_order FROM playlist_folders ORDER BY sort_order,name COLLATE NOCASE";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) result.Add(new PlaylistFolder(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt64(2), r.GetInt32(3)));
        return result;
    }

    public async Task<List<Category>> GetCategoriesAsync()
    {
        var result = new List<Category>();
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,sort_order FROM categories ORDER BY sort_order,name COLLATE NOCASE";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) result.Add(new Category(r.GetInt64(0), r.GetString(1), r.GetInt32(2)));
        return result;
    }

    public async Task<long> CreateCategoryAsync(string name)
    {
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO categories(name,sort_order) VALUES($n,COALESCE((SELECT MAX(sort_order)+1 FROM categories),0));SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", name.Trim());
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public async Task AddToCategoryAsync(long categoryId, long songId)
    {
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO category_songs(category_id,song_id,position) VALUES($c,$s,COALESCE((SELECT MAX(position)+1 FROM category_songs WHERE category_id=$c),0))";
        cmd.Parameters.AddWithValue("$c", categoryId);
        cmd.Parameters.AddWithValue("$s", songId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<Song>> GetCategorySongsAsync(long categoryId)
    {
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT s.id,s.path,s.title,s.artist,s.album,s.genre,s.duration_ms,s.track_number,s.year,s.artwork_path,s.favorite,s.hidden,s.date_added,s.last_played,s.play_count FROM category_songs cs JOIN songs s ON s.id=cs.song_id WHERE cs.category_id=$id AND s.hidden=0 ORDER BY cs.position";
        cmd.Parameters.AddWithValue("$id", categoryId);
        return await ReadSongsAsync(cmd);
    }

    public async Task RenamePlaylistAsync(long playlistId, string name)
        => await ExecAsync("UPDATE playlists SET name=$n WHERE id=$id", ("$n", name.Trim()), ("$id", playlistId));

    public async Task DeletePlaylistAsync(long playlistId)
        => await ExecAsync("DELETE FROM playlists WHERE id=$id", ("$id", playlistId));

    public async Task AddToPlaylistAsync(long playlistId, long songId)
    {
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO playlist_songs(playlist_id,song_id,position) VALUES($p,$s,COALESCE((SELECT MAX(position)+1 FROM playlist_songs WHERE playlist_id=$p),0))";
        cmd.Parameters.AddWithValue("$p", playlistId);
        cmd.Parameters.AddWithValue("$s", songId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveFromPlaylistAsync(long playlistId, long songId)
    {
        await ExecAsync("DELETE FROM playlist_songs WHERE playlist_id=$p AND song_id=$s", ("$p", playlistId), ("$s", songId));
        await NormalizePlaylistPositionsAsync(playlistId);
    }

    public async Task MovePlaylistSongAsync(long playlistId, long songId, int newIndex)
    {
        var songs = await GetPlaylistSongsAsync(playlistId);
        var current = songs.FindIndex(x => x.Id == songId);
        if (current < 0) return;
        var target = Math.Clamp(newIndex, 0, songs.Count - 1);
        var item = songs[current]; songs.RemoveAt(current); songs.Insert(target, item);
        await using var c = Open();
        await using var tx = await c.BeginTransactionAsync();
        for (var i = 0; i < songs.Count; i++)
        {
            await using var cmd = c.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "UPDATE playlist_songs SET position=$pos WHERE playlist_id=$p AND song_id=$s";
            cmd.Parameters.AddWithValue("$pos", i); cmd.Parameters.AddWithValue("$p", playlistId); cmd.Parameters.AddWithValue("$s", songs[i].Id);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public async Task<List<Song>> GetPlaylistSongsAsync(long playlistId)
    {
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT s.id,s.path,s.title,s.artist,s.album,s.genre,s.duration_ms,s.track_number,s.year,s.artwork_path,s.favorite,s.hidden,s.date_added,s.last_played,s.play_count FROM playlist_songs ps JOIN songs s ON s.id=ps.song_id WHERE ps.playlist_id=$p AND s.hidden=0 ORDER BY ps.position";
        cmd.Parameters.AddWithValue("$p", playlistId);
        return await ReadSongsAsync(cmd);
    }

    public async Task<LyricsDocument> GetLyricsAsync(long songId)
    {
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT plain_text,lrc_text,translation,romanization FROM lyrics WHERE song_id=$id";
        cmd.Parameters.AddWithValue("$id", songId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return new LyricsDocument("", "", "", "");
        return new LyricsDocument(r.IsDBNull(0) ? "" : r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2), r.IsDBNull(3) ? "" : r.GetString(3));
    }

    public async Task SaveLyricsAsync(long songId, LyricsDocument lyrics)
    {
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO lyrics(song_id,plain_text,lrc_text,translation,romanization,updated_at) VALUES($id,$p,$l,$t,$r,$u) ON CONFLICT(song_id) DO UPDATE SET plain_text=excluded.plain_text,lrc_text=excluded.lrc_text,translation=excluded.translation,romanization=excluded.romanization,updated_at=excluded.updated_at";
        cmd.Parameters.AddWithValue("$id", songId);
        cmd.Parameters.AddWithValue("$p", lyrics.PlainText);
        cmd.Parameters.AddWithValue("$l", lyrics.LrcText);
        cmd.Parameters.AddWithValue("$t", lyrics.Translation);
        cmd.Parameters.AddWithValue("$r", lyrics.Romanization);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task NormalizePlaylistPositionsAsync(long playlistId)
    {
        var songs = await GetPlaylistSongsAsync(playlistId);
        await using var c = Open();
        await using var tx = await c.BeginTransactionAsync();
        for (var i = 0; i < songs.Count; i++)
        {
            await using var cmd = c.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "UPDATE playlist_songs SET position=$pos WHERE playlist_id=$p AND song_id=$s";
            cmd.Parameters.AddWithValue("$pos", i); cmd.Parameters.AddWithValue("$p", playlistId); cmd.Parameters.AddWithValue("$s", songs[i].Id);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    private async Task ExecAsync(string sql, params (string Name, object Value)[] args)
    {
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var arg in args) cmd.Parameters.AddWithValue(arg.Name, arg.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<List<Song>> ReadSongsAsync(SqliteCommand cmd)
    {
        var list = new List<Song>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new Song(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt64(6), r.GetInt32(7), r.GetInt32(8), r.IsDBNull(9) ? null : r.GetString(9), r.GetInt32(10) != 0, r.GetInt32(11) != 0, r.GetInt64(12), r.GetInt64(13), r.GetInt32(14)));
        return list;
    }
}
