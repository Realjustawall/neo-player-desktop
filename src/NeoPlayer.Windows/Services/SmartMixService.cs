using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;
namespace NeoPlayer.Windows.Services;
public sealed class SmartMixService(NeoDatabase db)
{
    public async Task<List<Song>> RecentlyPlayedAsync(IEnumerable<Song> songs,int limit=50){var h=(await db.GetHistoryAsync()).OrderByDescending(x=>x.LastPlayedAt).Select(x=>x.SongId).ToList();var map=songs.ToDictionary(x=>x.Id);return h.Where(map.ContainsKey).Take(limit).Select(x=>map[x]).ToList();}
    public async Task<List<Song>> MostPlayedAsync(IEnumerable<Song> songs,int limit=50){var h=(await db.GetHistoryAsync()).OrderByDescending(x=>x.PlayCount).ThenByDescending(x=>x.TotalListeningMs).Select(x=>x.SongId).ToList();var map=songs.ToDictionary(x=>x.Id);return h.Where(map.ContainsKey).Take(limit).Select(x=>map[x]).ToList();}
    public async Task<List<Song>> NeverPlayedAsync(IEnumerable<Song> songs,int limit=50){var h=(await db.GetHistoryAsync()).Select(x=>x.SongId).ToHashSet();return songs.Where(x=>!h.Contains(x.Id)).Take(limit).ToList();}
    public async Task<List<Song>> ForgottenFavoritesAsync(IEnumerable<Song> songs,HashSet<long> fav,int limit=50){var h=(await db.GetHistoryAsync()).ToDictionary(x=>x.SongId);var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();return songs.Where(x=>fav.Contains(x.Id)).OrderBy(x=>h.GetValueOrDefault(x.Id)?.LastPlayedAt??0).ThenByDescending(x=>(now-(h.GetValueOrDefault(x.Id)?.LastPlayedAt??0))).Take(limit).ToList();}
    public async Task<List<Song>> TimeOfDayAsync(IEnumerable<Song> songs,int hour,int limit=50){var list=songs.ToList();var scored=new List<(Song s,float score)>();foreach(var s in list){var a=await db.GetAdvancedAnalysisAsync(s.Id);float e=a?.Energy??.5f,v=a?.Valence??.5f;float target=hour<7?.2f:hour<12?.55f:hour<18?.75f:hour<22?.55f:.3f;float score=1-Math.Abs(e-target)+v*.12f;scored.Add((s,score));}return scored.OrderByDescending(x=>x.score).Take(limit).Select(x=>x.s).ToList();}
}
