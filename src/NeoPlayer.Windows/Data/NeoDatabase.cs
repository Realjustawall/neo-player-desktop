using Microsoft.Data.Sqlite;
using NeoPlayer.Windows.Core;
using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.Data;

public sealed class NeoDatabase
{
    private readonly string _connectionString;
    public NeoDatabase(string? path = null)
    {
        var dbPath = path ?? AppPaths.Database;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();
    }

    private SqliteConnection Open() { var c = new SqliteConnection(_connectionString); c.Open(); return c; }

    public async Task InitializeAsync()
    {
        await using var c = Open();
        var sql = """
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;
CREATE TABLE IF NOT EXISTS songs(
 id INTEGER PRIMARY KEY AUTOINCREMENT, path TEXT NOT NULL UNIQUE COLLATE NOCASE, title TEXT NOT NULL, artist TEXT NOT NULL,
 album TEXT NOT NULL, genre TEXT NOT NULL, duration_ms INTEGER NOT NULL DEFAULT 0, track_number INTEGER NOT NULL DEFAULT 0,
 year INTEGER NOT NULL DEFAULT 0, artwork_path TEXT, favorite INTEGER NOT NULL DEFAULT 0, hidden INTEGER NOT NULL DEFAULT 0,
 date_added INTEGER NOT NULL DEFAULT 0, last_played INTEGER NOT NULL DEFAULT 0, play_count INTEGER NOT NULL DEFAULT 0);
CREATE INDEX IF NOT EXISTS ix_songs_title ON songs(title COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS ix_songs_artist ON songs(artist COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS ix_songs_album ON songs(album COLLATE NOCASE);
CREATE TABLE IF NOT EXISTS playlists(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, folder_id INTEGER, sort_order INTEGER NOT NULL DEFAULT 0, sort_mode TEXT NOT NULL DEFAULT 'Custom', sort_desc INTEGER NOT NULL DEFAULT 0, view_mode TEXT NOT NULL DEFAULT 'List');
CREATE TABLE IF NOT EXISTS playlist_folders(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, parent_id INTEGER, sort_order INTEGER NOT NULL DEFAULT 0, FOREIGN KEY(parent_id) REFERENCES playlist_folders(id) ON DELETE SET NULL);
CREATE TABLE IF NOT EXISTS playlist_songs(playlist_id INTEGER NOT NULL, song_id INTEGER NOT NULL, position INTEGER NOT NULL, PRIMARY KEY(playlist_id,song_id), FOREIGN KEY(playlist_id) REFERENCES playlists(id) ON DELETE CASCADE, FOREIGN KEY(song_id) REFERENCES songs(id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS categories(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, sort_order INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS category_songs(category_id INTEGER NOT NULL, song_id INTEGER NOT NULL, position INTEGER NOT NULL, PRIMARY KEY(category_id,song_id), FOREIGN KEY(category_id) REFERENCES categories(id) ON DELETE CASCADE, FOREIGN KEY(song_id) REFERENCES songs(id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS lyrics(song_id INTEGER PRIMARY KEY, plain_text TEXT, lrc_text TEXT, translation TEXT, romanization TEXT, updated_at INTEGER NOT NULL, FOREIGN KEY(song_id) REFERENCES songs(id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS metadata_overrides(song_id INTEGER PRIMARY KEY, title TEXT, artist TEXT, album TEXT, genre TEXT, artwork_path TEXT, FOREIGN KEY(song_id) REFERENCES songs(id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS analysis(song_id INTEGER PRIMARY KEY, rms REAL NOT NULL, peak REAL NOT NULL, lufs REAL NOT NULL, bpm REAL NOT NULL, energy REAL NOT NULL, analyzed_at INTEGER NOT NULL, FOREIGN KEY(song_id) REFERENCES songs(id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS pins(kind TEXT NOT NULL, item_id INTEGER NOT NULL, position INTEGER NOT NULL, PRIMARY KEY(kind,item_id));
""";
        await using var cmd = c.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync();
    }

