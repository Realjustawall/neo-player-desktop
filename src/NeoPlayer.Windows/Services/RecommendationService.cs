using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.Services;

public sealed class RecommendationService(NeoDatabase db)
{
    public async Task<List<(Song Song, float Score, string Reason)>> GetAsync(IEnumerable<Song> songs, HashSet<long> favorites, int limit = 30)
    {
        var list = songs.ToList();
        var hist = (await db.GetHistoryAsync()).ToDictionary(x => x.SongId);
        var fb = (await db.GetFeedbackAsync()).ToDictionary(x => x.SongId);
        var analyses = (await db.GetAdvancedAnalysesAsync()).ToDictionary(x => x.SongId);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var scored = new List<(Song, float, string)>();
        foreach (var s in list)
        {
            hist.TryGetValue(s.Id, out var h); fb.TryGetValue(s.Id, out var f);
            float score = 0; var reasons = new List<string>();
            if (favorites.Contains(s.Id)) { score += 2; reasons.Add("liked"); }
            if (h is not null)
            {
                score += Math.Min(3, h.PlayCount * .35f) - Math.Min(3, h.SkipCount * .6f);
                double days = (now - h.LastPlayedAt) / 86400000d;
                if (days > 30 && favorites.Contains(s.Id)) { score += 1.6f; reasons.Add("forgotten favorite"); }
                else if (days < 7) { score += .45f; reasons.Add("recently played"); }
            }
            else { score += .8f; reasons.Add("never played"); }
            if (f is not null) score += f.Boost * 1.4f - f.DismissCount * 1.3f;
            analyses.TryGetValue(s.Id, out var a);
            if (a is not null)
            {
                int hr = DateTime.Now.Hour;
                if (hr >= 20 || hr < 6) score += (1 - a.Energy) * .7f;
                else if (hr >= 12 && hr < 19) score += a.Energy * .5f;
            }
            score += (float)(Random.Shared.NextDouble() * .25);
            scored.Add((s, score, string.Join(", ", reasons.Take(2))));
        }
        return scored.OrderByDescending(x => x.Item2).Take(limit).Select(x => (x.Item1, x.Item2, x.Item3)).ToList();
    }

    public async Task<List<Song>> BuildRadioAsync(Song seed, IEnumerable<Song> songs, HashSet<long> favorites, int limit = 60)
    {
        var candidates = songs.Where(x => x.Id != seed.Id).ToList();
        var analyses = (await db.GetAdvancedAnalysesAsync()).ToDictionary(x => x.SongId);
        analyses.TryGetValue(seed.Id, out var seedA);
        var hist = (await db.GetHistoryAsync()).ToDictionary(x => x.SongId);
        var fb = (await db.GetFeedbackAsync()).ToDictionary(x => x.SongId);
        var scored = new List<(Song Song, double Score)>();
        foreach (var song in candidates)
        {
            double score = 0;
            if (!string.IsNullOrWhiteSpace(seed.Artist) && song.Artist.Equals(seed.Artist, StringComparison.OrdinalIgnoreCase)) score += 4;
            if (!string.IsNullOrWhiteSpace(seed.Genre) && song.Genre.Equals(seed.Genre, StringComparison.OrdinalIgnoreCase)) score += 2.3;
            if (!string.IsNullOrWhiteSpace(seed.AlbumArtist) && song.AlbumArtist.Equals(seed.AlbumArtist, StringComparison.OrdinalIgnoreCase)) score += 1.1;
            if (favorites.Contains(song.Id)) score += .55;
            if (hist.TryGetValue(song.Id, out var h)) score += Math.Min(1.2, h.PlayCount * .12) - Math.Min(2.5, h.SkipCount * .45);
            if (fb.TryGetValue(song.Id, out var f)) score += f.Boost * .8 - f.DismissCount * .9;
            analyses.TryGetValue(song.Id, out var a);
            if (seedA is not null && a is not null)
            {
                if (seedA.Bpm > 0 && a.Bpm > 0) score += Math.Max(0, 2.2 - Math.Abs(seedA.Bpm - a.Bpm) / 8.0);
                score += Math.Max(0, 1.8 - Math.Abs(seedA.Energy - a.Energy) * 3.2);
                score += Math.Max(0, .9 - Math.Abs(seedA.Valence - a.Valence) * 1.6);
                if (CamelotCompatible(seedA.CamelotKey, a.CamelotKey)) score += 1.7;
                if (seedA.Mood == a.Mood && seedA.Mood != "unknown") score += .65;
            }
            score += Random.Shared.NextDouble() * .22;
            scored.Add((song, score));
        }
        var ordered = scored.OrderByDescending(x => x.Score).Take(Math.Max(1, limit - 1)).Select(x => x.Song).ToList();
        ordered.Insert(0, seed);
        await FeedbackAsync(seed.Id, 0, false, seeded: true);
        return ordered;
    }

    static bool CamelotCompatible(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        static (int N, char Mode)? Parse(string key)
        {
            if (key.Length < 2 || !int.TryParse(key[..^1], out var n)) return null;
            return (n, char.ToUpperInvariant(key[^1]));
        }
        var x = Parse(a); var y = Parse(b); if (x is null || y is null) return false;
        var d = Math.Abs(x.Value.N - y.Value.N); d = Math.Min(d, 12 - d);
        return (x.Value.Mode == y.Value.Mode && d <= 1) || (x.Value.N == y.Value.N && x.Value.Mode != y.Value.Mode);
    }

    public Task FeedbackAsync(long songId, int delta, bool dismissed = false) => FeedbackAsync(songId, delta, dismissed, false);
    async Task FeedbackAsync(long songId, int delta, bool dismissed, bool seeded)
    {
        var all = await db.GetFeedbackAsync();
        var f = all.FirstOrDefault(x => x.SongId == songId) ?? new(songId, 0, 0, 0);
        await db.SaveFeedbackAsync(f with
        {
            Boost = Math.Clamp(f.Boost + delta, -5, 5),
            DismissCount = f.DismissCount + (dismissed ? 1 : 0),
            LastSeededAt = seeded ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : f.LastSeededAt
        });
    }
}
