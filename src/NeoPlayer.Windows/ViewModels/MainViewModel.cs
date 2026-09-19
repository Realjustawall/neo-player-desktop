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
    private string _searchText = "";
    private string _status = "Ready";
    private double _positionSeconds;
    private double _durationSeconds;
    private bool _isPlaying;
    private int _scanProgress;

    public ObservableCollection<Song> Songs { get; } = new();
    public ObservableCollection<Song> SearchResults { get; } = new();
    public ObservableCollection<Song> Queue { get; } = new();
    public ObservableCollection<Playlist> Playlists { get; } = new();
    public ObservableCollection<string> Sources { get; } = new();

    public NavigationPage Page { get => _page; set { if (Set(ref _page, value)) { Raise(nameof(IsHome)); Raise(nameof(IsSearch)); Raise(nameof(IsLibrary)); Raise(nameof(IsSettings)); } } }
    public bool IsHome => Page == NavigationPage.Home;
    public bool IsSearch => Page == NavigationPage.Search;
    public bool IsLibrary => Page == NavigationPage.Library;
    public bool IsSettings => Page == NavigationPage.Settings;
    public Song? CurrentSong { get => _currentSong; private set => Set(ref _currentSong, value); }
    public string SearchText { get => _searchText; set { if (Set(ref _searchText, value)) _ = SearchAsync(); } }
    public string Status { get => _status; set => Set(ref _status, value); }
    public double PositionSeconds { get => _positionSeconds; set => Set(ref _positionSeconds, value); }
    public double DurationSeconds { get => _durationSeconds; private set => Set(ref _durationSeconds, value); }
    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }
    public int ScanProgress { get => _scanProgress; private set => Set(ref _scanProgress, value); }
    public double Volume { get => _host.Playback.Volume; set { _host.Playback.SetVolume(value); Raise(); } }
    public string NowTitle => CurrentSong?.Title ?? "NEO Player";
    public string NowArtist => CurrentSong?.Artist ?? "Nothing playing";

    public ICommand HomeCommand { get; }
    public ICommand SearchPageCommand { get; }
    public ICommand LibraryCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand PlaySongCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand AddSourceCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand CreatePlaylistCommand { get; }
    public ICommand SmartRadioCommand { get; }
    public ICommand BackupCommand { get; }
    public ICommand RestoreCommand { get; }

    public MainViewModel(AppHost host)
    {
        _host = host;
        HomeCommand = new RelayCommand(() => Page = NavigationPage.Home);
        SearchPageCommand = new RelayCommand(() => Page = NavigationPage.Search);
        LibraryCommand = new RelayCommand(() => Page = NavigationPage.Library);
        SettingsCommand = new RelayCommand(() => Page = NavigationPage.Settings);
        PlayPauseCommand = new RelayCommand(() => _host.Playback.PlayPause());
        NextCommand = new AsyncCommand(_host.Playback.NextAsync);
        PreviousCommand = new AsyncCommand(_host.Playback.PreviousAsync);
        PlaySongCommand = new RelayCommand<Song>(song => { if (song is not null) _ = _host.Playback.PlaySongAsync(song, Songs); });
        ToggleFavoriteCommand = new RelayCommand<Song>(song => { if (song is not null) _ = ToggleFavoriteAsync(song); });
        AddSourceCommand = new RelayCommand(AddSource);
        ScanCommand = new AsyncCommand(ScanAsync);
        CreatePlaylistCommand = new AsyncCommand(CreatePlaylistAsync);
        SmartRadioCommand = new RelayCommand<Song>(song => { if (song is not null) _ = StartRadioAsync(song); });
        BackupCommand = new RelayCommand(Backup);
        RestoreCommand = new RelayCommand(Restore);
        host.Playback.TrackChanged += (_, song) => Application.Current.Dispatcher.Invoke(() => { CurrentSong = song; Raise(nameof(NowTitle)); Raise(nameof(NowArtist)); DurationSeconds = _host.Playback.Duration.TotalSeconds; RefreshQueue(); });
        host.Playback.StateChanged += (_, _) => Application.Current.Dispatcher.Invoke(() => IsPlaying = host.Playback.IsPlaying);
        host.Playback.PositionChanged += (_, p) => Application.Current.Dispatcher.BeginInvoke(() => { PositionSeconds = p.TotalSeconds; DurationSeconds = _host.Playback.Duration.TotalSeconds; });
        host.Scanner.Progress += (_, p) => Application.Current.Dispatcher.BeginInvoke(() => ScanProgress = p);
    }

    public async Task InitializeAsync()
    {
        Sources.Clear(); foreach (var s in _host.Settings.Value.SourceFolders) Sources.Add(s);
        await ReloadAsync();
    }

    public void CommitSeek() => _host.Playback.Seek(TimeSpan.FromSeconds(PositionSeconds));

    private async Task ReloadAsync()
    {
        var songs = await _host.Database.GetSongsAsync(); var playlists = await _host.Database.GetPlaylistsAsync();
        Application.Current.Dispatcher.Invoke(() => { Songs.Clear(); foreach (var s in songs) Songs.Add(s); Playlists.Clear(); foreach (var p in playlists) Playlists.Add(p); Status = $"{Songs.Count} songs"; });
    }

    private async Task SearchAsync()
    {
        var q = SearchText.Trim(); if (q.Length == 0) { Application.Current.Dispatcher.Invoke(SearchResults.Clear); return; }
        await Task.Delay(180); if (!string.Equals(q, SearchText.Trim(), StringComparison.Ordinal)) return;
        var results = await _host.Database.SearchAsync(q);
        Application.Current.Dispatcher.Invoke(() => { SearchResults.Clear(); foreach (var s in results) SearchResults.Add(s); });
    }

    private void AddSource()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a music folder", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        var path = Path.GetFullPath(dialog.FolderName); if (_host.Settings.Value.SourceFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        _host.Settings.Value.SourceFolders.Add(path); Sources.Add(path); _ = _host.Settings.SaveAsync();
    }

    private async Task ScanAsync() { Status = "Scanning…"; ScanProgress = 0; var n = await _host.Scanner.ScanAsync(); await ReloadAsync(); Status = $"Scan complete · {n} files checked"; }
    private async Task ToggleFavoriteAsync(Song song) { await _host.Database.SetFavoriteAsync(song.Id, !song.Favorite); await ReloadAsync(); }
    private async Task CreatePlaylistAsync() { var name = $"Playlist {Playlists.Count + 1}"; await _host.Database.CreatePlaylistAsync(name); await ReloadAsync(); }
    private async Task StartRadioAsync(Song seed) { var mix = await _host.SmartMix.BuildRadioAsync(seed); await _host.Playback.SetQueueAndPlayAsync(mix); }

    private void Backup()
    {
        var d = new SaveFileDialog { Filter = "NEO Backup|*.neobackup", FileName = $"neo-{DateTime.Now:yyyyMMdd-HHmm}.neobackup" };
        if (d.ShowDialog() == true) _ = _host.Backup.ExportAsync(d.FileName);
    }
    private void Restore()
    {
        var d = new OpenFileDialog { Filter = "NEO Backup|*.neobackup" };
        if (d.ShowDialog() == true) _ = RestoreCoreAsync(d.FileName);
    }
    private async Task RestoreCoreAsync(string path) { _host.Playback.Pause(); await _host.Backup.RestoreAsync(path); await _host.Settings.LoadAsync(); await ReloadAsync(); }
    private void RefreshQueue() { Queue.Clear(); foreach (var s in _host.Playback.Queue) Queue.Add(s); }
}
