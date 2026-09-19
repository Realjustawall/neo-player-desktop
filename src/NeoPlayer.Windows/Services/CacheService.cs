using NeoPlayer.Windows.Data;
namespace NeoPlayer.Windows.Services;
public sealed class CacheService(NeoDatabase db)
{
 public long Size(){if(!Directory.Exists(AppPaths.Cache))return 0;return Directory.EnumerateFiles(AppPaths.Cache,"*",SearchOption.AllDirectories).Sum(x=>{try{return new FileInfo(x).Length;}catch{return 0;}});}
 public async Task ClearAsync(){if(Directory.Exists(AppPaths.Cache)){foreach(var f in Directory.EnumerateFiles(AppPaths.Cache,"*",SearchOption.AllDirectories))try{File.Delete(f);}catch{}}await db.PurgeRegeneratableAnalysisAsync();AppPaths.Ensure();}
}