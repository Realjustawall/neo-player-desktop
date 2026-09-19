using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;
namespace NeoPlayer.Windows.Services;
public sealed class OfflineBackupEngine(NeoDatabase db,RecommendationService recommendations,SettingsService settings)
{
    public async Task<List<Song>> RefreshAsync(IEnumerable<Song> songs,HashSet<long> favorites)
    {
        var all=songs.ToList();var rec=await recommendations.GetAsync(all,favorites,Math.Min(500,settings.Current.OfflineBackupLimit*3));var selected=new List<(Song Song,float Score,string Reason,AdvancedAudioAnalysis? A)>();
        foreach(var x in rec){var a=await db.GetAdvancedAnalysisAsync(x.Song.Id);if(settings.Current.OfflineBackupMood!="all"&&!string.Equals(a?.Mood,settings.Current.OfflineBackupMood,StringComparison.OrdinalIgnoreCase))continue;if(settings.Current.OfflineBackupGenre!="all"&&!string.Equals(x.Song.Genre,settings.Current.OfflineBackupGenre,StringComparison.OrdinalIgnoreCase))continue;selected.Add((x.Song,x.Score,x.Reason,a));if(selected.Count>=settings.Current.OfflineBackupLimit)break;}
        var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();await db.SaveOfflineBackupEntriesAsync(selected.Select((x,i)=>new OfflineBackupEntry(x.Song.Id,i,x.Score,x.Reason,x.A?.Mood??"unknown",x.Song.Genre,now)));return selected.Select(x=>x.Song).ToList();
    }
    public async Task<List<Song>> GetAsync(IEnumerable<Song> songs){var map=songs.ToDictionary(x=>x.Id);return (await db.GetOfflineBackupEntriesAsync()).Where(x=>map.ContainsKey(x.SongId)).Select(x=>map[x.SongId]).ToList();}
}
