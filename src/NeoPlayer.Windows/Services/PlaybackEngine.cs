using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.Services;

public sealed class PlaybackEngine : IDisposable
{
    private readonly NeoDatabase _db;
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Song> _queue = new();
    private readonly System.Timers.Timer _timer;
    private IWavePlayer? _output;
    private MixingSampleProvider? _mixer;
    private Deck? _current;
    private Deck? _next;
    private int _index = -1;
    private bool _crossfading;
    private bool _disposed;

    public event EventHandler? StateChanged;
    public event EventHandler<Song?>? TrackChanged;
    public event EventHandler<TimeSpan>? PositionChanged;

    public IReadOnlyList<Song> Queue => _queue;
    public Song? CurrentSong => _index >= 0 && _index < _queue.Count ? _queue[_index] : null;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public double Volume { get; private set; }
    public bool Shuffle { get; set; }
    public RepeatMode Repeat { get; set; }
    public TimeSpan Position => _current?.Reader.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _current?.Reader.TotalTime ?? TimeSpan.Zero;

    public PlaybackEngine(NeoDatabase db, SettingsService settings)
    {
        _db = db; _settings = settings;
        Volume = settings.Value.Volume; Shuffle = settings.Value.Shuffle; Repeat = settings.Value.Repeat;
        _timer = new System.Timers.Timer(200); _timer.Elapsed += (_, _) => Tick(); _timer.AutoReset = true; _timer.Start();
    }

