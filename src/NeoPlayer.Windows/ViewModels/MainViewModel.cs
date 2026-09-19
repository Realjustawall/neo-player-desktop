using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using NeoPlayer.Windows.Core;
using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppHost _host;
    private NavigationPage _page = NavigationPage.Home;
    private Song? _currentSong;
    private Playlist? _selectedPlaylist;
    private Category? _selectedCategory;
    private CollectionSummary? _selectedCollection;
    private string _selectedCollectionTitle = "";
    private string _searchText = "";
    private string _status = "Ready";
    private double _positionSeconds;
    private double _durationSeconds;
    private bool _isPlaying;
    private int _scanProgress;
    private string _lyricsPlain = "";
    private string _lyricsLrc = "";
    private string _lyricsTranslation = "";
    private string _lyricsRomanization = "";
    private string _currentLyric = "";
    private IReadOnlyList<LyricLine> _parsedLyrics = Array.Empty<LyricLine>();
    private string _editTitle = "";
    private string _editArtist = "";
    private string _editAlbum = "";
    private string _editGenre = "";
    private string _editArtworkPath = "";
    private string _newPlaylistName = "";
    private string _newCategoryName = "";
    private int _sleepMinutes = 30;

    public ObservableCollection<Song> Songs { get; } = new();
    public ObservableCollection<Song> SearchResults { get; } = new();
    public ObservableCollection<Song> Queue { get; } = new();
    public ObservableCollection<Playlist> Playlists { get; } = new();
    public ObservableCollection<PlaylistFolder> PlaylistFolders { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<Song> SelectedPlaylistSongs { get; } = new();
    public ObservableCollection<Song> SelectedCategorySongs { get; } = new();
    public ObservableCollection<Song> HiddenSongs { get; } = new();
    public ObservableCollection<CollectionSummary> Albums { get; } = new();
    public ObservableCollection<CollectionSummary> Artists { get; } = new();
    public ObservableCollection<CollectionSummary> Genres { get; } = new();
    public ObservableCollection<CollectionSummary> Folders { get; } = new();
    public ObservableCollection<Song> SelectedCollectionSongs { get; } = new();
    public ObservableCollection<string> Sources { get; } = new();
    public ObservableCollection<string> ExcludedSources { get; } = new();
    public ObservableCollection<EqBandItem> EqBands { get; } = new();

    public IReadOnlyList<ThemeMode> ThemeModes { get; } = Enum.GetValues<ThemeMode>();
    public IReadOnlyList<string> Languages { get; } = new[] { "en", "fa" };
    public IReadOnlyList<string> Accents { get; } = new[] { "Orange", "Green", "Red", "Blue", "Purple", "Cyan", "Pink", "Gold" };

    public NavigationPage Page
    {
        get => _page;
        set
        {
            if (!Set(ref _page, value)) return;
            Raise(nameof(IsHome)); Raise(nameof(IsSearch)); Raise(nameof(IsLibrary)); Raise(nameof(IsPlaylists));
            Raise(nameof(IsQueue)); Raise(nameof(IsLyrics)); Raise(nameof(IsSettings));
        }
    }
    public bool IsHome => Page == NavigationPage.Home;
    public bool IsSearch => Page == NavigationPage.Search;
    public bool IsLibrary => Page == NavigationPage.Library;
    public bool IsPlaylists => Page == NavigationPage.Playlists;
    public bool IsQueue => Page == NavigationPage.Queue;
    public bool IsLyrics => Page == NavigationPage.Lyrics;
    public bool IsSettings => Page == NavigationPage.Settings;

    public Song? CurrentSong
    {
        get => _currentSong;
        private set
        {
            if (!Set(ref _currentSong, value)) return;
            Raise(nameof(NowTitle)); Raise(nameof(NowArtist));
            EditTitle = value?.Title ?? "";
            EditArtist = value?.Artist ?? "";
            EditAlbum = value?.Album ?? "";
            EditGenre = value?.Genre ?? "";
            EditArtworkPath = value?.ArtworkPath ?? "";
        }
    }

    public Playlist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set { if (Set(ref _selectedPlaylist, value)) _ = LoadSelectedPlaylistAsync(); }
    }

    public Category? SelectedCategory
    {
        get => _selectedCategory;
        set { if (Set(ref _selectedCategory, value)) _ = LoadSelectedCategoryAsync(); }
    }

    public CollectionSummary? SelectedCollection
    {
        get => _selectedCollection;
        set { if (Set(ref _selectedCollection, value)) _ = LoadSelectedCollectionAsync(); }
    }

    public string SelectedCollectionTitle { get => _selectedCollectionTitle; private set => Set(ref _selectedCollectionTitle, value); }
    public string SearchText { get => _searchText; set { if (Set(ref _searchText, value)) _ = SearchAsync(); } }
    public string Status { get => _status; set => Set(ref _status, value); }
    public double PositionSeconds { get => _positionSeconds; set => Set(ref _positionSeconds, value); }
    public double DurationSeconds { get => _durationSeconds; private set => Set(ref _durationSeconds, value); }
    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }
    public int ScanProgress { get => _scanProgress; private set => Set(ref _scanProgress, value); }
    public double Volume { get => _host.Playback.Volume; set { _host.Playback.SetVolume(value); Raise(); } }
    public string NowTitle => CurrentSong?.Title ?? "NEO Player";
    public string NowArtist => CurrentSong?.Artist ?? "Nothing playing";

    public bool Shuffle { get => _host.Playback.Shuffle; set { _host.Playback.SetShuffle(value); Raise(); } }
    public string RepeatText => _host.Playback.Repeat.ToString();
    public bool CrossfadeEnabled
    {
        get => _host.Settings.Value.CrossfadeEnabled;
        set { if (_host.Settings.Value.CrossfadeEnabled == value) return; _host.Settings.Value.CrossfadeEnabled = value; _ = _host.Settings.SaveAsync(); Raise(); }
    }
    public double CrossfadeSeconds
    {
        get => _host.Settings.Value.CrossfadeSeconds;
        set { var v = Math.Clamp(value, 0, 15); if (Math.Abs(_host.Settings.Value.CrossfadeSeconds - v) < 0.001) return; _host.Settings.Value.CrossfadeSeconds = v; _ = _host.Settings.SaveAsync(); Raise(); }
    }
    public float BassDb
    {
        get => _host.Settings.Value.BassDb;
        set { _host.Playback.SetBass(value); Raise(); }
    }
    public float StereoWidth
    {
        get => _host.Settings.Value.StereoWidth;
        set { _host.Playback.SetStereoWidth(value); Raise(); }
    }
    public bool StartWithWindows
    {
        get => _host.Settings.Value.StartWithWindows;
        set
        {
            if (_host.Settings.Value.StartWithWindows == value) return;
            _host.Settings.Value.StartWithWindows = value;
            try { _host.Windows.SetStartupEnabled(value); Status = value ? "Start with Windows enabled" : "Start with Windows disabled"; }
            catch (Exception ex) { Status = $"Startup setting failed: {ex.Message}"; }
            _ = _host.Settings.SaveAsync(); Raise();
        }
    }
    public ThemeMode SelectedTheme
    {
        get => _host.Settings.Value.Theme;
        set { if (_host.Settings.Value.Theme == value) return; _host.Settings.Value.Theme = value; _host.Theme.Apply(); _ = _host.Settings.SaveAsync(); Raise(); }
    }
    public string Language
    {
        get => _host.Settings.Value.Language;
        set { if (string.Equals(_host.Settings.Value.Language, value, StringComparison.OrdinalIgnoreCase)) return; _host.Localization.Apply(value); _ = _host.Settings.SaveAsync(); Raise(); }
    }
    public string Accent
    {
        get => _host.Settings.Value.Accent;
        set { if (string.Equals(_host.Settings.Value.Accent, value, StringComparison.OrdinalIgnoreCase)) return; _host.Settings.Value.Accent = value; _host.Theme.Apply(); _ = _host.Settings.SaveAsync(); Raise(); }
    }

    public string LyricsPlain { get => _lyricsPlain; set => Set(ref _lyricsPlain, value); }
    public string LyricsLrc { get => _lyricsLrc; set { if (Set(ref _lyricsLrc, value)) _parsedLyrics = LyricsService.ParseLrc(value); } }
    public string LyricsTranslation { get => _lyricsTranslation; set => Set(ref _lyricsTranslation, value); }
    public string LyricsRomanization { get => _lyricsRomanization; set => Set(ref _lyricsRomanization, value); }
    public string CurrentLyric { get => _currentLyric; private set => Set(ref _currentLyric, value); }

    public string EditTitle { get => _editTitle; set => Set(ref _editTitle, value); }
    public string EditArtist { get => _editArtist; set => Set(ref _editArtist, value); }
    public string EditAlbum { get => _editAlbum; set => Set(ref _editAlbum, value); }
    public string EditGenre { get => _editGenre; set => Set(ref _editGenre, value); }
    public string EditArtworkPath { get => _editArtworkPath; set => Set(ref _editArtworkPath, value); }
    public string NewPlaylistName { get => _newPlaylistName; set => Set(ref _newPlaylistName, value); }
    public string NewCategoryName { get => _newCategoryName; set => Set(ref _newCategoryName, value); }
    public int SleepMinutes { get => _sleepMinutes; set => Set(ref _sleepMinutes, Math.Clamp(value, 1, 480)); }
    public string SleepTimerText => _host.SleepTimer.EndsAt is null ? "Off" : $"Until {_host.SleepTimer.EndsAt.Value:t}";

    public ICommand HomeCommand { get; }
    public ICommand SearchPageCommand { get; }
    public ICommand LibraryCommand { get; }
    public ICommand PlaylistsPageCommand { get; }
    public ICommand QueuePageCommand { get; }
    public ICommand LyricsPageCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand PlaySongCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand HideSongCommand { get; }
    public ICommand UnhideSongCommand { get; }
    public ICommand AddSourceCommand { get; }
    public ICommand RemoveSourceCommand { get; }
    public ICommand AddExcludedSourceCommand { get; }
    public ICommand RemoveExcludedSourceCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand CreatePlaylistCommand { get; }
    public ICommand DeletePlaylistCommand { get; }
    public ICommand AddCurrentToPlaylistCommand { get; }
    public ICommand RemoveFromPlaylistCommand { get; }
    public ICommand CreateCategoryCommand { get; }
    public ICommand AddCurrentToCategoryCommand { get; }
    public ICommand SmartRadioCommand { get; }
    public ICommand AnalyzeCommand { get; }
    public ICommand OpenCollectionCommand { get; }
    public ICommand MoveQueueUpCommand { get; }
    public ICommand MoveQueueDownCommand { get; }
    public ICommand RemoveQueueCommand { get; }
    public ICommand ClearQueueCommand { get; }
    public ICommand CycleRepeatCommand { get; }
    public ICommand SaveLyricsCommand { get; }
    public ICommand ImportLyricsCommand { get; }
    public ICommand SaveMetadataCommand { get; }
    public ICommand ChooseArtworkCommand { get; }
    public ICommand OpenContainingFolderCommand { get; }
    public ICommand BackupCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand StartSleepTimerCommand { get; }
    public ICommand CancelSleepTimerCommand { get; }
    public ICommand ResetEqCommand { get; }

    public MainViewModel(AppHost host)
    {
        _host = host;
        HomeCommand = new RelayCommand(() => Page = NavigationPage.Home);
        SearchPageCommand = new RelayCommand(() => Page = NavigationPage.Search);
        LibraryCommand = new RelayCommand(() => Page = NavigationPage.Library);
        PlaylistsPageCommand = new RelayCommand(() => Page = NavigationPage.Playlists);
        QueuePageCommand = new RelayCommand(() => Page = NavigationPage.Queue);
        LyricsPageCommand = new RelayCommand(() => Page = NavigationPage.Lyrics);
        SettingsCommand = new RelayCommand(() => Page = NavigationPage.Settings);
        PlayPauseCommand = new RelayCommand(() => _host.Playback.PlayPause());
        NextCommand = new AsyncCommand(_host.Playback.NextAsync);
        PreviousCommand = new AsyncCommand(_host.Playback.PreviousAsync);
        PlaySongCommand = new RelayCommand<Song>(song => { if (song is not null) _ = _host.Playback.PlaySongAsync(song, Songs); });
        ToggleFavoriteCommand = new RelayCommand<Song>(song => { if (song is not null) _ = ToggleFavoriteAsync(song); });
        HideSongCommand = new RelayCommand<Song>(song => { if (song is not null) _ = HideSongAsync(song); });
        UnhideSongCommand = new RelayCommand<Song>(song => { if (song is not null) _ = UnhideSongAsync(song); });
        AddSourceCommand = new RelayCommand(AddSource);
        RemoveSourceCommand = new RelayCommand<string>(path => { if (!string.IsNullOrWhiteSpace(path)) _ = RemoveSourceAsync(path); });
        AddExcludedSourceCommand = new RelayCommand(AddExcludedSource);
        RemoveExcludedSourceCommand = new RelayCommand<string>(path => { if (!string.IsNullOrWhiteSpace(path)) _ = RemoveExcludedSourceAsync(path); });
        ScanCommand = new AsyncCommand(ScanAsync);
        CreatePlaylistCommand = new AsyncCommand(CreatePlaylistAsync);
        DeletePlaylistCommand = new AsyncCommand(DeleteSelectedPlaylistAsync);
        AddCurrentToPlaylistCommand = new AsyncCommand(AddCurrentToSelectedPlaylistAsync);
        RemoveFromPlaylistCommand = new RelayCommand<Song>(song => { if (song is not null) _ = RemoveFromSelectedPlaylistAsync(song); });
        CreateCategoryCommand = new AsyncCommand(CreateCategoryAsync);
        AddCurrentToCategoryCommand = new AsyncCommand(AddCurrentToSelectedCategoryAsync);
        SmartRadioCommand = new RelayCommand<Song>(song => { if (song is not null) _ = StartRadioAsync(song); });
        AnalyzeCommand = new RelayCommand<Song>(song => { if (song is not null) _ = AnalyzeAsync(song); });
        OpenCollectionCommand = new RelayCommand<CollectionSummary>(collection => { if (collection is not null) SelectedCollection = collection; });
        MoveQueueUpCommand = new RelayCommand<Song>(song => { if (song is not null) _ = MoveQueueAsync(song, -1); });
        MoveQueueDownCommand = new RelayCommand<Song>(song => { if (song is not null) _ = MoveQueueAsync(song, 1); });
        RemoveQueueCommand = new RelayCommand<Song>(song => { if (song is not null) _ = _host.Playback.RemoveQueueItemAsync(song); });
        ClearQueueCommand = new AsyncCommand(_host.Playback.ClearQueueAsync);
        CycleRepeatCommand = new RelayCommand(CycleRepeat);
        SaveLyricsCommand = new AsyncCommand(SaveLyricsAsync);
        ImportLyricsCommand = new RelayCommand(ImportLyrics);
        SaveMetadataCommand = new AsyncCommand(SaveMetadataAsync);
        ChooseArtworkCommand = new RelayCommand(ChooseArtwork);
        OpenContainingFolderCommand = new RelayCommand<Song>(song => { if (song is not null) WindowsIntegrationService.OpenContainingFolder(song.Path); });
        BackupCommand = new RelayCommand(Backup);
        RestoreCommand = new RelayCommand(Restore);
        StartSleepTimerCommand = new RelayCommand(() => { _host.SleepTimer.Start(TimeSpan.FromMinutes(SleepMinutes)); Raise(nameof(SleepTimerText)); });
        CancelSleepTimerCommand = new RelayCommand(() => { _host.SleepTimer.Cancel(); Raise(nameof(SleepTimerText)); });
        ResetEqCommand = new RelayCommand(ResetEq);

        var labels = new[] { "31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };
        for (var i = 0; i < 10; i++)
        {
            var gain = i < host.Settings.Value.EqualizerBands.Count ? host.Settings.Value.EqualizerBands[i] : 0f;
            EqBands.Add(new EqBandItem(i, labels[i], gain, (index, value) => _host.Playback.SetEqBand(index, value)));
        }

        host.Playback.TrackChanged += (_, song) => _ = HandleTrackChangedAsync(song);
        host.Playback.QueueChanged += (_, _) => OnUi(RefreshQueue);
        host.Playback.StateChanged += (_, _) => OnUi(() => { IsPlaying = host.Playback.IsPlaying; Raise(nameof(Shuffle)); Raise(nameof(RepeatText)); });
        host.Playback.PositionChanged += (_, p) => OnUi(() =>
        {
            PositionSeconds = p.TotalSeconds;
            DurationSeconds = _host.Playback.Duration.TotalSeconds;
            UpdateCurrentLyric(p);
        });
        host.Scanner.Progress += (_, p) => OnUi(() => ScanProgress = p);
        host.SleepTimer.Changed += (_, _) => OnUi(() => Raise(nameof(SleepTimerText)));
    }

    public async Task InitializeAsync()
    {
        Sources.Clear(); foreach (var s in _host.Settings.Value.SourceFolders) Sources.Add(s);
        ExcludedSources.Clear(); foreach (var s in _host.Settings.Value.ExcludedFolders) ExcludedSources.Add(s);
        await ReloadAsync();
        await RestoreQueueAsync();
    }

    public void CommitSeek() => _host.Playback.Seek(TimeSpan.FromSeconds(PositionSeconds));

    public void RestoreWindowPlacement(Window window)
    {
        var s = _host.Settings.Value;
        window.Width = s.WindowWidth;
        window.Height = s.WindowHeight;
        if (s.RememberWindowPosition && !double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = s.WindowLeft;
            window.Top = s.WindowTop;
        }
    }

    public async Task SaveWindowPlacementAsync(Window window)
    {
        var s = _host.Settings.Value;
        if (window.WindowState == WindowState.Normal)
        {
            s.WindowLeft = window.Left;
            s.WindowTop = window.Top;
            s.WindowWidth = window.Width;
            s.WindowHeight = window.Height;
        }
        await _host.Playback.PersistPlaybackStateAsync();
        await _host.Settings.SaveAsync();
    }

    private async Task ReloadAsync()
    {
        var songs = await _host.Database.GetSongsAsync();
        var playlists = await _host.Database.GetPlaylistsAsync();
        var hidden = await _host.Collections.GetHiddenSongsAsync();
        var albums = await _host.Collections.GetCollectionsAsync("album");
        var artists = await _host.Collections.GetCollectionsAsync("artist");
        var genres = await _host.Collections.GetCollectionsAsync("genre");
        var folders = await _host.Collections.GetCollectionsAsync("folder");
        var categories = await _host.Collections.GetCategoriesAsync();
        var playlistFolders = await _host.Collections.GetPlaylistFoldersAsync();

        OnUi(() =>
        {
            Replace(Songs, songs);
            Replace(Playlists, playlists);
            Replace(HiddenSongs, hidden);
            Replace(Albums, albums);
            Replace(Artists, artists);
            Replace(Genres, genres);
            Replace(Folders, folders);
            Replace(Categories, categories);
            Replace(PlaylistFolders, playlistFolders);
            Status = $"{Songs.Count} songs · {Albums.Count} albums · {Artists.Count} artists";
        });
    }

    private async Task RestoreQueueAsync()
    {
        var restored = new List<Song>();
        foreach (var path in _host.Settings.Value.PersistedQueuePaths)
        {
            var song = await _host.Collections.GetSongByPathAsync(path);
            if (song is not null && !song.Hidden && File.Exists(song.Path)) restored.Add(song);
        }
        _host.Playback.RestoreQueue(restored, _host.Settings.Value.PersistedQueueIndex, _host.Settings.Value.PersistedPositionSeconds);
        OnUi(RefreshQueue);
    }

    private async Task SearchAsync()
    {
        var q = SearchText.Trim();
        if (q.Length == 0) { OnUi(SearchResults.Clear); return; }
        await Task.Delay(180);
        if (!string.Equals(q, SearchText.Trim(), StringComparison.Ordinal)) return;
        var results = await _host.Database.SearchAsync(q);
        OnUi(() => Replace(SearchResults, results));
    }

    private void AddSource()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a music folder", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        var path = Path.GetFullPath(dialog.FolderName);
        if (_host.Settings.Value.SourceFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        _host.Settings.Value.SourceFolders.Add(path); Sources.Add(path); _ = _host.Settings.SaveAsync();
    }

    private async Task RemoveSourceAsync(string path)
    {
        _host.Settings.Value.SourceFolders.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
        await _host.Settings.SaveAsync();
        OnUi(() => Sources.Remove(path));
    }

    private void AddExcludedSource()
    {
        var dialog = new OpenFolderDialog { Title = "Exclude a folder", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        var path = Path.GetFullPath(dialog.FolderName);
        if (_host.Settings.Value.ExcludedFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        _host.Settings.Value.ExcludedFolders.Add(path); ExcludedSources.Add(path); _ = _host.Settings.SaveAsync();
    }

    private async Task RemoveExcludedSourceAsync(string path)
    {
        _host.Settings.Value.ExcludedFolders.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
        await _host.Settings.SaveAsync();
        OnUi(() => ExcludedSources.Remove(path));
    }

    private async Task ScanAsync()
    {
        Status = "Scanning…"; ScanProgress = 0;
        var n = await _host.Scanner.ScanAsync();
        await ReloadAsync();
        Status = $"Scan complete · {n} files checked";
    }

    private async Task ToggleFavoriteAsync(Song song) { await _host.Database.SetFavoriteAsync(song.Id, !song.Favorite); await ReloadAsync(); }
    private async Task HideSongAsync(Song song) { await _host.Collections.SetHiddenAsync(song.Id, true); await ReloadAsync(); }
    private async Task UnhideSongAsync(Song song) { await _host.Collections.SetHiddenAsync(song.Id, false); await ReloadAsync(); }

    private async Task CreatePlaylistAsync()
    {
        var name = string.IsNullOrWhiteSpace(NewPlaylistName) ? $"Playlist {Playlists.Count + 1}" : NewPlaylistName.Trim();
        await _host.Database.CreatePlaylistAsync(name);
        NewPlaylistName = "";
        await ReloadAsync();
    }

    private async Task DeleteSelectedPlaylistAsync()
    {
        if (SelectedPlaylist is null) return;
        await _host.Collections.DeletePlaylistAsync(SelectedPlaylist.Id);
        SelectedPlaylist = null;
        OnUi(SelectedPlaylistSongs.Clear);
        await ReloadAsync();
    }

    private async Task AddCurrentToSelectedPlaylistAsync()
    {
        if (SelectedPlaylist is null || CurrentSong is null) return;
        await _host.Collections.AddToPlaylistAsync(SelectedPlaylist.Id, CurrentSong.Id);
        await LoadSelectedPlaylistAsync();
    }

    private async Task RemoveFromSelectedPlaylistAsync(Song song)
    {
        if (SelectedPlaylist is null) return;
        await _host.Collections.RemoveFromPlaylistAsync(SelectedPlaylist.Id, song.Id);
        await LoadSelectedPlaylistAsync();
    }

    private async Task LoadSelectedPlaylistAsync()
    {
        if (SelectedPlaylist is null) { OnUi(SelectedPlaylistSongs.Clear); return; }
        var songs = await _host.Collections.GetPlaylistSongsAsync(SelectedPlaylist.Id);
        OnUi(() => Replace(SelectedPlaylistSongs, songs));
    }

    private async Task CreateCategoryAsync()
    {
        var name = string.IsNullOrWhiteSpace(NewCategoryName) ? $"Category {Categories.Count + 1}" : NewCategoryName.Trim();
        await _host.Collections.CreateCategoryAsync(name);
        NewCategoryName = "";
        await ReloadAsync();
    }

    private async Task AddCurrentToSelectedCategoryAsync()
    {
        if (SelectedCategory is null || CurrentSong is null) return;
        await _host.Collections.AddToCategoryAsync(SelectedCategory.Id, CurrentSong.Id);
        await LoadSelectedCategoryAsync();
    }

    private async Task LoadSelectedCategoryAsync()
    {
        if (SelectedCategory is null) { OnUi(SelectedCategorySongs.Clear); return; }
        var songs = await _host.Collections.GetCategorySongsAsync(SelectedCategory.Id);
        OnUi(() => Replace(SelectedCategorySongs, songs));
    }

    private async Task LoadSelectedCollectionAsync()
    {
        if (SelectedCollection is null)
        {
            OnUi(() => { SelectedCollectionSongs.Clear(); SelectedCollectionTitle = ""; });
            return;
        }
        var songs = await _host.Collections.GetSongsForCollectionAsync(SelectedCollection);
        OnUi(() => { Replace(SelectedCollectionSongs, songs); SelectedCollectionTitle = $"{SelectedCollection.Kind}: {SelectedCollection.Name}"; });
    }

    private async Task StartRadioAsync(Song seed)
    {
        Status = "Building local radio…";
        var mix = await _host.SmartMix.BuildRadioAsync(seed);
        await _host.Playback.SetQueueAndPlayAsync(mix);
        Status = $"Radio · {mix.Count} tracks";
    }

    private async Task AnalyzeAsync(Song song)
    {
        try
        {
            Status = $"Analyzing {song.Title}…";
            var result = await _host.Analysis.AnalyzeAsync(song);
            Status = $"{song.Title} · {result.Bpm:0} BPM · {result.EstimatedLufs:0.0} LUFS · energy {result.Energy:0.00}";
        }
        catch (Exception ex) { Status = $"Analysis failed: {ex.Message}"; }
    }

    private async Task MoveQueueAsync(Song song, int delta)
    {
        var index = Queue.IndexOf(song);
        if (index < 0) return;
        await _host.Playback.MoveQueueItemAsync(index, index + delta);
    }

    private void CycleRepeat()
    {
        var next = _host.Playback.Repeat switch { RepeatMode.Off => RepeatMode.All, RepeatMode.All => RepeatMode.One, _ => RepeatMode.Off };
        _host.Playback.SetRepeat(next);
        Raise(nameof(RepeatText));
    }

    private async Task HandleTrackChangedAsync(Song? song)
    {
        OnUi(() =>
        {
            CurrentSong = song;
            DurationSeconds = _host.Playback.Duration.TotalSeconds;
            PositionSeconds = _host.Playback.Position.TotalSeconds;
            RefreshQueue();
        });
        await LoadLyricsAsync(song);
    }

    private async Task LoadLyricsAsync(Song? song)
    {
        if (song is null)
        {
            OnUi(() => { LyricsPlain = LyricsLrc = LyricsTranslation = LyricsRomanization = CurrentLyric = ""; });
            return;
        }
        var doc = await _host.Collections.GetLyricsAsync(song.Id);
        if (string.IsNullOrWhiteSpace(doc.PlainText) && string.IsNullOrWhiteSpace(doc.LrcText))
        {
            var sidecar = LyricsService.FindSidecar(song.Path);
            if (sidecar is not null)
            {
                var text = await File.ReadAllTextAsync(sidecar);
                doc = Path.GetExtension(sidecar).Equals(".lrc", StringComparison.OrdinalIgnoreCase)
                    ? new LyricsDocument("", text, "", "")
                    : new LyricsDocument(text, "", "", "");
            }
        }
        OnUi(() =>
        {
            LyricsPlain = doc.PlainText;
            LyricsLrc = doc.LrcText;
            LyricsTranslation = doc.Translation;
            LyricsRomanization = doc.Romanization;
            UpdateCurrentLyric(TimeSpan.FromSeconds(PositionSeconds));
        });
    }

    private async Task SaveLyricsAsync()
    {
        if (CurrentSong is null) return;
        await _host.Collections.SaveLyricsAsync(CurrentSong.Id, new LyricsDocument(LyricsPlain, LyricsLrc, LyricsTranslation, LyricsRomanization));
        Status = "Lyrics saved";
    }

    private void ImportLyrics()
    {
        if (CurrentSong is null) return;
        var dialog = new OpenFileDialog { Filter = "Lyrics|*.lrc;*.txt|All files|*.*" };
        if (dialog.ShowDialog() != true) return;
        var text = File.ReadAllText(dialog.FileName);
        if (Path.GetExtension(dialog.FileName).Equals(".lrc", StringComparison.OrdinalIgnoreCase)) LyricsLrc = text;
        else LyricsPlain = text;
        Status = $"Imported {Path.GetFileName(dialog.FileName)}";
    }

    private void UpdateCurrentLyric(TimeSpan position)
    {
        if (_parsedLyrics.Count == 0) { CurrentLyric = ""; return; }
        var lo = 0; var hi = _parsedLyrics.Count - 1; var found = -1;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_parsedLyrics[mid].Time <= position) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        CurrentLyric = found >= 0 ? _parsedLyrics[found].Text : "";
    }

    private async Task SaveMetadataAsync()
    {
        if (CurrentSong is null) return;
        await _host.Collections.UpdateMetadataAsync(CurrentSong.Id, EditTitle, EditArtist, EditAlbum, EditGenre, string.IsNullOrWhiteSpace(EditArtworkPath) ? null : EditArtworkPath);
        await ReloadAsync();
        var updated = Songs.FirstOrDefault(x => x.Id == CurrentSong.Id);
        if (updated is not null) CurrentSong = updated;
        Status = "Track metadata updated";
    }

    private void ChooseArtwork()
    {
        var dialog = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp|All files|*.*" };
        if (dialog.ShowDialog() == true) EditArtworkPath = dialog.FileName;
    }

    private void Backup()
    {
        var d = new SaveFileDialog { Filter = "NEO Backup|*.neobackup", FileName = $"neo-{DateTime.Now:yyyyMMdd-HHmm}.neobackup" };
        if (d.ShowDialog() == true) _ = BackupCoreAsync(d.FileName);
    }

    private async Task BackupCoreAsync(string path)
    {
        try { await _host.Backup.ExportAsync(path); Status = "Backup created"; }
        catch (Exception ex) { Status = $"Backup failed: {ex.Message}"; }
    }

    private void Restore()
    {
        var d = new OpenFileDialog { Filter = "NEO Backup|*.neobackup" };
        if (d.ShowDialog() == true) _ = RestoreCoreAsync(d.FileName);
    }

    private async Task RestoreCoreAsync(string path)
    {
        _host.Playback.Pause();
        await _host.Backup.RestoreAsync(path);
        await _host.Settings.LoadAsync();
        await ReloadAsync();
        Status = "Backup restored";
    }

    private void ResetEq()
    {
        foreach (var band in EqBands) band.SetWithoutCallback(0);
        for (var i = 0; i < 10; i++) _host.Playback.SetEqBand(i, 0);
        BassDb = 0;
        StereoWidth = 1;
    }

    private void RefreshQueue()
    {
        Replace(Queue, _host.Playback.Queue);
        Raise(nameof(Shuffle));
        Raise(nameof(RepeatText));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
