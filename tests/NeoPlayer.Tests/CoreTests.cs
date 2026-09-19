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
        Assert.Equal(2, lines.Count); Assert.Equal("A", lines[0].Text); Assert.Equal(TimeSpan.FromMilliseconds(1005), lines[0].Time);
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
}
