using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vosk;
namespace NeoPlayer.Windows.Services;
public sealed class SpeechModelService(NeoDatabase db,LyricsService lyrics)
{
 public async Task<OfflineSpeechModel> ImportModelAsync(string directory,string language,string displayName,CancellationToken ct=default){if(!Directory.Exists(directory))throw new DirectoryNotFoundException(directory);string id=$"{language}-{Path.GetFileName(directory)}-{Math.Abs(directory.GetHashCode())}";string dest=Path.Combine(AppPaths.Models,id);if(Directory.Exists(dest))Directory.Delete(dest,true);await CopyDirAsync(directory,dest,ct);using var test=new Model(dest);var m=new OfflineSpeechModel(id,displayName,language,dest,16000,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());await db.SaveSpeechModelAsync(m);return m;}
 public Task<List<OfflineSpeechModel>> ModelsAsync()=>db.GetSpeechModelsAsync();
 public async Task DiscoverBundledModelsAsync(CancellationToken ct=default)
 {
  var roots=new[]{Path.Combine(AppContext.BaseDirectory,"models"),Path.Combine(AppContext.BaseDirectory,"assets","models")};
  var existing=await db.GetSpeechModelsAsync();
  foreach(var root in roots.Where(Directory.Exists))
  {
   foreach(var dir in Directory.EnumerateDirectories(root))
   {
    ct.ThrowIfCancellationRequested();
    if(!File.Exists(Path.Combine(dir,"conf","mfcc.conf")) && !Directory.EnumerateFiles(dir,"*",SearchOption.AllDirectories).Any())continue;
    var name=Path.GetFileName(dir); var lower=name.ToLowerInvariant(); var lang=lower.Contains("fa")||lower.Contains("persian")?"fa":"en";
    var id="bundled-"+name; if(existing.Any(x=>x.Id.Equals(id,StringComparison.OrdinalIgnoreCase)))continue;
    try{using var test=new Model(dir); await db.SaveSpeechModelAsync(new(id,name,lang,dir,16000,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));}
    catch{ }
   }
  }
 }

