using NeoPlayer.Windows.Models;
using System.Diagnostics;
namespace NeoPlayer.Windows.Services;
public sealed class FileOperationsService
{
 public void Reveal(Song s)=>Process.Start(new ProcessStartInfo("explorer.exe",$"/select,\"{s.Path}\""){UseShellExecute=true});
 public void Share(Song s){try{Process.Start(new ProcessStartInfo(s.Path){UseShellExecute=true,Verb="share"});}catch{Reveal(s);}}
 public void Recycle(Song s){Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(s.Path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);}
}