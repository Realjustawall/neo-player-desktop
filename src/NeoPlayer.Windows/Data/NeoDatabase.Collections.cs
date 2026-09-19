using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.Data;

public sealed partial class NeoDatabase
{
    public Task RenameFolderAsync(long id, string title) =>
        ExecuteAsync("UPDATE playlist_folders SET title=@t WHERE id=@id", ("@t", title.Trim()), ("@id", id));

    public async Task ReorderFoldersAsync(IReadOnlyList<long> ids)
    {
        await _gate.WaitAsync();
        try
        {
            await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await Conn.BeginTransactionAsync();
            for (var i = 0; i < ids.Count; i++)
            {
                await using var cmd = Conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE playlist_folders SET position=@p WHERE id=@id";
                cmd.Parameters.AddWithValue("@p", i);
                cmd.Parameters.AddWithValue("@id", ids[i]);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        finally { _gate.Release(); }
    }

    public Task RenameCategoryAsync(long id, string title) =>
        ExecuteAsync("UPDATE categories SET title=@t WHERE id=@id", ("@t", title.Trim()), ("@id", id));

    public Task UpdateCategoryAsync(long id, string title, string description, string? artworkPath) =>
        ExecuteAsync("UPDATE categories SET title=@t,description=@d,artworkPath=@a WHERE id=@id",
            ("@t", title.Trim()), ("@d", description ?? ""), ("@a", artworkPath), ("@id", id));

    public Task DeleteCategoryAsync(long id) =>
        ExecuteAsync("DELETE FROM categories WHERE id=@id", ("@id", id));

    public Task RemoveFromCategoryAsync(long categoryId, long songId) =>
        ExecuteAsync("DELETE FROM category_songs WHERE categoryId=@c AND songId=@s", ("@c", categoryId), ("@s", songId));

    public async Task ReorderCategoryAsync(long categoryId, IReadOnlyList<long> ids)
    {
        await _gate.WaitAsync();
        try
        {
            await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await Conn.BeginTransactionAsync();
            for (var i = 0; i < ids.Count; i++)
            {
                await using var cmd = Conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE category_songs SET position=@p WHERE categoryId=@c AND songId=@s";
                cmd.Parameters.AddWithValue("@p", i);
                cmd.Parameters.AddWithValue("@c", categoryId);
                cmd.Parameters.AddWithValue("@s", ids[i]);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        finally { _gate.Release(); }
    }

    public Task UpdatePlaylistAsync(long id, string title, string description, string? artworkPath) =>
        ExecuteAsync("UPDATE playlists SET title=@t,description=@d,artworkPath=@a WHERE id=@id",
            ("@t", title.Trim()), ("@d", description ?? ""), ("@a", artworkPath), ("@id", id));

    public Task SetSongArtworkAsync(long songId, string path) =>
        ExecuteAsync("UPDATE songs SET customArtworkPath=@p WHERE id=@id", ("@p", path ?? ""), ("@id", songId));

    public Task<List<(string Type, string Key)>> GetFavoriteCollectionsAsync() =>
        QueryAsync("SELECT type,key FROM favorite_collections ORDER BY addedAt DESC", r => (r.GetString(0), r.GetString(1)));

    public async Task ReorderPinsAsync(IReadOnlyList<(string Type, string Key)> pins)
    {
        await _gate.WaitAsync();
        try
        {
            await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await Conn.BeginTransactionAsync();
            for (var i = 0; i < pins.Count; i++)
            {
                await using var cmd = Conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE pinned_collections SET position=@p WHERE type=@t AND key=@k";
                cmd.Parameters.AddWithValue("@p", i);
                cmd.Parameters.AddWithValue("@t", pins[i].Type);
                cmd.Parameters.AddWithValue("@k", pins[i].Key);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        finally { _gate.Release(); }
    }

    public async Task<List<Song>> SearchSongsAsync(string query, int limit = 250)
    {
        query = query.Trim();
        if (query.Length == 0) return [];
        var songs = await GetSongsAsync();
        return songs.Where(s => s.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || s.Artist.Contains(query, StringComparison.OrdinalIgnoreCase)
            || s.Album.Contains(query, StringComparison.OrdinalIgnoreCase)
            || s.Genre.Contains(query, StringComparison.OrdinalIgnoreCase)
            || s.Composer.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Clamp(limit, 1, 2000)).ToList();
    }


    public async Task ReorderPlaylistsAsync(IReadOnlyList<long> ids)
    {
        await _gate.WaitAsync();
        try
        {
            await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await Conn.BeginTransactionAsync();
            for (var i = 0; i < ids.Count; i++)
            {
                await using var cmd = Conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE playlists SET customOrder=@p WHERE id=@id";
                cmd.Parameters.AddWithValue("@p", i);
                cmd.Parameters.AddWithValue("@id", ids[i]);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteMissingSongsInRootsAsync(HashSet<long> present, IEnumerable<string> scannedRoots)
    {
        var roots = scannedRoots.Select(Path.GetFullPath).Select(x => x.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).ToArray();
        if (roots.Length == 0) return;
        var all = await QueryAsync("SELECT id,path FROM songs", r => (Id:r.GetInt64(0), Path:r.GetString(1)));
        var missing = all.Where(x => !present.Contains(x.Id) && roots.Any(root => { try { var full = Path.GetFullPath(x.Path); return full.StartsWith(root, StringComparison.OrdinalIgnoreCase); } catch { return false; } })).Select(x => x.Id).ToList();
        foreach (var chunk in missing.Chunk(250)) await ExecuteAsync($"DELETE FROM songs WHERE id IN ({string.Join(',', chunk)})");
    }

    public Task PurgeRegeneratableAnalysisAsync() =>
        ExecuteAsync("DELETE FROM audio_analysis; DELETE FROM advanced_audio_analysis; DELETE FROM replay_gain_cache;");
}

public sealed partial class NeoDatabase
{
    /// <summary>Rewrites app-private absolute paths after a backup is restored on another Windows profile/machine.</summary>
    public async Task RebaseLocalPathsAsync(string? oldAssetsRoot, string newAssetsRoot, string? oldModelsRoot, string newModelsRoot)
    {
        static string Norm(string value) => Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var oa = string.IsNullOrWhiteSpace(oldAssetsRoot) ? null : Norm(oldAssetsRoot);
        var na = Norm(newAssetsRoot);
        var om = string.IsNullOrWhiteSpace(oldModelsRoot) ? null : Norm(oldModelsRoot);
        var nm = Norm(newModelsRoot);
        await _gate.WaitAsync();
        try
        {
            await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await Conn.BeginTransactionAsync();
            async Task ReplaceAsync(string table, string column, string oldRoot, string newRoot)
            {
                await using var cmd = Conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"UPDATE {table} SET {column}=REPLACE({column},@old,@new) WHERE {column} LIKE @like";
                cmd.Parameters.AddWithValue("@old", oldRoot);
                cmd.Parameters.AddWithValue("@new", newRoot);
                cmd.Parameters.AddWithValue("@like", oldRoot.Replace("%", "[%]") + "%");
                await cmd.ExecuteNonQueryAsync();
            }
            if (oa is not null && !oa.Equals(na, StringComparison.OrdinalIgnoreCase))
            {
                await ReplaceAsync("songs", "customArtworkPath", oa, na);
                await ReplaceAsync("playlists", "artworkPath", oa, na);
                await ReplaceAsync("categories", "artworkPath", oa, na);
                await ReplaceAsync("track_visual_profiles", "canvasPath", oa, na);
                await ReplaceAsync("track_visual_profiles", "backgroundImagePath", oa, na);
            }
            if (om is not null && !om.Equals(nm, StringComparison.OrdinalIgnoreCase))
                await ReplaceAsync("offline_speech_models", "localPath", om, nm);
            await tx.CommitAsync();
        }
        finally { _gate.Release(); }
    }
}

public sealed partial class NeoDatabase
{
    public Task<List<AdvancedAudioAnalysis>> GetAdvancedAnalysesAsync() => QueryAsync(
        "SELECT songId,bpm,beatIntervalMs,beatOffsetMs,beatConfidence,phraseLengthBeats,phraseOffsetMs,phraseConfidence,musicalKey,camelotKey,keyConfidence,energy,valence,danceability,spectralCentroidHz,dynamicRangeDb,mood,beatGridJson,analyzedAt FROM advanced_audio_analysis",
        r => new AdvancedAudioAnalysis(r.GetInt64(0),r.GetFloat(1),r.GetFloat(2),r.GetFloat(3),r.GetFloat(4),r.GetInt32(5),r.GetFloat(6),r.GetFloat(7),r.GetString(8),r.GetString(9),r.GetFloat(10),r.GetFloat(11),r.GetFloat(12),r.GetFloat(13),r.GetFloat(14),r.GetFloat(15),r.GetString(16),r.GetString(17),r.GetInt64(18)));

    public Task<List<ReplayGain>> GetReplayGainsAsync() => QueryAsync(
        "SELECT songId,trackGainDb,albumGainDb,trackPeak,r128TrackGainDb,source,scannedAt FROM replay_gain_cache",
        r => new ReplayGain(r.GetInt64(0),r.IsDBNull(1)?null:r.GetFloat(1),r.IsDBNull(2)?null:r.GetFloat(2),r.IsDBNull(3)?null:r.GetFloat(3),r.IsDBNull(4)?null:r.GetFloat(4),r.GetString(5),r.GetInt64(6)));
}

public sealed partial class NeoDatabase
{
    public async Task<(string ModelId, string Language)> GetSpeechPreferenceAsync()
    {
        var rows = await QueryAsync("SELECT selectedSpeechModelId,selectedSpeechLanguage FROM track_experience_preferences WHERE id=0",
            r => (r.GetString(0), r.GetString(1)));
        return rows.FirstOrDefault() == default ? ("", "en") : rows[0];
    }
    public Task SaveSpeechPreferenceAsync(string modelId, string language) => ExecuteAsync(
        "INSERT OR REPLACE INTO track_experience_preferences(id,selectedSpeechModelId,selectedSpeechLanguage) VALUES(0,@m,@l)",
        ("@m", modelId ?? ""), ("@l", string.IsNullOrWhiteSpace(language) ? "en" : language));
}

