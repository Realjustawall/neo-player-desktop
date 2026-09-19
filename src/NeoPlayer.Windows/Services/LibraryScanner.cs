using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace NeoPlayer.Windows.Services;
public sealed class LibraryScanner : IDisposable
{
    readonly NeoDatabase db; readonly SettingsService settings; readonly ArtworkService artwork; readonly SemaphoreSlim scanGate=new(1,1); readonly List<FileSystemWatcher> watchers=[];
    static readonly HashSet<string> Ext=new(StringComparer.OrdinalIgnoreCase){".mp3",".flac",".m4a",".aac",".ogg",".opus",".wav",".wma",".aiff",".ape",".mka"};
    public event EventHandler? Changed;
    public LibraryScanner(NeoDatabase d,SettingsService s,ArtworkService a){db=d;settings=s;artwork=a;}
    public async Task<List<Song>> ScanAsync(CancellationToken ct=default)
    {
        await scanGate.WaitAsync(ct);try{var roots=NormalizeRoots(settings.Current.IncludedFolders.Count>0?settings.Current.IncludedFolders:DefaultRoots());var availableRoots=roots.Where(Directory.Exists).ToList();var found=new List<Song>();var ids=new HashSet<long>();foreach(var root in availableRoots){foreach(var path in EnumerateSafe(root)){ct.ThrowIfCancellationRequested();if(!Ext.Contains(Path.GetExtension(path))||IsExcluded(path))continue;try{var song=ReadSong(path,root);if(song.DurationMs<settings.Current.MinDurationMs)continue;found.Add(song);ids.Add(song.Id);}catch{}}}if(availableRoots.Count>0){await db.UpsertSongsAsync(found);await db.DeleteMissingSongsInRootsAsync(ids,availableRoots);}SetupWatchers(roots);Changed?.Invoke(this,EventArgs.Empty);return await db.GetSongsAsync();}finally{scanGate.Release();}
    }
    IEnumerable<string> EnumerateSafe(string root)
    {
        var q=new Stack<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);q.Push(root);
        while(q.Count>0)
        {
            var d=q.Pop();string full;try{full=Path.GetFullPath(d);}catch{continue;}if(!seen.Add(full))continue;
            try{if((File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0&& !full.Equals(Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase))continue;}catch{continue;}
            IEnumerable<string> fs=[];IEnumerable<string> ds=[];try{fs=Directory.EnumerateFiles(full);ds=Directory.EnumerateDirectories(full);}catch{}
            foreach(var f in fs)yield return f;foreach(var x in ds)q.Push(x);
        }
    }
    static List<string> NormalizeRoots(IEnumerable<string> values)
    {
        var list=values.Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>{try{return Path.GetFullPath(x).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);}catch{return x.Trim();}}).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x.Length).ToList();
        var result=new List<string>();foreach(var root in list)if(!result.Any(parent=>IsUnder(root,parent)))result.Add(root);return result;
    }
    bool IsExcluded(string p)=>settings.Current.ExcludedFolders.Any(x=>IsUnder(p,x));
    static bool IsUnder(string p,string root){try{p=Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;return p.StartsWith(root,StringComparison.OrdinalIgnoreCase);}catch{return false;}}
    Song ReadSong(string path,string root){using var tag=TagLib.File.Create(path);var fi=new FileInfo(path);long id=StableFileId(path);string title=string.IsNullOrWhiteSpace(tag.Tag.Title)?Path.GetFileNameWithoutExtension(path):tag.Tag.Title;string artist=tag.Tag.FirstPerformer??"";string album=tag.Tag.Album??"";string aa=tag.Tag.FirstAlbumArtist??"";string genre=tag.Tag.FirstGenre??"";int br=tag.Properties.AudioBitrate*1000;return new Song(id,path,title,artist,album,aa,genre,(int)tag.Tag.Year,(long)tag.Properties.Duration.TotalMilliseconds,(int)tag.Tag.Track,(int)tag.Tag.Disc,tag.Tag.FirstComposer??"",br,Mime(path),fi.Length,Path.GetRelativePath(root,path),new DateTimeOffset(fi.CreationTimeUtc).ToUnixTimeMilliseconds(),new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds());}
    static string Mime(string p)=>Path.GetExtension(p).ToLowerInvariant() switch{".mp3"=>"audio/mpeg",".flac"=>"audio/flac",".wav"=>"audio/wav",".m4a"=>"audio/mp4",".ogg"=>"audio/ogg",".opus"=>"audio/opus",_=>"audio/*"};
    static long StableFileId(string path){try{using var h=File.OpenHandle(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);if(GetFileInformationByHandle(h,out var i)){ulong id=((ulong)i.FileIndexHigh<<32)|i.FileIndexLow;ulong v=((ulong)i.VolumeSerialNumber<<32)^id;return unchecked((long)(v&0x7FFFFFFFFFFFFFFF));}}catch{}return unchecked((long)BitConverter.ToUInt64(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path))),0)&0x7FFFFFFFFFFFFFFF);}
    void SetupWatchers(IEnumerable<string> roots){foreach(var w in watchers)w.Dispose();watchers.Clear();foreach(var root in roots.Where(Directory.Exists)){try{var w=new FileSystemWatcher(root){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite|NotifyFilters.Size,EnableRaisingEvents=true};FileSystemEventHandler h=(_,__)=>DebouncedScan();RenamedEventHandler rh=(_,__)=>DebouncedScan();w.Created+=h;w.Deleted+=h;w.Changed+=h;w.Renamed+=rh;watchers.Add(w);}catch{}}}
    CancellationTokenSource? debounce;void DebouncedScan(){debounce?.Cancel();debounce=new();var token=debounce.Token;_=Task.Run(async()=>{try{await Task.Delay(900,token);await ScanAsync(token);}catch{}});}
    static List<string> DefaultRoots(){var l=new List<string>();var m=Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);if(Directory.Exists(m))l.Add(m);return l;}
    public void Dispose(){debounce?.Cancel();foreach(var w in watchers)w.Dispose();scanGate.Dispose();}
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetFileInformationByHandle(SafeFileHandle h,out BY_HANDLE_FILE_INFORMATION info);
    [StructLayout(LayoutKind.Sequential)]struct BY_HANDLE_FILE_INFORMATION{public uint FileAttributes;public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime,LastAccessTime,LastWriteTime;public uint VolumeSerialNumber,FileSizeHigh,FileSizeLow,NumberOfLinks,FileIndexHigh,FileIndexLow;}
}