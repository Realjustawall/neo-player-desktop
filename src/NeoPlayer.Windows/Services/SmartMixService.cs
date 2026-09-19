using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.Services;

public sealed class SmartMixService(NeoDatabase db)
{
    public async Task<List<Song>> BuildRadioAsync(Song seed, int count = 30)
    {
        var songs = await db.GetSongsAsync();
        return songs.Where(x => x.Id != seed.Id)
            .OrderByDescending(x => Score(seed, x))
            .ThenByDescending(x => x.Favorite)
            .ThenByDescending(x => x.PlayCount)
            .Take(count).Prepend(seed).ToList();
    }

    private static double Score(Song a, Song b)
    {
        double s = 0;
        if (Eq(a.Artist, b.Artist)) s += 5; if (Eq(a.Album, b.Album)) s += 2; if (!string.IsNullOrWhiteSpace(a.Genre) && Eq(a.Genre, b.Genre)) s += 3; if (b.Favorite) s += 1.5; if (b.PlayCount == 0) s += .25;
        return s + Random.Shared.NextDouble() * .15;
    }
    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
