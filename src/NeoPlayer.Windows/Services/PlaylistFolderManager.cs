using Microsoft.Data.Sqlite;
using NeoPlayer.Windows.Core;
using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.Services;

public sealed class PlaylistFolderManager
{
    private readonly string _connectionString;

    public PlaylistFolderManager(string? databasePath = null)
    {
        var path = databasePath ?? AppPaths.Database;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public async Task<List<PlaylistFolder>> GetFoldersAsync()
    {
        var result = new List<PlaylistFolder>();
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,parent_id,sort_order FROM playlist_folders ORDER BY sort_order,name COLLATE NOCASE";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            result.Add(new PlaylistFolder(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt64(2), r.GetInt32(3)));
        return result;
    }

    public async Task<long> CreateAsync(string name, long? parentId = null)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Folder name cannot be empty.", nameof(name));
        if (parentId is not null && !await ExistsAsync(parentId.Value)) throw new InvalidOperationException("Parent folder not found.");
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO playlist_folders(name,parent_id,sort_order) VALUES($n,$p,COALESCE((SELECT MAX(sort_order)+1 FROM playlist_folders),0));SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$p", (object?)parentId ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public async Task RenameAsync(long folderId, string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Folder name cannot be empty.", nameof(name));
        await ExecAsync("UPDATE playlist_folders SET name=$n WHERE id=$id", ("$n", name), ("$id", folderId));
    }

    public async Task MoveAsync(long folderId, long? newParentId)
    {
        if (folderId == newParentId) throw new InvalidOperationException("A playlist folder cannot contain itself.");
        if (!await ExistsAsync(folderId)) throw new InvalidOperationException("Playlist folder not found.");
        if (newParentId is not null && !await ExistsAsync(newParentId.Value)) throw new InvalidOperationException("Target folder not found.");

        var cursor = newParentId;
        var seen = new HashSet<long>();
        while (cursor is not null)
        {
            if (!seen.Add(cursor.Value) || cursor.Value == folderId) throw new InvalidOperationException("Playlist folder cycle detected.");
            cursor = await GetParentAsync(cursor.Value);
        }
        await ExecAsync("UPDATE playlist_folders SET parent_id=$parent WHERE id=$id", ("$parent", (object?)newParentId ?? DBNull.Value), ("$id", folderId));
    }

    public async Task AssignPlaylistAsync(long playlistId, long? folderId)
    {
        if (folderId is not null && !await ExistsAsync(folderId.Value)) throw new InvalidOperationException("Playlist folder not found.");
        await ExecAsync("UPDATE playlists SET folder_id=$folder WHERE id=$id", ("$folder", (object?)folderId ?? DBNull.Value), ("$id", playlistId));
    }

    public async Task DeleteAsync(long folderId)
    {
        await using var c = Open();
        await using var tx = await c.BeginTransactionAsync();
        await using (var playlists = c.CreateCommand())
        {
            playlists.Transaction = (SqliteTransaction)tx;
            playlists.CommandText = "UPDATE playlists SET folder_id=NULL WHERE folder_id=$id";
            playlists.Parameters.AddWithValue("$id", folderId);
            await playlists.ExecuteNonQueryAsync();
        }
        await using (var children = c.CreateCommand())
        {
            children.Transaction = (SqliteTransaction)tx;
            children.CommandText = "UPDATE playlist_folders SET parent_id=NULL WHERE parent_id=$id";
            children.Parameters.AddWithValue("$id", folderId);
            await children.ExecuteNonQueryAsync();
        }
        await using (var delete = c.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)tx;
            delete.CommandText = "DELETE FROM playlist_folders WHERE id=$id";
            delete.Parameters.AddWithValue("$id", folderId);
            await delete.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    private async Task<bool> ExistsAsync(long id)
    {
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM playlist_folders WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    private async Task<long?> GetParentAsync(long id)
    {
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT parent_id FROM playlist_folders WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    private async Task ExecAsync(string sql, params (string Name, object Value)[] args)
    {
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var arg in args) cmd.Parameters.AddWithValue(arg.Name, arg.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
