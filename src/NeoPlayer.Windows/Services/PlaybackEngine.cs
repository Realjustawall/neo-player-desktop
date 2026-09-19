using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NeoPlayer.Windows.Audio;
using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;
using System.Collections.Specialized;

namespace NeoPlayer.Windows.Services;

public sealed class PlaybackEngine : IAsyncDisposable
{
    readonly NeoDatabase db;
    readonly SettingsService settings;
    readonly AudioAnalysisService analysis;
    readonly AutoMixPlanner planner;
    readonly MixingSampleProvider mixer;
    IWavePlayer? output;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly System.Threading.Timer timer;
    Deck? current, next;
    int preparedNextIndex = -1;
    TransitionPlan? preparedPlan;
    long accumulatedListened;
    bool transitioning;
    CancellationTokenSource? transitionCts;
    readonly Random random = new();

    public PlaybackState State { get; } = new();
    public event EventHandler? StateChanged;
    public event Action<float[]>? Samples;

    public PlaybackEngine(NeoDatabase d, SettingsService s, AudioAnalysisService a)
    {
        db = d; settings = s; analysis = a; planner = new(d);
        mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { ReadFully = true };
        timer = new(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        State.Queue.CollectionChanged += QueueChanged;
    }

    public async Task InitializeAsync()
    {
        EnsureOutput();
        if (settings.Current.RememberQueue)
        {
            var ids = await db.LoadQueueAsync();
            foreach (var id in ids) { var s = await db.GetSongAsync(id); if (s is not null) State.Queue.Add(s); }
            var idx = await db.GetAppStateAsync("queueIndex");
            if (int.TryParse(idx, out var i)) State.QueueIndex = Math.Clamp(i, -1, State.Queue.Count - 1);
        }
        State.Speed = settings.Current.DefaultSpeed;
        if (settings.Current.ResumeLastSong && State.QueueIndex >= 0 && State.QueueIndex < State.Queue.Count)
        {
            var pos = await db.GetAppStateAsync("positionMs");
            if (long.TryParse(pos, out var ms))
            {
                await gate.WaitAsync();
                try { await StartCurrentLockedAsync(State.Queue[State.QueueIndex], ms / 1000d, false); }
                finally { gate.Release(); }
            }
        }
    }

    public async Task PlayQueueAsync(IEnumerable<Song> songs, int index = 0)
    {
        await gate.WaitAsync();
        try
        {
            ClearDecks(); State.Queue.Clear();
            foreach (var s in songs) State.Queue.Add(s);
            if (State.Queue.Count == 0) return;
            State.QueueIndex = Math.Clamp(index, 0, State.Queue.Count - 1);
            await StartCurrentLockedAsync(State.Queue[State.QueueIndex], 0, true);
            await PersistAsync();
        }
        finally { gate.Release(); }
    }
    public Task PlayAsync(Song song) => PlayQueueAsync([song]);

    async Task StartCurrentLockedAsync(Song song, double seekSeconds, bool startOutput)
    {
        ClearDecks();
        current = await CreateDeckAsync(song, seekSeconds, State.Speed);
        current.Enabled = true; current.Volume.Volume = 1;
        mixer.AddMixerInput(current.Gate);
        State.Current = song; State.DurationMs = song.DurationMs; State.PositionMs = (long)(seekSeconds * 1000); State.Error = null;
        EnsureOutput();
        if (startOutput) { output!.Play(); State.IsPlaying = true; }
        else State.IsPlaying = false;
        timer.Change(120, 120);
        await PrepareNextLockedAsync();
        Raise();
    }

    async Task<Deck> CreateDeckAsync(Song song, double start, float speed)
    {
        var d = new Deck(song.Id, song.Path, start, speed);
        d.Dsp.Samples += OnSamples;
        var eq = await db.GetEqAsync(song.Id) ?? new EqProfile(song.Id, "Normal", 0, 0, 0, new float[10]);
        var ng = await NormalizationGainAsync(song);
        d.Dsp.Configure(eq.Bands, eq.Bass, eq.Virtualizer, eq.LoudnessDb, ng);
        return d;
    }

    async Task<float> NormalizationGainAsync(Song s)
    {
        if (!settings.Current.LoudnessNormalization) return 0;
        var mode = settings.Current.NormalizationMode;
        if (mode is "smart" or "replaygain")
        {
            var rg = await db.GetReplayGainAsync(s.Id);
            if (rg?.PreferredGainDb is float g) return Math.Clamp(g, -18, 12);
            if (mode == "replaygain") return 0;
        }
        var a = await db.GetAnalysisAsync(s.Id);
        if (a is null && mode == "smart")
        {
            try { a = (await analysis.AnalyzeAsync(s)).Basic; } catch { }
        }
        if (a is null) return 0;
        return Math.Clamp(settings.Current.NormalizationTargetLufs - a.IntegratedLufs, -18, 12);
    }

    async Task PrepareNextLockedAsync()
    {
        if (next is not null) { mixer.RemoveMixerInput(next.Gate); next.Dispose(); }
        next = null; preparedNextIndex = -1; preparedPlan = null;
        if (State.Queue.Count == 0 || State.QueueIndex < 0) return;
        var ni = NextIndex(false);
        if (ni < 0 || ni >= State.Queue.Count) return;
        if (ni == State.QueueIndex && State.Repeat != RepeatMode.One) return;
        preparedNextIndex = ni;
        next = await CreateDeckAsync(State.Queue[ni], 0, State.Speed);
        next.Enabled = false; next.Volume.Volume = 0;
        mixer.AddMixerInput(next.Gate);
        if (settings.Current.AutomixEnabled && settings.Current.AdvancedAutomixEnabled && State.Current is not null)
        {
            try { preparedPlan = await planner.PlanAsync(State.Current, State.Queue[ni], settings.Current.CrossfadeMs); } catch { preparedPlan = null; }
        }
    }

    int NextIndex(bool userInitiated)
    {
        if (State.Queue.Count == 0 || State.QueueIndex < 0) return -1;
        if (!userInitiated && State.Repeat == RepeatMode.One) return State.QueueIndex;
        if (State.Shuffle && State.Queue.Count > 1)
        {
            int i; do i = random.Next(State.Queue.Count); while (i == State.QueueIndex); return i;
        }
        var n = State.QueueIndex + 1;
        if (n < State.Queue.Count) return n;
        if (State.Repeat == RepeatMode.All) return 0;
        return -1;
    }

    void Tick()
    {
        if (current is null || State.Current is null) return;
        State.PositionMs = Math.Min(State.DurationMs, (long)(current.Decoder.PositionSeconds * 1000));
        if (State.IsPlaying) accumulatedListened += 120;
        _ = PersistPositionOccasionallyAsync();
        Raise();
        if (!State.IsPlaying) return;
        var remaining = State.DurationMs - State.PositionMs;
        if (!transitioning && next is not null && preparedNextIndex >= 0)
        {
            long threshold = settings.Current.AutomixEnabled
                ? Math.Max(1200, settings.Current.CrossfadeMs == 0 ? 6000 : settings.Current.CrossfadeMs)
                : settings.Current.CrossfadeMs;
            var atPlannedPoint = preparedPlan is not null && State.PositionMs >= preparedPlan.StartAtMs;
            if (atPlannedPoint || (preparedPlan is null && threshold > 0 && remaining <= threshold) || (settings.Current.GaplessEnabled && threshold == 0 && remaining < 220)) _ = BeginTransitionAsync();
        }
        if (current.Decoder.Ended && !transitioning) _ = HandleEndedAsync();
    }

    long lastPersistTick;
    async Task PersistPositionOccasionallyAsync()
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref lastPersistTick) < 2000) return;
        Interlocked.Exchange(ref lastPersistTick, now);
        if (settings.Current.ResumeLastSong) await db.SetAppStateAsync("positionMs", State.PositionMs.ToString());
    }

    async Task HandleEndedAsync()
    {
        if (State.Repeat == RepeatMode.One) { await SeekAsync(0); Resume(); return; }
        var ni = NextIndex(false);
        if (ni >= 0) await NextAsync(false);
        else
        {
            Pause(); State.PositionMs = State.DurationMs;
            if (State.Current is not null) await db.RecordListenAsync(State.Current.Id, accumulatedListened, true, false);
            accumulatedListened = 0; Raise();
        }
    }

    async Task BeginTransitionAsync()
    {
        if (transitioning || current is null || next is null || State.Current is null || preparedNextIndex < 0) return;
        transitioning = true; transitionCts?.Cancel(); transitionCts = new(); var token = transitionCts.Token;
        var targetIndex = preparedNextIndex; var targetSong = State.Queue[targetIndex];
        try
        {
            var p = settings.Current.AutomixEnabled && settings.Current.AdvancedAutomixEnabled
                ? preparedPlan ?? await planner.PlanAsync(State.Current, targetSong, settings.Current.CrossfadeMs, token)
                : new TransitionPlan(settings.Current.CrossfadeMs, Math.Max(0, State.DurationMs - settings.Current.CrossfadeMs), 1, 0, "crossfade");
            var targetSpeed = Math.Clamp(State.Speed * p.NextSpeed, .25f, 3f);
            if (p.NextSeekMs > 0 || Math.Abs(targetSpeed - State.Speed) > .001)
            {
                mixer.RemoveMixerInput(next.Gate); next.Dispose();
                next = await CreateDeckAsync(targetSong, p.NextSeekMs / 1000d, targetSpeed);
                next.Volume.Volume = 0; next.Enabled = false; mixer.AddMixerInput(next.Gate);
            }
            next.Enabled = true;
            var dur = Math.Max(1, p.DurationMs);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < dur && !token.IsCancellationRequested)
            {
                var t = Math.Clamp(sw.Elapsed.TotalMilliseconds / dur, 0, 1);
                current.Volume.Volume = (float)Math.Cos(t * Math.PI / 2);
                next.Volume.Volume = (float)Math.Sin(t * Math.PI / 2);
                await Task.Delay(20, token);
            }
            if (!token.IsCancellationRequested) await PromoteNextAsync(targetIndex, targetSpeed);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { State.Error = ex.Message; Raise(); }
        finally { transitioning = false; }
    }

    async Task PromoteNextAsync(int targetIndex, float transitionSpeed)
    {
        await gate.WaitAsync();
        try
        {
            if (current is null || next is null || targetIndex < 0 || targetIndex >= State.Queue.Count) return;
            var old = current; mixer.RemoveMixerInput(old.Gate); old.Dispose();
            current = next; next = null; current.Volume.Volume = 1;
            State.QueueIndex = targetIndex; State.Current = State.Queue[targetIndex]; State.DurationMs = State.Current.DurationMs; State.PositionMs = (long)(current.Decoder.PositionSeconds * 1000);
            await db.RecordListenAsync(old.SongId, accumulatedListened, true, false); accumulatedListened = 0;
            await PrepareNextLockedAsync(); await PersistAsync(); Raise();
            if (Math.Abs(transitionSpeed - State.Speed) > .002f && current is not null) _ = RestoreBaseSpeedAsync(current, transitionSpeed, State.Speed);
        }
        finally { gate.Release(); }
    }

    async Task RestoreBaseSpeedAsync(Deck deck, float from, float to)
    {
        try
        {
            const int steps = 4;
            for (var i = 1; i <= steps; i++)
            {
                await Task.Delay(650);
                await gate.WaitAsync();
                try
                {
                    if (!ReferenceEquals(current, deck) || !State.IsPlaying) return;
                    var speed = from + (to - from) * (i / (float)steps);
                    var pos = deck.Decoder.PositionSeconds;
                    deck.Decoder.Restart(pos, speed);
                }
                finally { gate.Release(); }
            }
        }
        catch { }
    }

    IWavePlayer CreateOutput()
    {
        var provider = new SampleToWaveProvider(mixer);
        try
        {
            var o = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 80);
            o.Init(provider); o.Volume = (float)State.Volume; return o;
        }
        catch
        {
            var fallback = new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3 };
            fallback.Init(provider); fallback.Volume = (float)State.Volume; return fallback;
        }
    }
    void EnsureOutput() => output ??= CreateOutput();

    public void TogglePlay() { if (State.IsPlaying) Pause(); else Resume(); }
    public void Pause() { if (!State.IsPlaying) return; output?.Pause(); State.IsPlaying = false; Raise(); }
    public void Resume() { EnsureOutput(); if (State.Current is null || State.IsPlaying) return; output!.Play(); State.IsPlaying = true; Raise(); }

    public async Task ReinitializeOutputAsync()
    {
        await gate.WaitAsync();
        try
        {
            var was = State.IsPlaying; try { output?.Stop(); output?.Dispose(); } catch { } output = null; EnsureOutput(); if (was) output!.Play();
        }
        finally { gate.Release(); }
    }

    public async Task SeekAsync(long ms)
    {
        await gate.WaitAsync();
        try
        {
            if (State.Current is null) return;
            var playing = State.IsPlaying; await StartCurrentLockedAsync(State.Current, Math.Clamp(ms, 0, State.DurationMs) / 1000d, playing);
            await PersistAsync();
        }
        finally { gate.Release(); }
    }
    public async Task SetSpeedAsync(float speed) { State.Speed = Math.Clamp(speed, .25f, 3f); await SeekAsync(State.PositionMs); }

    public async Task NextAsync(bool user = true)
    {
        await gate.WaitAsync();
        try
        {
            if (State.Queue.Count == 0) return;
            var ni = NextIndex(user); if (ni < 0) return;
            var old = State.Current; var skipped = old is not null && State.PositionMs < Math.Min(30000, State.DurationMs / 2);
            if (old is not null) await db.RecordListenAsync(old.Id, accumulatedListened, false, skipped);
            accumulatedListened = 0; State.QueueIndex = ni; await StartCurrentLockedAsync(State.Queue[ni], 0, true); await PersistAsync();
        }
        finally { gate.Release(); }
    }

    public async Task PreviousAsync()
    {
        if (State.PositionMs > 5000) { await SeekAsync(0); return; }
        await gate.WaitAsync();
        try
        {
            if (State.Queue.Count == 0) return;
            var i = Math.Max(0, State.QueueIndex - 1); State.QueueIndex = i; await StartCurrentLockedAsync(State.Queue[i], 0, true); await PersistAsync();
        }
        finally { gate.Release(); }
    }

    public void ToggleShuffle() { State.Shuffle = !State.Shuffle; _ = PrepareAfterModeChangeAsync(); Raise(); }
    public void CycleRepeat() { State.Repeat = State.Repeat switch { RepeatMode.Off => RepeatMode.All, RepeatMode.All => RepeatMode.One, _ => RepeatMode.Off }; _ = PrepareAfterModeChangeAsync(); Raise(); }
    async Task PrepareAfterModeChangeAsync() { await gate.WaitAsync(); try { if (current is not null) await PrepareNextLockedAsync(); } finally { gate.Release(); } }

    public async Task PlayNextAsync(Song song) { var p = Math.Clamp(State.QueueIndex + 1, 0, State.Queue.Count); var existing = State.Queue.FirstOrDefault(x => x.Id == song.Id); if (existing is not null) State.Queue.Remove(existing); State.Queue.Insert(p, song); await PersistAsync(); await PrepareAfterModeChangeAsync(); }
    public async Task AddToQueueAsync(Song song) { if (!State.Queue.Any(x => x.Id == song.Id)) State.Queue.Add(song); await PersistAsync(); await PrepareAfterModeChangeAsync(); }
    public async Task RemoveFromQueueAsync(Song song) { var i = State.Queue.IndexOf(song); if (i < 0) return; State.Queue.RemoveAt(i); if (i < State.QueueIndex) State.QueueIndex--; await PersistAsync(); await PrepareAfterModeChangeAsync(); }
    public async Task MoveQueueAsync(int from, int to) { if (from < 0 || from >= State.Queue.Count || to < 0 || to >= State.Queue.Count || from == to) return; var s = State.Queue[from]; State.Queue.RemoveAt(from); State.Queue.Insert(to, s); if (State.QueueIndex == from) State.QueueIndex = to; else if (from < State.QueueIndex && to >= State.QueueIndex) State.QueueIndex--; else if (from > State.QueueIndex && to <= State.QueueIndex) State.QueueIndex++; await PersistAsync(); await PrepareAfterModeChangeAsync(); }
    async void QueueChanged(object? s, NotifyCollectionChangedEventArgs e) { if (settings.Current.RememberQueue) await PersistAsync(); }
    async Task PersistAsync() { if (!settings.Current.RememberQueue) return; await db.SaveQueueAsync(State.Queue.Select(x => x.Id)); await db.SetAppStateAsync("queueIndex", State.QueueIndex.ToString()); if (settings.Current.ResumeLastSong) await db.SetAppStateAsync("positionMs", State.PositionMs.ToString()); }

    public async Task ApplyEqAsync(EqProfile e) { await db.SaveEqAsync(e); if (current?.SongId == e.SongId) { var ng = State.Current is null ? 0 : await NormalizationGainAsync(State.Current); current.Dsp.Configure(e.Bands, e.Bass, e.Virtualizer, e.LoudnessDb, ng); } }
    public Task SetTrackVisualAsync(TrackVisualProfile v) => db.SaveTrackVisualAsync(v);
    public void SetVolume(double v) { State.Volume = Math.Clamp(v, 0, 1); EnsureOutput(); output!.Volume = (float)State.Volume; Raise(); }
    void OnSamples(float[] s) => Samples?.Invoke(s);
    void Raise() => StateChanged?.Invoke(this, EventArgs.Empty);

    void ClearDecks()
    {
        transitionCts?.Cancel(); preparedNextIndex = -1; preparedPlan = null;
        if (current is not null) { mixer.RemoveMixerInput(current.Gate); current.Dispose(); }
        if (next is not null) { mixer.RemoveMixerInput(next.Gate); next.Dispose(); }
        current = next = null; transitioning = false;
    }

    public async ValueTask DisposeAsync()
    {
        timer.Dispose(); State.Queue.CollectionChanged -= QueueChanged;
        await gate.WaitAsync();
        try { ClearDecks(); output?.Stop(); output?.Dispose(); output = null; }
        finally { gate.Release(); gate.Dispose(); }
    }
}
