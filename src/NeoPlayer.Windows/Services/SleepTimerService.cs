namespace NeoPlayer.Windows.Services;

public sealed class SleepTimerService : IDisposable
{
    private CancellationTokenSource? _cts;
    public DateTimeOffset? EndsAt { get; private set; }
    public event EventHandler? Changed;
    public event EventHandler? Elapsed;

    public void Start(TimeSpan duration)
    {
        Cancel();
        if (duration <= TimeSpan.Zero) return;
        _cts = new CancellationTokenSource();
        EndsAt = DateTimeOffset.Now.Add(duration);
        Changed?.Invoke(this, EventArgs.Empty);
        _ = RunAsync(duration, _cts.Token);
    }

    private async Task RunAsync(TimeSpan duration, CancellationToken token)
    {
        try
        {
            await Task.Delay(duration, token);
            if (token.IsCancellationRequested) return;
            EndsAt = null;
            Elapsed?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        if (EndsAt is not null)
        {
            EndsAt = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => Cancel();
}
