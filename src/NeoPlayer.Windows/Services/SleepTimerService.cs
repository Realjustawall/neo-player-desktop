namespace NeoPlayer.Windows.Services;
public sealed class SleepTimerService(PlaybackEngine playback) : BindableBase, IDisposable
{
 CancellationTokenSource? cts; DateTimeOffset? end; public DateTimeOffset? EndAt=>end; public bool Active=>cts is not null;
 public void Cancel(){cts?.Cancel();cts?.Dispose();cts=null;end=null;Raise(nameof(Active));Raise(nameof(EndAt));}
 public void Start(TimeSpan duration,bool fade=true){Cancel();cts=new();end=DateTimeOffset.Now+duration;Raise(nameof(Active));Raise(nameof(EndAt));var token=cts.Token;_=Task.Run(async()=>{try{var fadeDur=fade?TimeSpan.FromSeconds(Math.Min(30,duration.TotalSeconds/4)):TimeSpan.Zero;var normal=playback.State.Volume;var delay=duration-fadeDur;if(delay>TimeSpan.Zero)await Task.Delay(delay,token);if(fadeDur>TimeSpan.Zero){var sw=System.Diagnostics.Stopwatch.StartNew();while(sw.Elapsed<fadeDur){token.ThrowIfCancellationRequested();playback.SetVolume(normal*(1-sw.Elapsed.TotalMilliseconds/fadeDur.TotalMilliseconds));await Task.Delay(100,token);}}playback.Pause();playback.SetVolume(normal);Cancel();}catch(OperationCanceledException){}});}
 public void Dispose()=>Cancel();
}