using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;
using System.Drawing;
using System.Security.Cryptography;
namespace NeoPlayer.Windows.Services;
public sealed class ArtworkService(NeoDatabase db)
{
    public async Task<string?> ResolveAsync(Song song,CancellationToken ct=default)
    {
        if(!string.IsNullOrWhiteSpace(song.CustomArtworkPath)&&File.Exists(song.CustomArtworkPath))return song.CustomArtworkPath;
        var key=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(song.Path))).Substring(0,20)+".jpg";var cache=Path.Combine(AppPaths.ArtworkCache,key);if(File.Exists(cache))return cache;
        try{using var f=TagLib.File.Create(song.Path);var pic=f.Tag.Pictures?.FirstOrDefault();if(pic is not null){await File.WriteAllBytesAsync(cache,pic.Data.Data,ct);return cache;}}catch{}
        var dir=Path.GetDirectoryName(song.Path); if(dir is not null)foreach(var n in new[]{"cover.jpg","folder.jpg","front.jpg","cover.png","folder.png"}){var p=Path.Combine(dir,n);if(File.Exists(p))return p;}return null;
    }
    public async Task<int> DominantColorAsync(Song song,CancellationToken ct=default)
    {
        var p=await ResolveAsync(song,ct);if(p is null)return unchecked((int)0xFFFF7A1A);
        try{using var bmp=new Bitmap(p);long r=0,g=0,b=0,c=0;for(int y=0;y<bmp.Height;y+=Math.Max(1,bmp.Height/32))for(int x=0;x<bmp.Width;x+=Math.Max(1,bmp.Width/32)){var px=bmp.GetPixel(x,y);if(px.GetBrightness()<.08||px.GetBrightness()>.92)continue;r+=px.R;g+=px.G;b+=px.B;c++;}if(c==0)return unchecked((int)0xFFFF7A1A);return unchecked((int)(0xFF000000|((r/c)<<16)|((g/c)<<8)|(b/c)));}catch{return unchecked((int)0xFFFF7A1A);}
    }
    public async Task SetCustomArtworkAsync(Song song,string path){var ext=Path.GetExtension(path);var dest=Path.Combine(AppPaths.Assets,$"art-{song.Id}{ext}");File.Copy(path,dest,true);await db.SetSongArtworkAsync(song.Id,dest);}
}