 public async Task<OfflineTranscript> TranscribeAsync(Song song,string modelId,CancellationToken ct=default)
 {
  var model=(await db.GetSpeechModelsAsync()).FirstOrDefault(x=>x.Id==modelId)??throw new InvalidOperationException("Speech model not found");
  var words=new List<Word>();var plain=new List<string>();using var vosk=new Model(model.LocalPath);using var rec=new VoskRecognizer(vosk,16000);rec.SetWords(true);
  var psi=new ProcessStartInfo(FfmpegLocator.Require()){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
  foreach(var arg in new[]{"-hide_banner","-loglevel","error","-i",song.Path,"-vn","-ac","1","-ar","16000","-f","s16le","pipe:1"})psi.ArgumentList.Add(arg);
  using var p=Process.Start(psi)??throw new InvalidOperationException("Could not start FFmpeg");var buf=new byte[8192];
  while(true){ct.ThrowIfCancellationRequested();int n=await p.StandardOutput.BaseStream.ReadAsync(buf,ct);if(n<=0)break;if(rec.AcceptWaveform(buf,n))Collect(rec.Result(),words,plain);}
  Collect(rec.FinalResult(),words,plain);await p.WaitForExitAsync(ct);if(p.ExitCode!=0&&words.Count==0)throw new InvalidOperationException("FFmpeg/Vosk transcription failed for this track.");
  var lines=GroupToLines(words);string lrc=lyrics.ToLrc(lines);float avg=words.Count==0?0:words.Average(x=>x.Conf);var t=new OfflineTranscript(song.Id,"vosk",model.Id,model.Language,string.Join(' ',plain),lrc,JsonSerializer.Serialize(words),avg,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
  await db.SaveTranscriptAsync(t);await db.SaveLyricsAsync(new(song.Id,lrc,"","",true,"vosk",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));return t;
 }
 public async Task<LyricsRecord> ForceAlignAsync(Song song,string plainLyrics,string modelId,CancellationToken ct=default){var tr=await TranscribeAsync(song,modelId,ct);var recognized=JsonSerializer.Deserialize<List<Word>>(tr.WordTimedJson)??[];var lyricLines=plainLyrics.Replace("\r","").Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);var flat=lyricLines.SelectMany((l,li)=>Tokenize(l).Select(w=>(Word:w,Line:li))).ToList();var rw=recognized.Select(x=>Normalize(x.WordText)).ToList();var map=new Dictionary<int,List<Word>>();int cursor=0;foreach(var token in flat){string n=Normalize(token.Word);int best=-1;for(int j=cursor;j<Math.Min(rw.Count,cursor+10);j++){if(rw[j]==n||Similarity(rw[j],n)>.72){best=j;break;}}if(best<0)best=Math.Min(cursor,Math.Max(0,rw.Count-1));if(recognized.Count>0){if(!map.TryGetValue(token.Line,out var l))map[token.Line]=l=[];l.Add(recognized[best]);cursor=Math.Min(recognized.Count,best+1);}}var lines=new List<LrcLine>();for(int i=0;i<lyricLines.Length;i++){long t=map.TryGetValue(i,out var ws)&&ws.Count>0?(long)(ws[0].Start*1000):(i==0?0:lines[^1].TimeMs+2500);lines.Add(new(t,lyricLines[i]));}var lrc=lyrics.ToLrc(lines);var result=new LyricsRecord(song.Id,lrc,"","",true,"forced-align",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());await db.SaveLyricsAsync(result);return result;}
 static void Collect(string json,List<Word> words,List<string> plain){try{using var doc=JsonDocument.Parse(json);if(doc.RootElement.TryGetProperty("text",out var txt)&&!string.IsNullOrWhiteSpace(txt.GetString()))plain.Add(txt.GetString()!);if(doc.RootElement.TryGetProperty("result",out var arr))foreach(var x in arr.EnumerateArray())words.Add(new(x.GetProperty("word").GetString()??"",x.GetProperty("start").GetDouble(),x.GetProperty("end").GetDouble(),x.TryGetProperty("conf",out var c)?c.GetSingle():0));}catch{}}
 static List<LrcLine> GroupToLines(List<Word> w){var l=new List<LrcLine>();for(int i=0;i<w.Count;){int start=i;double t=w[i].Start;while(i<w.Count&&i-start<8&&w[i].End-t<5.5)i++;l.Add(new((long)(t*1000),string.Join(' ',w.GetRange(start,i-start).Select(x=>x.WordText))));}return l;}
 static IEnumerable<string> Tokenize(string s)=>Regex.Matches(s,@"[\p{L}\p{N}']+").Select(x=>x.Value);static string Normalize(string s)=>Regex.Replace(s.ToLowerInvariant(),@"[^\p{L}\p{N}]","");static double Similarity(string a,string b){if(a==b)return 1;if(a.Length==0||b.Length==0)return 0;int[,] d=new int[a.Length+1,b.Length+1];for(int i=0;i<=a.Length;i++)d[i,0]=i;for(int j=0;j<=b.Length;j++)d[0,j]=j;for(int i=1;i<=a.Length;i++)for(int j=1;j<=b.Length;j++)d[i,j]=Math.Min(Math.Min(d[i-1,j]+1,d[i,j-1]+1),d[i-1,j-1]+(a[i-1]==b[j-1]?0:1));return 1-d[a.Length,b.Length]/(double)Math.Max(a.Length,b.Length);}
 static async Task CopyDirAsync(string src,string dst,CancellationToken ct){Directory.CreateDirectory(dst);foreach(var file in Directory.EnumerateFiles(src,"*",SearchOption.AllDirectories)){ct.ThrowIfCancellationRequested();var rel=Path.GetRelativePath(src,file);var to=Path.Combine(dst,rel);Directory.CreateDirectory(Path.GetDirectoryName(to)!);await using var a=File.OpenRead(file);await using var b=File.Create(to);await a.CopyToAsync(b,ct);}}
 public sealed record Word(string WordText,double Start,double End,float Conf);
}