    public async Task SetQueueAndPlayAsync(IEnumerable<Song> songs, int startIndex = 0)
    {
        await _gate.WaitAsync();
        try
        {
            StopDecks(); _queue.Clear(); _queue.AddRange(songs); _index = _queue.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _queue.Count - 1);
            if (_index >= 0) StartCurrent();
        }
        finally { _gate.Release(); }
    }

    public async Task PlaySongAsync(Song song, IEnumerable<Song>? context = null)
    {
        var queue = context?.ToList() ?? new List<Song> { song };
        var index = Math.Max(0, queue.FindIndex(x => x.Id == song.Id));
        await SetQueueAndPlayAsync(queue, index);
    }

    public void Pause() { _output?.Pause(); StateChanged?.Invoke(this, EventArgs.Empty); }

    public void PlayPause()
    {
        if (_output is null && CurrentSong is not null) StartCurrent();
        else if (_output?.PlaybackState == PlaybackState.Playing) _output.Pause();
        else _output?.Play();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task NextAsync() => await ChangeTrackAsync(GetNextIndex());
    public async Task PreviousAsync() => await ChangeTrackAsync(_queue.Count == 0 ? -1 : Math.Max(0, _index - 1));

    public void Seek(TimeSpan position)
    {
        if (_current is null) return;
        _current.Reader.CurrentTime = position < TimeSpan.Zero ? TimeSpan.Zero : position > _current.Reader.TotalTime ? _current.Reader.TotalTime : position;
        PositionChanged?.Invoke(this, _current.Reader.CurrentTime);
    }

    public void SetVolume(double value)
    {
        Volume = Math.Clamp(value, 0, 1); if (_current is not null) _current.Volume.Volume = (float)Volume; if (_next is not null && !_crossfading) _next.Volume.Volume = 0;
        _settings.Value.Volume = Volume; _ = _settings.SaveAsync(); StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetEqBand(int band, float db) { _current?.Equalizer.SetBand(band, db); _next?.Equalizer.SetBand(band, db); }
    public void SetBass(float db) { _current?.Equalizer.SetBass(db); _next?.Equalizer.SetBass(db); }
    public void SetStereoWidth(float width) { _current?.Equalizer.SetStereoWidth(width); _next?.Equalizer.SetStereoWidth(width); }

    private void EnsureOutput()
    {
        if (_output is not null) return;
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)) { ReadFully = true };
        try { _output = new WasapiOut(AudioClientShareMode.Shared, true, 100); }
        catch { _output = new WaveOutEvent { DesiredLatency = 120 }; }
        _output.Init(_mixer);
    }

    private void StartCurrent()
    {
        var song = CurrentSong; if (song is null || !File.Exists(song.Path)) return;
        EnsureOutput(); StopDecks();
        _current = CreateDeck(song.Path); _current.Volume.Volume = (float)Volume; _mixer!.AddMixerInput(_current.Volume); _output!.Play();
        _ = _db.RecordPlayedAsync(song.Id); TrackChanged?.Invoke(this, song); StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private Deck CreateDeck(string path)
    {
        var reader = new MediaFoundationReader(path);
        ISampleProvider source = reader.ToSampleProvider();
        if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
        else if (source.WaveFormat.Channels != 2) throw new InvalidDataException($"Unsupported channel count: {source.WaveFormat.Channels}");
        if (source.WaveFormat.SampleRate != 44100) source = new WdlResamplingSampleProvider(source, 44100);
        var eq = new EqualizerSampleProvider(source); var vol = new VolumeSampleProvider(eq) { Volume = 0 };
        return new Deck(reader, eq, vol);
    }

    private async Task ChangeTrackAsync(int nextIndex)
    {
        await _gate.WaitAsync();
        try
        {
            if (nextIndex < 0 || nextIndex >= _queue.Count) { _output?.Stop(); StopDecks(); _index = -1; TrackChanged?.Invoke(this, null); return; }
            _index = nextIndex; StartCurrent();
        }
        finally { _gate.Release(); }
    }

    private int GetNextIndex()
    {
        if (_queue.Count == 0) return -1;
        if (Repeat == RepeatMode.One) return _index;
        if (Shuffle && _queue.Count > 1)
        {
            var r = Random.Shared.Next(_queue.Count - 1); return r >= _index ? r + 1 : r;
        }
        if (_index + 1 < _queue.Count) return _index + 1;
        return Repeat == RepeatMode.All ? 0 : -1;
    }

    private void Tick()
    {
        var deck = _current; if (deck is null) return;
        PositionChanged?.Invoke(this, deck.Reader.CurrentTime);
        if (!IsPlaying) return;
        var remaining = deck.Reader.TotalTime - deck.Reader.CurrentTime;
        var cf = TimeSpan.FromSeconds(_settings.Value.CrossfadeEnabled ? _settings.Value.CrossfadeSeconds : 0);
        if (!_crossfading && cf > TimeSpan.Zero && remaining <= cf && remaining > TimeSpan.Zero)
        {
            var ni = GetNextIndex(); if (ni >= 0 && ni != _index) BeginCrossfade(ni, cf);
        }
        else if (!_crossfading && remaining <= TimeSpan.FromMilliseconds(120)) _ = NextAsync();
    }

    private void BeginCrossfade(int nextIndex, TimeSpan duration)
    {
        try
        {
            var song = _queue[nextIndex]; if (!File.Exists(song.Path)) return;
            _next = CreateDeck(song.Path); _mixer!.AddMixerInput(_next.Volume); _crossfading = true;
            var started = DateTime.UtcNow;
            _ = Task.Run(async () =>
            {
                while (!_disposed)
                {
                    var t = Math.Clamp((DateTime.UtcNow - started).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
                    if (_current is not null) _current.Volume.Volume = (float)(Volume * (1 - t)); if (_next is not null) _next.Volume.Volume = (float)(Volume * t);
                    if (t >= 1) break; await Task.Delay(25);
                }
                await _gate.WaitAsync();
                try
                {
                    if (_next is null) return;
                    if (_current is not null) { _mixer?.RemoveMixerInput(_current.Volume); _current.Dispose(); }
                    _current = _next; _next = null; _index = nextIndex; _crossfading = false;
                    _ = _db.RecordPlayedAsync(song.Id); TrackChanged?.Invoke(this, song); StateChanged?.Invoke(this, EventArgs.Empty);
                }
                finally { _gate.Release(); }
            });
        }
        catch { _next?.Dispose(); _next = null; _crossfading = false; }
    }

    private void StopDecks()
    {
        if (_current is not null) { _mixer?.RemoveMixerInput(_current.Volume); _current.Dispose(); _current = null; }
        if (_next is not null) { _mixer?.RemoveMixerInput(_next.Volume); _next.Dispose(); _next = null; }
        _crossfading = false;
    }

    public void Dispose()
    {
        _disposed = true; _timer.Stop(); _timer.Dispose(); StopDecks(); _output?.Stop(); _output?.Dispose(); _gate.Dispose();
    }

    private sealed class Deck(MediaFoundationReader reader, EqualizerSampleProvider equalizer, VolumeSampleProvider volume) : IDisposable
    {
        public MediaFoundationReader Reader { get; } = reader;
        public EqualizerSampleProvider Equalizer { get; } = equalizer;
        public VolumeSampleProvider Volume { get; } = volume;
        public void Dispose() => Reader.Dispose();
    }
}
