using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;
using NeoPlayer.Windows.Services;

namespace NeoPlayer.Tests;

public sealed class CoreTests
{
    [Fact]
    public void LrcParser_SortsAndParsesFractions()
    {
        var lines = LyricsService.ParseLrc("[00:02.50]B\n[00:01.005]A");
        Assert.Equal(2, lines.Count);
        Assert.Equal("A", lines[0].Text);
        Assert.Equal(TimeSpan.FromMilliseconds(1005), lines[0].Time);
    }

    [Fact]
    public async Task Database_PlaylistAndCyclePrevention_Work()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var db = new NeoDatabase(Path.Combine(root, "db.sqlite")); await db.InitializeAsync();
            var id = await db.UpsertSongAsync(new Song(0, Path.Combine(root,"x.mp3"), "X", "Y", "Z", "", 1, 0, 0, null, false, false, 1, 0, 0));
            var p = await db.CreatePlaylistAsync("P"); await db.AddToPlaylistAsync(p, id); Assert.Single(await db.GetPlaylistSongsAsync(p));
            var a = await db.CreateFolderAsync("a", null); var b = await db.CreateFolderAsync("b", a); await Assert.ThrowsAsync<InvalidOperationException>(() => db.MoveFolderAsync(a, b));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Collections_Hidden_Metadata_Lyrics_AndCategories_Work()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "db.sqlite");
        try
        {
            var db = new NeoDatabase(dbPath);
            await db.InitializeAsync();
            var first = await db.UpsertSongAsync(new Song(0, Path.Combine(root, "one.mp3"), "One", "Artist A", "Album A", "Rock", 1000, 1, 2026, null, false, false, 1, 0, 0));
            var second = await db.UpsertSongAsync(new Song(0, Path.Combine(root, "two.mp3"), "Two", "Artist A", "Album A", "Rock", 1000, 2, 2026, null, false, false, 2, 0, 0));
            var service = new LibraryCollectionsService(dbPath);

            var albums = await service.GetCollectionsAsync("album");
            Assert.Single(albums);
            Assert.Equal(2, albums[0].Count);
            Assert.Equal(2, (await service.GetSongsForCollectionAsync(albums[0])).Count);

            await service.SetHiddenAsync(second, true);
            Assert.Single(await service.GetHiddenSongsAsync());
            Assert.Single(await service.GetSongsForCollectionAsync(albums[0]));
            await service.SetHiddenAsync(second, false);

            await service.UpdateMetadataAsync(first, "Renamed", "Artist B", "Album B", "Electronic", null);
            var renamed = await service.GetSongByPathAsync(Path.Combine(root, "one.mp3"));
            Assert.NotNull(renamed);
            Assert.Equal("Renamed", renamed!.Title);
            Assert.Equal("Artist B", renamed.Artist);

            var lyrics = new LyricsDocument("plain", "[00:01.00]hello", "ترجمه", "romanized");
            await service.SaveLyricsAsync(first, lyrics);
            Assert.Equal(lyrics, await service.GetLyricsAsync(first));

            var categoryId = await service.CreateCategoryAsync("Focus");
            await service.AddToCategoryAsync(categoryId, first);
            Assert.Single(await service.GetCategorySongsAsync(categoryId));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PlaylistMutation_ReordersAndRemovesDeterministically()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "db.sqlite");
        try
        {
            var db = new NeoDatabase(dbPath);
            await db.InitializeAsync();
            var a = await db.UpsertSongAsync(new Song(0, Path.Combine(root, "a.mp3"), "A", "Artist", "Album", "", 1, 1, 0, null, false, false, 1, 0, 0));
            var b = await db.UpsertSongAsync(new Song(0, Path.Combine(root, "b.mp3"), "B", "Artist", "Album", "", 1, 2, 0, null, false, false, 2, 0, 0));
            var c = await db.UpsertSongAsync(new Song(0, Path.Combine(root, "c.mp3"), "C", "Artist", "Album", "", 1, 3, 0, null, false, false, 3, 0, 0));
            var playlist = await db.CreatePlaylistAsync("Order");
            var service = new LibraryCollectionsService(dbPath);
            await service.AddToPlaylistAsync(playlist, a);
            await service.AddToPlaylistAsync(playlist, b);
            await service.AddToPlaylistAsync(playlist, c);
            await service.MovePlaylistSongAsync(playlist, c, 0);
            var ordered = await service.GetPlaylistSongsAsync(playlist);
            Assert.Equal(new[] { c, a, b }, ordered.Select(x => x.Id));
            await service.RemoveFromPlaylistAsync(playlist, a);
            var remaining = await service.GetPlaylistSongsAsync(playlist);
            Assert.Equal(new[] { c, b }, remaining.Select(x => x.Id));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PlaylistFolders_PreventCycles_AndAssignPlaylists()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "db.sqlite");
        try
        {
            var db = new NeoDatabase(dbPath);
            await db.InitializeAsync();
            var manager = new PlaylistFolderManager(dbPath);
            var rootFolder = await manager.CreateAsync("Root");
            var childFolder = await manager.CreateAsync("Child", rootFolder);
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.MoveAsync(rootFolder, childFolder));

            var playlistId = await db.CreatePlaylistAsync("Inside");
            await manager.AssignPlaylistAsync(playlistId, childFolder);
            var playlist = (await db.GetPlaylistsAsync()).Single(x => x.Id == playlistId);
            Assert.Equal(childFolder, playlist.FolderId);

            await manager.DeleteAsync(childFolder);
            playlist = (await db.GetPlaylistsAsync()).Single(x => x.Id == playlistId);
            Assert.Null(playlist.FolderId);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task M3u8_ExportImport_RoundTripsLibraryTracks()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "db.sqlite");
        try
        {
            var db = new NeoDatabase(dbPath);
            await db.InitializeAsync();
            var collections = new LibraryCollectionsService(dbPath);
            var transfer = new PlaylistTransferService(db, collections);
            var songPath = Path.Combine(root, "song.mp3");
            await File.WriteAllBytesAsync(songPath, new byte[] { 0 });
            var songId = await db.UpsertSongAsync(new Song(0, songPath, "Song", "Artist", "Album", "Rock", 123000, 1, 2026, null, false, false, 1, 0, 0));
            var playlistId = await db.CreatePlaylistAsync("Export me");
            await collections.AddToPlaylistAsync(playlistId, songId);
            var playlist = (await db.GetPlaylistsAsync()).Single(x => x.Id == playlistId);
            var m3u = Path.Combine(root, "mix.m3u8");

            await transfer.ExportM3u8Async(playlist, m3u);
            var exported = await File.ReadAllTextAsync(m3u);
            Assert.Contains("#EXTM3U", exported);
            Assert.Contains(songPath, exported);

            var imported = await transfer.ImportM3u8Async(m3u, "Imported");
            var importedSongs = await collections.GetPlaylistSongsAsync(imported.Id);
            Assert.Single(importedSongs);
            Assert.Equal(songId, importedSongs[0].Id);
        }
        finally { Directory.Delete(root, true); }
    }
}
