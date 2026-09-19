using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;
using NeoPlayer.Windows.Services;

namespace NeoPlayer.Windows.Core;

public static class SelfTestRunner
{
    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "neo-player-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var old = Environment.GetEnvironmentVariable("NEO_DATA_DIR");
        Environment.SetEnvironmentVariable("NEO_DATA_DIR", root);
        try
        {
            var settings = new SettingsService(); await settings.LoadAsync(); settings.Value.Volume = 0.42; settings.Value.Language = "fa"; await settings.SaveAsync();
            var settings2 = new SettingsService(); await settings2.LoadAsync(); Assert(Math.Abs(settings2.Value.Volume - 0.42) < 0.001, "settings roundtrip"); Assert(settings2.Value.Language == "fa", "language roundtrip");

            var db = new NeoDatabase(Path.Combine(root, "test.db")); await db.InitializeAsync();
            var song = new Song(0, Path.Combine(root, "song.mp3"), "Title", "Artist", "Album", "Genre", 180000, 1, 2026, null, false, false, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 0, 0);
            var id = await db.UpsertSongAsync(song); Assert(id > 0, "song insert"); Assert((await db.SearchAsync("Tit")).Count == 1, "search");
            var playlist = await db.CreatePlaylistAsync("Test"); await db.AddToPlaylistAsync(playlist, id); Assert((await db.GetPlaylistSongsAsync(playlist)).Count == 1, "playlist membership");
            var a = await db.CreateFolderAsync("A", null); var b = await db.CreateFolderAsync("B", a); var cycleRejected = false; try { await db.MoveFolderAsync(a, b); } catch (InvalidOperationException) { cycleRejected = true; } Assert(cycleRejected, "folder cycle prevention");

            var lrc = LyricsService.ParseLrc("[00:01.20]Hello\n[00:02.500]World"); Assert(lrc.Count == 2 && lrc[0].Time == TimeSpan.FromMilliseconds(1200), "LRC parser");
            Console.WriteLine("NEO_SELF_TEST_OK"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("NEO_SELF_TEST_FAILED: " + ex); return 2; }
        finally { Environment.SetEnvironmentVariable("NEO_DATA_DIR", old); try { Directory.Delete(root, true); } catch { } }
    }

    private static void Assert(bool condition, string name) { if (!condition) throw new InvalidOperationException("Self-test failed: " + name); }
}
