using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;
namespace NeoPlayer.Windows.Services;
public sealed class PlaylistTransferService(NeoDatabase db)
{
 public async Task ExportM3u8Async(Playlist p,string path){var songs=await db.GetPlaylistSongsAsync(p.Id);var dir=Path.GetDirectoryName(path)??"";var lines=new List<string>{"#EXTM3U"};foreach(var s in songs){lines.Add($"#EXTINF:{s.DurationMs/1000},{s.Artist} - {s.Title}");lines.Add(Path.GetRelativePath(dir,s.Path));}await File.WriteAllLinesAsync(path,lines,new System.Text.UTF8Encoding(true));}
 public async Task<long> ImportM3u8Async(string path,string? title=null){var id=await db.CreatePlaylistAsync(title??Path.GetFileNameWithoutExtension(path));var songs=await db.GetSongsAsync(true);var byPath=songs.ToDictionary(x=>Path.GetFullPath(x.Path),StringComparer.OrdinalIgnoreCase);var dir=Path.GetDirectoryName(path)??"";foreach(var raw in await File.ReadAllLinesAsync(path)){var line=raw.Trim();if(line.Length==0||line.StartsWith('#'))continue;var p=Path.IsPathRooted(line)?line:Path.GetFullPath(Path.Combine(dir,line));if(byPath.TryGetValue(p,out var s))await db.AddToPlaylistAsync(id,s.Id);}return id;}
}