    public async Task<long> UpsertSongAsync(Song song)
    {
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
INSERT INTO songs(path,title,artist,album,genre,duration_ms,track_number,year,artwork_path,date_added)
VALUES($p,$t,$ar,$al,$g,$d,$n,$y,$aw,$da)
ON CONFLICT(path) DO UPDATE SET title=excluded.title,artist=excluded.artist,album=excluded.album,genre=excluded.genre,duration_ms=excluded.duration_ms,track_number=excluded.track_number,year=excluded.year,artwork_path=COALESCE(songs.artwork_path,excluded.artwork_path)
RETURNING id;
""";
        cmd.Parameters.AddWithValue("$p", song.Path); cmd.Parameters.AddWithValue("$t", song.Title); cmd.Parameters.AddWithValue("$ar", song.Artist); cmd.Parameters.AddWithValue("$al", song.Album); cmd.Parameters.AddWithValue("$g", song.Genre); cmd.Parameters.AddWithValue("$d", song.DurationMs); cmd.Parameters.AddWithValue("$n", song.TrackNumber); cmd.Parameters.AddWithValue("$y", song.Year); cmd.Parameters.AddWithValue("$aw", (object?)song.ArtworkPath ?? DBNull.Value); cmd.Parameters.AddWithValue("$da", song.DateAddedUnix);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public async Task<List<Song>> GetSongsAsync(bool includeHidden = false, int? limit = null, int offset = 0)
    {
        await using var c = Open(); await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT id,path,title,artist,album,genre,duration_ms,track_number,year,artwork_path,favorite,hidden,date_added,last_played,play_count FROM songs {(includeHidden ? "" : "WHERE hidden=0")} ORDER BY title COLLATE NOCASE LIMIT $limit OFFSET $offset";
        cmd.Parameters.AddWithValue("$limit", limit ?? int.MaxValue); cmd.Parameters.AddWithValue("$offset", offset);
        return await ReadSongsAsync(cmd);
    }

    public async Task<List<Song>> SearchAsync(string query, int limit = 100)
    {
        await using var c = Open(); await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,path,title,artist,album,genre,duration_ms,track_number,year,artwork_path,favorite,hidden,date_added,last_played,play_count FROM songs WHERE hidden=0 AND (title LIKE $q OR artist LIKE $q OR album LIKE $q OR genre LIKE $q) ORDER BY favorite DESC, play_count DESC, title COLLATE NOCASE LIMIT $limit";
        cmd.Parameters.AddWithValue("$q", "%" + query.Trim() + "%"); cmd.Parameters.AddWithValue("$limit", limit);
        return await ReadSongsAsync(cmd);
    }

    public async Task SetFavoriteAsync(long songId, bool value) => await ExecAsync("UPDATE songs SET favorite=$v WHERE id=$id", ("$v", value ? 1 : 0), ("$id", songId));
    public async Task SetHiddenAsync(long songId, bool value) => await ExecAsync("UPDATE songs SET hidden=$v WHERE id=$id", ("$v", value ? 1 : 0), ("$id", songId));
    public async Task RecordPlayedAsync(long songId) => await ExecAsync("UPDATE songs SET play_count=play_count+1,last_played=$now WHERE id=$id", ("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds()), ("$id", songId));
    public async Task DeleteByPathAsync(string path) => await ExecAsync("DELETE FROM songs WHERE path=$p", ("$p", path));

    public async Task<long> CreatePlaylistAsync(string name, long? folderId = null)
    {
        await using var c = Open(); await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO playlists(name,folder_id,sort_order) VALUES($n,$f,COALESCE((SELECT MAX(sort_order)+1 FROM playlists),0)); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", name.Trim()); cmd.Parameters.AddWithValue("$f", (object?)folderId ?? DBNull.Value); return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public async Task<List<Playlist>> GetPlaylistsAsync()
    {
        var list = new List<Playlist>(); await using var c = Open(); await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT id,name,folder_id,sort_order,sort_mode,sort_desc,view_mode FROM playlists ORDER BY sort_order,name"; await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new Playlist(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt64(2), r.GetInt32(3), r.GetString(4), r.GetInt32(5) != 0, r.GetString(6))); return list;
    }

    public async Task AddToPlaylistAsync(long playlistId, long songId)
    {
        await using var c = Open(); await using var tx = await c.BeginTransactionAsync();
        await using var cmd = c.CreateCommand(); cmd.Transaction = (SqliteTransaction)tx; cmd.CommandText = "INSERT OR IGNORE INTO playlist_songs(playlist_id,song_id,position) VALUES($p,$s,COALESCE((SELECT MAX(position)+1 FROM playlist_songs WHERE playlist_id=$p),0))"; cmd.Parameters.AddWithValue("$p", playlistId); cmd.Parameters.AddWithValue("$s", songId); await cmd.ExecuteNonQueryAsync(); await tx.CommitAsync();
    }

    public async Task<List<Song>> GetPlaylistSongsAsync(long playlistId)
    {
        await using var c = Open(); await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT s.id,s.path,s.title,s.artist,s.album,s.genre,s.duration_ms,s.track_number,s.year,s.artwork_path,s.favorite,s.hidden,s.date_added,s.last_played,s.play_count FROM playlist_songs ps JOIN songs s ON s.id=ps.song_id WHERE ps.playlist_id=$p AND s.hidden=0 ORDER BY ps.position"; cmd.Parameters.AddWithValue("$p", playlistId); return await ReadSongsAsync(cmd);
    }

    public async Task<long> CreateFolderAsync(string name, long? parentId)
    {
        if (parentId is not null && !await FolderExistsAsync(parentId.Value)) throw new InvalidOperationException("Parent folder not found.");
        await using var c = Open(); await using var cmd = c.CreateCommand(); cmd.CommandText = "INSERT INTO playlist_folders(name,parent_id,sort_order) VALUES($n,$p,COALESCE((SELECT MAX(sort_order)+1 FROM playlist_folders),0)); SELECT last_insert_rowid();"; cmd.Parameters.AddWithValue("$n", name.Trim()); cmd.Parameters.AddWithValue("$p", (object?)parentId ?? DBNull.Value); return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public async Task MoveFolderAsync(long folderId, long? newParent)
    {
        if (newParent == folderId) throw new InvalidOperationException("A folder cannot contain itself.");
        if (newParent is not null)
        {
            var cursor = newParent;
            var seen = new HashSet<long>();
            while (cursor is not null)
            {
                if (!seen.Add(cursor.Value) || cursor.Value == folderId) throw new InvalidOperationException("Folder cycle detected.");
                cursor = await GetFolderParentAsync(cursor.Value);
            }
        }
        await ExecAsync("UPDATE playlist_folders SET parent_id=$p WHERE id=$id", ("$p", (object?)newParent ?? DBNull.Value), ("$id", folderId));
    }

    private async Task<bool> FolderExistsAsync(long id) { await using var c = Open(); await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM playlist_folders WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id); return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0; }
    private async Task<long?> GetFolderParentAsync(long id) { await using var c = Open(); await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT parent_id FROM playlist_folders WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id); var v = await cmd.ExecuteScalarAsync(); return v is null or DBNull ? null : Convert.ToInt64(v); }

    public async Task SaveLyricsAsync(long songId, string? plain, string? lrc, string? translation = null, string? romanization = null)
    {
        await using var c = Open(); await using var cmd = c.CreateCommand(); cmd.CommandText = "INSERT INTO lyrics(song_id,plain_text,lrc_text,translation,romanization,updated_at) VALUES($id,$p,$l,$t,$r,$u) ON CONFLICT(song_id) DO UPDATE SET plain_text=excluded.plain_text,lrc_text=excluded.lrc_text,translation=excluded.translation,romanization=excluded.romanization,updated_at=excluded.updated_at"; cmd.Parameters.AddWithValue("$id", songId); cmd.Parameters.AddWithValue("$p", (object?)plain ?? DBNull.Value); cmd.Parameters.AddWithValue("$l", (object?)lrc ?? DBNull.Value); cmd.Parameters.AddWithValue("$t", (object?)translation ?? DBNull.Value); cmd.Parameters.AddWithValue("$r", (object?)romanization ?? DBNull.Value); cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToUnixTimeSeconds()); await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveAnalysisAsync(long songId, AudioAnalysis a)
    {
        await using var c = Open(); await using var cmd = c.CreateCommand(); cmd.CommandText = "INSERT INTO analysis(song_id,rms,peak,lufs,bpm,energy,analyzed_at) VALUES($id,$r,$p,$l,$b,$e,$a) ON CONFLICT(song_id) DO UPDATE SET rms=excluded.rms,peak=excluded.peak,lufs=excluded.lufs,bpm=excluded.bpm,energy=excluded.energy,analyzed_at=excluded.analyzed_at"; cmd.Parameters.AddWithValue("$id", songId); cmd.Parameters.AddWithValue("$r", a.Rms); cmd.Parameters.AddWithValue("$p", a.Peak); cmd.Parameters.AddWithValue("$l", a.EstimatedLufs); cmd.Parameters.AddWithValue("$b", a.Bpm); cmd.Parameters.AddWithValue("$e", a.Energy); cmd.Parameters.AddWithValue("$a", a.AnalyzedAtUnix); await cmd.ExecuteNonQueryAsync();
    }

    private async Task ExecAsync(string sql, params (string Name, object Value)[] args) { await using var c = Open(); await using var cmd = c.CreateCommand(); cmd.CommandText = sql; foreach (var a in args) cmd.Parameters.AddWithValue(a.Name, a.Value); await cmd.ExecuteNonQueryAsync(); }
    private static async Task<List<Song>> ReadSongsAsync(SqliteCommand cmd)
    {
        var list = new List<Song>(); await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new Song(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt64(6), r.GetInt32(7), r.GetInt32(8), r.IsDBNull(9) ? null : r.GetString(9), r.GetInt32(10) != 0, r.GetInt32(11) != 0, r.GetInt64(12), r.GetInt64(13), r.GetInt32(14)));
        return list;
    }
}
