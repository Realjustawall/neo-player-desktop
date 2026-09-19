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
    private bool _resumePending;
    private DateTime _lastPositionPersist = DateTime.MinValue;

    public event EventHandler? StateChanged;
    public event EventHandler<Song?>? TrackChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? QueueChanged;

    public IReadOnlyList<Song> Queue => _queue;
    public int CurrentIndex => _index;
    public Song? CurrentSong => _index >= 0 && _index < _queue.Count ? _queue[_index] : null;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public double Volume { get; private set; }
    public bool Shuffle { get; private set; }
    public RepeatMode Repeat { get; private set; }
    public TimeSpan Position => _current?.Reader.CurrentTime ?? TimeSpan.FromSeconds(_settings.Value.PersistedPositionSeconds);
    public TimeSpan Duration => _current?.Reader.TotalTime ?? TimeSpan.Zero;

    public PlaybackEngine(NeoDatabase db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
        Volume = settings.Value.Volume;
        Shuffle = settings.Value.Shuffle;
        Repeat = settings.Value.Repeat;
        _timer = new System.Timers.Timer(200);
        _timer.Elapsed += (_, _) => Tick();
        _timer.AutoReset = true;
        _timer.Start();
    }

    public void RestoreQueue(IEnumerable<Song> songs, int index, double positionSeconds)
    {
        StopDecks();
        _queue.Clear();
        _queue.AddRange(songs);
        _index = _queue.Count == 0 ? -1 : Math.Clamp(index, 0, _queue.Count - 1);
        _settings.Value.PersistedPositionSeconds = Math.Max(0, positionSeconds);
        _resumePending = _index >= 0 && positionSeconds > 0;
        QueueChanged?.Invoke(this, EventArgs.Empty);
        TrackChanged?.Invoke(this, CurrentSong);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetQueueAndPlayAsync(IEnumerable<Song> songs, int startIndex = 0)
    {
        await _gate.WaitAsync();
        try
        {
            StopDecks();
            _queue.Clear();
            _queue.AddRange(songs);
            _index = _queue.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _queue.Count - 1);
            _resumePending = false;
            _settings.Value.PersistedPositionSeconds = 0;
            await PersistQueueAsync();
            QueueChanged?.Invoke(this, EventArgs.Empty);
            if (_index >= 0) StartCurrent();
            else TrackChanged?.Invoke(this, null);
        }
        finally { _gate.Release(); }
    }

    public async Task PlaySongAsync(Song song, IEnumerable<Song>? context = null)
    {
        var queue = context?.ToList() ?? new List<Song> { song };
        var index = Math.Max(0, queue.FindIndex(x => x.Id == song.Id));
        await SetQueueAndPlayAsync(queue, index);
    }

    public void Pause()
    {
        _output?.Pause();
        _ = PersistPlaybackStateAsync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PlayPause()
    {
        if (_output is null && CurrentSong is not null) StartCurrent();
        else if (_output?.PlaybackState == PlaybackState.Playing) _output.Pause();
        else _output?.Play();
        _ = PersistPlaybackStateAsync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task NextAsync() => await ChangeTrackAsync(GetNextIndex());
    public async Task PreviousAsync() => await ChangeTrackAsync(_queue.Count == 0 ? -1 : Math.Max(0, _index - 1));

    public async Task MoveQueueItemAsync(int fromIndex, int toIndex)
    {
        await _gate.WaitAsync();
        try
        {
            if (fromIndex < 0 || fromIndex >= _queue.Count || _queue.Count < 2) return;
            var target = Math.Clamp(toIndex, 0, _queue.Count - 1);
            if (target == fromIndex) return;
            var currentSongId = CurrentSong?.Id;
            var item = _queue[fromIndex];
            _queue.RemoveAt(fromIndex);
            _queue.Insert(target, item);
            _index = currentSongId is null ? -1 : _queue.FindIndex(x => x.Id == currentSongId.Value);
            await PersistQueueAsync();
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveQueueItemAsync(Song song)
    {
        await _gate.WaitAsync();
        try
        {
            var removeIndex = _queue.FindIndex(x => x.Id == song.Id);
            if (removeIndex < 0) return;
            var removingCurrent = removeIndex == _index;
            _queue.RemoveAt(removeIndex);
            if (_queue.Count == 0)
            {
                _output?.Stop();
                StopDecks();
                _index = -1;
                _settings.Value.PersistedPositionSeconds = 0;
                TrackChanged?.Invoke(this, null);
            }
            else if (removingCurrent)
            {
                _index = Math.Min(removeIndex, _queue.Count - 1);
                _settings.Value.PersistedPositionSeconds = 0;
                StartCurrent();
            }
            else if (removeIndex < _index) _index--;
            await PersistQueueAsync();
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _gate.Release(); }
    }

    public async Task ClearQueueAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _output?.Stop();
            StopDecks();
            _queue.Clear();
            _index = -1;
            _settings.Value.PersistedPositionSeconds = 0;
            await PersistQueueAsync();
            QueueChanged?.Invoke(this, EventArgs.Empty);
            TrackChanged?.Invoke(this, null);
        }
        finally { _gate.Release(); }
    }

    public void Seek(TimeSpan position)
    {
        if (_current is null) return;
        _current.Reader.CurrentTime = position < TimeSpan.Zero ? TimeSpan.Zero : position > _current.Reader.TotalTime ? _current.Reader.TotalTime : position;
        _settings.Value.PersistedPositionSeconds = _current.Reader.CurrentTime.TotalSeconds;
        PositionChanged?.Invoke(this, _current.Reader.CurrentTime);
        _ = PersistPlaybackStateAsync();
    }

    public void SetVolume(double value)
    {
        Volume = Math.Clamp(value, 0, 1);
        if (_current is not null) _current.Volume.Volume = (float)Volume;
        if (_next is not null && !_crossfading) _next.Volume.Volume = 0;
        _settings.Value.Volume = Volume;
        _ = _settings.SaveAsync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetShuffle(bool value)
    {
        Shuffle = value;
        _settings.Value.Shuffle = value;
        _ = _settings.SaveAsync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetRepeat(RepeatMode value)
    {
        Repeat = value;
        _settings.Value.Repeat = value;
        _ = _settings.SaveAsync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetEqBand(int band, float db)
    {
        if (band < 0 || band >= 10) return;
        db = Math.Clamp(db, -12f, 12f);
        while (_settings.Value.EqualizerBands.Count < 10) _settings.Value.EqualizerBands.Add(0);
        _settings.Value.EqualizerBands[band] = db;
        _current?.Equalizer.SetBand(band, db);
        _next?.Equalizer.SetBand(band, db);
        _ = _settings.SaveAsync();
    }

    public void SetBass(float db)
    {
        db = Math.Clamp(db, -12f, 12f);
        _settings.Value.BassDb = db;
        _current?.Equalizer.SetBass(db);
        _next?.Equalizer.SetBass(db);
        _ = _settings.SaveAsync();
    }

    public void SetStereoWidth(float width)
    {
        width = Math.Clamp(width, 0f, 2f);
        _settings.Value.StereoWidth = width;
        _current?.Equalizer.SetStereoWidth(width);
        _next?.Equalizer.SetStereoWidth(width);
        _ = _settings.SaveAsync();
    }

    public async Task PersistPlaybackStateAsync()
    {
        _settings.Value.PersistedQueuePaths = _queue.Select(x => x.Path).ToList();
        _settings.Value.PersistedQueueIndex = _index;
        _settings.Value.PersistedPositionSeconds = _current?.Reader.CurrentTime.TotalSeconds ?? _settings.Value.PersistedPositionSeconds;
        await _settings.SaveAsync();
    }

    private async Task PersistQueueAsync()
    {
        _settings.Value.PersistedQueuePaths = _queue.Select(x => x.Path).ToList();
        _settings.Value.PersistedQueueIndex = _index;
        await _settings.SaveAsync();
    }

    private void EnsureOutput()
    {
        if (_output is not null && _mixer is not null) return;
        var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)) { ReadFully = true };
        IWavePlayer output;
        try { output = new WasapiOut(AudioClientShareMode.Shared, true, 100); }
        catch { output = new WaveOutEvent { DesiredLatency = 120 }; }
        output.Init(mixer);
        _mixer = mixer;
        _output = output;
    }

    private void StartCurrent()
    {
        var song = CurrentSong;
        if (song is null || !File.Exists(song.Path)) return;
        EnsureOutput();
        StopDecks();
        var mixer = _mixer ?? throw new InvalidOperationException("Audio mixer was not initialized.");
        var output = _output ?? throw new InvalidOperationException("Audio output was not initialized.");
        _current = CreateDeck(song.Path);
        if (_resumePending && _settings.Value.PersistedPositionSeconds > 0)
        {
            var resume = TimeSpan.FromSeconds(_settings.Value.PersistedPositionSeconds);
            _current.Reader.CurrentTime = resume < _current.Reader.TotalTime ? resume : TimeSpan.Zero;
            _resumePending = false;
        }
        _current.Volume.Volume = (float)Volume;
        mixer.AddMixerInput(_current.Volume);
        output.Play();
        _ = _db.RecordPlayedAsync(song.Id);
        _ = PersistQueueAsync();
        TrackChanged?.Invoke(this, song);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private Deck CreateDeck(string path)
    {
        var reader = new MediaFoundationReader(path);
        ISampleProvider source = reader.ToSampleProvider();
        if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
        else if (source.WaveFormat.Channels != 2) throw new InvalidDataException($"Unsupported channel count: {source.WaveFormat.Channels}");
        if (source.WaveFormat.SampleRate != 44100) source = new WdlResamplingSampleProvider(source, 44100);
        var eq = new EqualizerSampleProvider(source);
        for (var i = 0; i < Math.Min(10, _settings.Value.EqualizerBands.Count); i++) eq.SetBand(i, _settings.Value.EqualizerBands[i]);
        eq.SetBass(_settings.Value.BassDb);
        eq.SetStereoWidth(_settings.Value.StereoWidth);
        var vol = new VolumeSampleProvider(eq) { Volume = 0 };
        return new Deck(reader, eq, vol);
    }

    private async Task ChangeTrackAsync(int nextIndex)
    {
        await _gate.WaitAsync();
        try
        {
            if (nextIndex < 0 || nextIndex >= _queue.Count)
            {
                _output?.Stop();
                StopDecks();
                _index = -1;
                _settings.Value.PersistedPositionSeconds = 0;
                await PersistQueueAsync();
                TrackChanged?.Invoke(this, null);
                return;
            }
            _index = nextIndex;
            _settings.Value.PersistedPositionSeconds = 0;
            _resumePending = false;
            StartCurrent();
            await PersistQueueAsync();
        }
        finally { _gate.Release(); }
    }

    private int GetNextIndex()
    {
        if (_queue.Count == 0) return -1;
        if (Repeat == RepeatMode.One) return _index;
        if (Shuffle && _queue.Count > 1)
        {
            var r = Random.Shared.Next(_queue.Count - 1);
            return r >= _index ? r + 1 : r;
        }
        if (_index + 1 < _queue.Count) return _index + 1;
        return Repeat == RepeatMode.All ? 0 : -1;
    }

    private void Tick()
    {
        var deck = _current;
        if (deck is null) return;
        PositionChanged?.Invoke(this, deck.Reader.CurrentTime);
        if ((DateTime.UtcNow - _lastPositionPersist).TotalSeconds >= 5)
        {
            _lastPositionPersist = DateTime.UtcNow;
            _settings.Value.PersistedPositionSeconds = deck.Reader.CurrentTime.TotalSeconds;
        }
        if (!IsPlaying) return;
        var remaining = deck.Reader.TotalTime - deck.Reader.CurrentTime;
        var cf = TimeSpan.FromSeconds(_settings.Value.CrossfadeEnabled ? _settings.Value.CrossfadeSeconds : 0);
        if (!_crossfading && cf > TimeSpan.Zero && remaining <= cf && remaining > TimeSpan.Zero)
        {
            var ni = GetNextIndex();
            if (ni >= 0 && ni != _index) BeginCrossfade(ni, cf);
        }
        else if (!_crossfading && remaining <= TimeSpan.FromMilliseconds(120)) _ = NextAsync();
    }

    private void BeginCrossfade(int nextIndex, TimeSpan duration)
    {
        try
        {
            var song = _queue[nextIndex];
            if (!File.Exists(song.Path)) return;
            var mixer = _mixer;
            if (mixer is null) return;
            _next = CreateDeck(song.Path);
            mixer.AddMixerInput(_next.Volume);
            _crossfading = true;
            var started = DateTime.UtcNow;
            _ = Task.Run(async () =>
            {
                while (!_disposed)
                {
                    var t = Math.Clamp((DateTime.UtcNow - started).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
                    if (_current is not null) _current.Volume.Volume = (float)(Volume * (1 - t));
                    if (_next is not null) _next.Volume.Volume = (float)(Volume * t);
                    if (t >= 1) break;
                    await Task.Delay(25);
                }
                await _gate.WaitAsync();
                try
                {
                    if (_next is null) return;
                    if (_current is not null)
                    {
                        _mixer?.RemoveMixerInput(_current.Volume);
                        _current.Dispose();
                    }
                    _current = _next;
                    _next = null;
                    _index = nextIndex;
                    _crossfading = false;
                    _settings.Value.PersistedPositionSeconds = 0;
                    await PersistQueueAsync();
                    _ = _db.RecordPlayedAsync(song.Id);
                    TrackChanged?.Invoke(this, song);
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
                finally { _gate.Release(); }
            });
        }
        catch
        {
            _next?.Dispose();
            _next = null;
            _crossfading = false;
        }
    }

    private void StopDecks()
    {
        if (_current is not null)
        {
            _mixer?.RemoveMixerInput(_current.Volume);
            _current.Dispose();
            _current = null;
        }
        if (_next is not null)
        {
            _mixer?.RemoveMixerInput(_next.Volume);
            _next.Dispose();
            _next = null;
        }
        _crossfading = false;
    }

    public void Dispose()
    {
        _disposed = true;
        _settings.Value.PersistedQueuePaths = _queue.Select(x => x.Path).ToList();
        _settings.Value.PersistedQueueIndex = _index;
        _settings.Value.PersistedPositionSeconds = _current?.Reader.CurrentTime.TotalSeconds ?? _settings.Value.PersistedPositionSeconds;
        _ = _settings.SaveAsync();
        _timer.Stop();
        _timer.Dispose();
        StopDecks();
        _output?.Stop();
        _output?.Dispose();
        _gate.Dispose();
    }

    private sealed class Deck(MediaFoundationReader reader, EqualizerSampleProvider equalizer, VolumeSampleProvider volume) : IDisposable
    {
        public MediaFoundationReader Reader { get; } = reader;
        public EqualizerSampleProvider Equalizer { get; } = equalizer;
        public VolumeSampleProvider Volume { get; } = volume;
        public void Dispose() => Reader.Dispose();
    }
}
