using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;
using NeoPlayer.Windows.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NeoPlayer.Windows.ViewModels;

public sealed class MainViewModel : BindableBase
{
    readonly AppHost host;
    readonly NeoDatabase db;
    readonly CancellationTokenSource lifetime = new();
    CancellationTokenSource? searchCts;
    CancellationTokenSource? analysisCts;
    PageKind page = PageKind.Home;
    bool initialized;
    bool isBusy;
    string status = "";
    string searchText = "";
    bool nowPlayingOpen;
    bool settingsOpen;
    bool queueOpen;
    bool lyricsOpen;
    bool eqOpen;
    bool trackPlusOpen;
    bool collectionOpen;
    bool hiddenOpen;
    bool lyricsEditorOpen;
    string? currentArtwork;
    LyricsRecord? currentLyrics;
    LyricDraftLine? selectedLyricDraft;
    string lyricsTranslation = "";
    string lyricsRomanization = "";
    float eqBass;
    float eqVirtualizer;
    float eqLoudness;
    string eqPreset = "Normal";
    long seekPreview;
    bool seeking;
    int analysisCurrent;
    int analysisTotal;
    string analysisTrack = "";
    long cacheSize;
    string selectedSpeechModelId = "";
    double visualPulse;

    public bool ReallyExit { get; set; }
    public AppSettings Settings => host.Settings.Current;
    public PlaybackState Playback => host.Playback.State;
    public LocalizationService L => host.Localization;
    public ThemeService Theme => host.Theme;
    public SleepTimerService SleepTimer => host.SleepTimer;
    public PageKind CurrentPage { get => page; set => Set(ref page, value); }
    public bool IsBusy { get => isBusy; set => Set(ref isBusy, value); }
    public string Status { get => status; set => Set(ref status, value); }
    public string SearchText { get => searchText; set { if (Set(ref searchText, value)) DebounceSearch(); } }
    public bool IsNowPlayingOpen { get => nowPlayingOpen; set => Set(ref nowPlayingOpen, value); }
    public bool IsSettingsOpen { get => settingsOpen; set => Set(ref settingsOpen, value); }
    public bool IsQueueOpen { get => queueOpen; set => Set(ref queueOpen, value); }
    public bool IsLyricsOpen { get => lyricsOpen; set => Set(ref lyricsOpen, value); }
    public bool IsEqOpen { get => eqOpen; set => Set(ref eqOpen, value); }
    public bool IsTrackPlusOpen { get => trackPlusOpen; set => Set(ref trackPlusOpen, value); }
    public bool IsCollectionOpen { get => collectionOpen; set => Set(ref collectionOpen, value); }
    public bool IsHiddenOpen { get => hiddenOpen; set => Set(ref hiddenOpen, value); }
    public bool IsLyricsEditorOpen { get => lyricsEditorOpen; set => Set(ref lyricsEditorOpen, value); }
    public string? CurrentArtwork { get => currentArtwork; private set => Set(ref currentArtwork, value); }
    public LyricsRecord? CurrentLyrics { get => currentLyrics; private set => Set(ref currentLyrics, value); }
    public LyricDraftLine? SelectedLyricDraft { get => selectedLyricDraft; set => Set(ref selectedLyricDraft, value); }
    public string LyricsTranslation { get => lyricsTranslation; set => Set(ref lyricsTranslation, value); }
    public string LyricsRomanization { get => lyricsRomanization; set => Set(ref lyricsRomanization, value); }
    public float EqBass { get => eqBass; set => Set(ref eqBass, value); }
    public float EqVirtualizer { get => eqVirtualizer; set => Set(ref eqVirtualizer, value); }
    public float EqLoudness { get => eqLoudness; set => Set(ref eqLoudness, value); }
    public string EqPreset { get => eqPreset; set => Set(ref eqPreset, value); }
    public long SeekPreview { get => seekPreview; set => Set(ref seekPreview, value); }
    public bool IsSeeking { get => seeking; set => Set(ref seeking, value); }
    public int AnalysisCurrent { get => analysisCurrent; private set => Set(ref analysisCurrent, value); }
    public int AnalysisTotal { get => analysisTotal; private set => Set(ref analysisTotal, value); }
    public string AnalysisTrack { get => analysisTrack; private set => Set(ref analysisTrack, value); }
    public long CacheSize { get => cacheSize; private set { if (Set(ref cacheSize, value)) Raise(nameof(CacheSizeText)); } }
    public string SelectedSpeechModelId { get => selectedSpeechModelId; set { if (Set(ref selectedSpeechModelId, value ?? "")) _ = SaveSpeechPreferenceAsync(); } }
    public string CacheSizeText => FormatBytes(CacheSize);
    public double VisualPulse { get => visualPulse; private set => Set(ref visualPulse, value); }
    public string PositionText => Time(Playback.PositionMs);
    public string DurationText => Time(Playback.DurationMs);
    public double SeekMaximum => Math.Max(1, Playback.DurationMs);
    public double SeekValue { get => IsSeeking ? SeekPreview : Playback.PositionMs; set { SeekPreview = (long)value; } }
    public string RepeatText => Playback.Repeat.ToString();
    public string PlayPauseGlyph => Playback.IsPlaying ? "⏸" : "▶";
    public string ShuffleGlyph => Playback.Shuffle ? "🔀" : "⇄";
    public string Greeting => DateTime.Now.Hour switch { >= 5 and <= 11 => "Good morning", >= 12 and <= 17 => "Good afternoon", _ => "Good evening" };
    public string LibraryCountText => $"{Songs.Count:N0} tracks";
    public System.Windows.FlowDirection FlowDirection => L.IsRtl ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;
    public ThemeMode[] ThemeModes { get; } = Enum.GetValues<ThemeMode>();
    public Accent[] Accents { get; } = Enum.GetValues<Accent>();
    public string[] Languages { get; } = ["system", "en", "fa"];
    public string[] NormalizationModes { get; } = ["smart", "replaygain", "analysis"];
    public string[] LyricsModes { get; } = ["auto", "local", "provider", "off"];
    public string[] CollectionSortModes { get; } = ["custom", "title", "artist", "album", "date", "duration"];
    public string[] CollectionViewModes { get; } = ["list", "grid"];
    public string[] CanvasFits { get; } = ["crop", "fit", "stretch"];
    public string[] TrackThemeModes { get; } = ["inherit", "system", "dark", "light", "amoled"];
    public string[] BackgroundModes { get; } = ["overlay", "replace", "none"];
    public string[] VisualizerModes { get; } = ["waveform", "spectrum", "bars", "pulse", "off"];

    public ObservableCollection<Song> Songs { get; } = [];
    public ObservableCollection<Song> RecentSongs { get; } = [];
    public ObservableCollection<Song> FavoriteSongs { get; } = [];
    public ObservableCollection<Song> MostPlayed { get; } = [];
    public ObservableCollection<Song> RecentlyPlayed { get; } = [];
    public ObservableCollection<Song> NeverPlayed { get; } = [];
    public ObservableCollection<Song> ForgottenFavorites { get; } = [];
    public ObservableCollection<Song> DayMix { get; } = [];
    public ObservableCollection<RecommendationItem> Recommendations { get; } = [];
    public ObservableCollection<Song> SearchResults { get; } = [];
    public ObservableCollection<SearchSuggestion> SearchSuggestions { get; } = [];
    public ObservableCollection<Playlist> Playlists { get; } = [];
    public ObservableCollection<Category> Categories { get; } = [];
    public ObservableCollection<PlaylistFolder> PlaylistFolders { get; } = [];
    public ObservableCollection<PlaylistFolderNode> FolderTree { get; } = [];
    public ObservableCollection<CollectionCard> PlaylistCards { get; } = [];
    public ObservableCollection<CollectionCard> CategoryCards { get; } = [];
    public ObservableCollection<CollectionCard> PlaylistFolderCards { get; } = [];
    public ObservableCollection<CollectionCard> AlbumCards { get; } = [];
    public ObservableCollection<CollectionCard> ArtistCards { get; } = [];
    public ObservableCollection<CollectionCard> GenreCards { get; } = [];
    public ObservableCollection<CollectionCard> FolderCards { get; } = [];
    public ObservableCollection<CollectionCard> PinnedCards { get; } = [];
    public ObservableCollection<Song> HiddenSongs { get; } = [];
    public ObservableCollection<Song> OfflineBackupSongs { get; } = [];
    public ObservableCollection<OfflineSpeechModel> SpeechModels { get; } = [];
    public ObservableCollection<LyricDraftLine> LyricDraftLines { get; } = [];
    public ObservableCollection<EqBandViewModel> EqBands { get; } = [];
    public ObservableCollection<double> SpectrumBars { get; } = [];
    public ObservableCollection<double> WaveformPoints { get; } = [];
    public TrackPlusEditorViewModel TrackPlus { get; } = new();
    public CollectionDetailViewModel CollectionDetail { get; } = new();
    public HashSet<long> FavoriteIds { get; private set; } = [];

    public ICommand HomeCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand LibraryCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand OpenNowPlayingCommand { get; }
    public ICommand CloseNowPlayingCommand { get; }
    public ICommand TogglePlayCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand ShuffleCommand { get; }
    public ICommand RepeatCommand { get; }
    public ICommand PlaySongCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand PlayNextCommand { get; }
    public ICommand AddQueueCommand { get; }
    public ICommand RemoveQueueCommand { get; }
    public ICommand OpenQueueCommand { get; }
    public ICommand OpenLyricsCommand { get; }
    public ICommand OpenEqCommand { get; }
    public ICommand OpenTrackPlusCommand { get; }
    public ICommand ClosePanelsCommand { get; }
    public ICommand RescanCommand { get; }
    public ICommand AddMusicFolderCommand { get; }
    public ICommand ExcludeFolderCommand { get; }
    public ICommand AnalyzeLibraryCommand { get; }
    public ICommand CancelAnalysisCommand { get; }
    public ICommand CreatePlaylistCommand { get; }
    public ICommand CreateCategoryCommand { get; }
    public ICommand CreateFolderCommand { get; }
    public ICommand OpenCollectionCommand { get; }
    public ICommand CloseCollectionCommand { get; }
    public ICommand PlayCollectionCommand { get; }
    public ICommand PinCollectionCommand { get; }
    public ICommand FavoriteCollectionCommand { get; }
    public ICommand MoreLikeCommand { get; }
    public ICommand LessLikeCommand { get; }
    public ICommand HideSongCommand { get; }
    public ICommand UnhideSongCommand { get; }
    public ICommand OpenHiddenCommand { get; }
    public ICommand EditMetadataCommand { get; }
    public ICommand RevealFileCommand { get; }
    public ICommand ShareFileCommand { get; }
    public ICommand RecycleFileCommand { get; }
    public ICommand AddToPlaylistCommand { get; }
    public ICommand AddToCategoryCommand { get; }
    public ICommand ImportM3uCommand { get; }
    public ICommand ExportM3uCommand { get; }
    public ICommand CreateBackupCommand { get; }
    public ICommand RestoreBackupCommand { get; }
    public ICommand ClearCacheCommand { get; }
    public ICommand RefreshOfflineBackupCommand { get; }
    public ICommand SetListViewCommand { get; }
    public ICommand SetGridViewCommand { get; }
    public ICommand ApplyEqCommand { get; }
    public ICommand EqPresetCommand { get; }
    public ICommand ImportLyricsCommand { get; }
    public ICommand EditLyricsCommand { get; }
    public ICommand StampLyricCommand { get; }
    public ICommand SaveLyricsCommand { get; }
    public ICommand ImportSpeechModelCommand { get; }
    public ICommand TranscribeCommand { get; }
    public ICommand ForceAlignCommand { get; }
    public ICommand PickCanvasCommand { get; }
    public ICommand PickBackgroundCommand { get; }
    public ICommand PickArtworkCommand { get; }
    public ICommand SaveTrackPlusCommand { get; }
    public ICommand Sleep15Command { get; }
    public ICommand Sleep30Command { get; }
    public ICommand Sleep60Command { get; }
    public ICommand CancelSleepCommand { get; }
    public ICommand ClearRecentSearchesCommand { get; }
    public ICommand ApplySuggestionCommand { get; }
    public ICommand PickCustomAccentCommand { get; }
    public ICommand RenameCollectionCommand { get; }
    public ICommand DeleteCollectionCommand { get; }
    public ICommand MovePlaylistCommand { get; }
    public ICommand MoveFolderCommand { get; }
    public ICommand NewChildFolderCommand { get; }
    public ICommand MovePlaylistUpCommand { get; }
    public ICommand MovePlaylistDownCommand { get; }
    public ICommand MovePinUpCommand { get; }
    public ICommand MovePinDownCommand { get; }
    public ICommand SetCollectionArtworkCommand { get; }
    public ICommand RemoveFromCollectionCommand { get; }
    public ICommand ResetMetadataCommand { get; }
    public ICommand RemoveIncludedFolderCommand { get; }
    public ICommand RemoveExcludedFolderCommand { get; }
    public ICommand StartRadioCommand { get; }
    public ICommand SongDetailsCommand { get; }
    public ICommand ClearArtworkCommand { get; }
    public ICommand OpenCompactPlayerCommand { get; }

    public MainViewModel(AppHost host)
    {
        this.host = host;
        db = host.Database;
        for (var i = 0; i < 48; i++) SpectrumBars.Add(0);
        var labels = new[] { "31", "62", "125", "250", "500", "1K", "2K", "4K", "8K", "16K" };
        for (var i = 0; i < labels.Length; i++) EqBands.Add(new EqBandViewModel { Index = i, Label = labels[i] });

        HomeCommand = new RelayCommand(() => CurrentPage = PageKind.Home);
        SearchCommand = new RelayCommand(() => CurrentPage = PageKind.Search);
        LibraryCommand = new RelayCommand(() => CurrentPage = PageKind.Library);
        OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = true);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
        OpenNowPlayingCommand = new RelayCommand(() => IsNowPlayingOpen = Playback.Current is not null);
        CloseNowPlayingCommand = new RelayCommand(() => IsNowPlayingOpen = false);
        TogglePlayCommand = new RelayCommand(host.Playback.TogglePlay);
        NextCommand = new AsyncRelayCommand(() => host.Playback.NextAsync());
        PreviousCommand = new AsyncRelayCommand(host.Playback.PreviousAsync);
        ShuffleCommand = new RelayCommand(host.Playback.ToggleShuffle);
        RepeatCommand = new RelayCommand(host.Playback.CycleRepeat);
        PlaySongCommand = new AsyncRelayCommand<Song>(PlaySongAsync, s => s is not null);
        ToggleFavoriteCommand = new AsyncRelayCommand<Song>(ToggleFavoriteAsync, s => s is not null);
        PlayNextCommand = new AsyncRelayCommand<Song>(s => s is null ? Task.CompletedTask : host.Playback.PlayNextAsync(s));
        AddQueueCommand = new AsyncRelayCommand<Song>(s => s is null ? Task.CompletedTask : host.Playback.AddToQueueAsync(s));
        RemoveQueueCommand = new AsyncRelayCommand<Song>(s => s is null ? Task.CompletedTask : host.Playback.RemoveFromQueueAsync(s));
        OpenQueueCommand = new RelayCommand(() => { IsQueueOpen = true; IsLyricsOpen = IsEqOpen = IsTrackPlusOpen = false; });
        OpenLyricsCommand = new RelayCommand(() => { IsLyricsOpen = true; IsQueueOpen = IsEqOpen = IsTrackPlusOpen = false; });
        OpenEqCommand = new RelayCommand(() => { IsEqOpen = true; IsQueueOpen = IsLyricsOpen = IsTrackPlusOpen = false; });
        OpenTrackPlusCommand = new RelayCommand(() => { IsTrackPlusOpen = true; IsQueueOpen = IsLyricsOpen = IsEqOpen = false; });
        ClosePanelsCommand = new RelayCommand(() => IsQueueOpen = IsLyricsOpen = IsEqOpen = IsTrackPlusOpen = false);
        RescanCommand = new AsyncRelayCommand(RescanAsync);
        AddMusicFolderCommand = new AsyncRelayCommand(AddMusicFolderAsync);
        ExcludeFolderCommand = new AsyncRelayCommand(ExcludeFolderAsync);
        AnalyzeLibraryCommand = new AsyncRelayCommand(AnalyzeLibraryAsync, () => analysisCts is null);
        CancelAnalysisCommand = new RelayCommand(() => analysisCts?.Cancel());
        CreatePlaylistCommand = new AsyncRelayCommand(CreatePlaylistAsync);
        CreateCategoryCommand = new AsyncRelayCommand(CreateCategoryAsync);
        CreateFolderCommand = new AsyncRelayCommand(CreateFolderAsync);
        OpenCollectionCommand = new AsyncRelayCommand<CollectionCard>(OpenCollectionAsync, x => x is not null);
        CloseCollectionCommand = new RelayCommand(() => IsCollectionOpen = false);
        PlayCollectionCommand = new AsyncRelayCommand(() => host.Playback.PlayQueueAsync(CollectionDetail.VisibleSongs));
        PinCollectionCommand = new AsyncRelayCommand<CollectionCard>(TogglePinAsync, x => x is not null);
        FavoriteCollectionCommand = new AsyncRelayCommand<CollectionCard>(ToggleCollectionFavoriteAsync, x => x is not null);
        MoreLikeCommand = new AsyncRelayCommand<Song>(s => FeedbackAsync(s, 1, false));
        LessLikeCommand = new AsyncRelayCommand<Song>(s => FeedbackAsync(s, -1, true));
        HideSongCommand = new AsyncRelayCommand<Song>(HideSongAsync);
        UnhideSongCommand = new AsyncRelayCommand<Song>(UnhideSongAsync);
        OpenHiddenCommand = new AsyncRelayCommand(OpenHiddenAsync);
        EditMetadataCommand = new AsyncRelayCommand<Song>(EditMetadataAsync);
        RevealFileCommand = new RelayCommand<Song>(s => { if (s is not null) host.Files.Reveal(s); });
        ShareFileCommand = new RelayCommand<Song>(s => { if (s is not null) host.Files.Share(s); });
        RecycleFileCommand = new AsyncRelayCommand<Song>(RecycleFileAsync);
        AddToPlaylistCommand = new AsyncRelayCommand<Song>(AddToPlaylistAsync);
        AddToCategoryCommand = new AsyncRelayCommand<Song>(AddToCategoryAsync);
        ImportM3uCommand = new AsyncRelayCommand(ImportM3uAsync);
        ExportM3uCommand = new AsyncRelayCommand(ExportM3uAsync);
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync);
        RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync);
        ClearCacheCommand = new AsyncRelayCommand(ClearCacheAsync);
        RefreshOfflineBackupCommand = new AsyncRelayCommand(RefreshOfflineBackupAsync);
        SetListViewCommand = new AsyncRelayCommand(() => SetLibraryViewAsync(LibraryViewMode.List));
        SetGridViewCommand = new AsyncRelayCommand(() => SetLibraryViewAsync(LibraryViewMode.Grid));
        ApplyEqCommand = new AsyncRelayCommand(ApplyEqAsync);
        EqPresetCommand = new AsyncRelayCommand<string>(ApplyEqPresetAsync);
        ImportLyricsCommand = new AsyncRelayCommand(ImportLyricsAsync);
        EditLyricsCommand = new RelayCommand(OpenLyricsEditor);
        StampLyricCommand = new RelayCommand(StampSelectedLyric);
        SaveLyricsCommand = new AsyncRelayCommand(SaveAuthoredLyricsAsync);
        ImportSpeechModelCommand = new AsyncRelayCommand(ImportSpeechModelAsync);
        TranscribeCommand = new AsyncRelayCommand(TranscribeAsync);
        ForceAlignCommand = new AsyncRelayCommand(ForceAlignAsync);
        PickCanvasCommand = new AsyncRelayCommand(PickCanvasAsync);
        PickBackgroundCommand = new AsyncRelayCommand(PickBackgroundAsync);
        PickArtworkCommand = new AsyncRelayCommand(PickArtworkAsync);
        SaveTrackPlusCommand = new AsyncRelayCommand(SaveTrackPlusAsync);
        Sleep15Command = new RelayCommand(() => host.SleepTimer.Start(TimeSpan.FromMinutes(15)));
        Sleep30Command = new RelayCommand(() => host.SleepTimer.Start(TimeSpan.FromMinutes(30)));
        Sleep60Command = new RelayCommand(() => host.SleepTimer.Start(TimeSpan.FromMinutes(60)));
        CancelSleepCommand = new RelayCommand(host.SleepTimer.Cancel);
        ClearRecentSearchesCommand = new AsyncRelayCommand(host.Settings.ClearRecentSearchesAsync);
        ApplySuggestionCommand = new AsyncRelayCommand<SearchSuggestion>(s => s is null ? Task.CompletedTask : ApplySearchSuggestionAsync(s));
        PickCustomAccentCommand = new RelayCommand(() => { var c = host.Dialogs.PickColor(Settings.CustomColor); if (c is int argb) { Settings.CustomColor = argb; Settings.Accent = Accent.Custom; } });
        RenameCollectionCommand = new AsyncRelayCommand<CollectionCard>(RenameCollectionAsync);
        DeleteCollectionCommand = new AsyncRelayCommand<CollectionCard>(DeleteCollectionAsync);
        MovePlaylistCommand = new AsyncRelayCommand<CollectionCard>(MovePlaylistAsync);
        MoveFolderCommand = new AsyncRelayCommand<CollectionCard>(MoveFolderAsync);
        NewChildFolderCommand = new AsyncRelayCommand<CollectionCard>(NewChildFolderAsync);
        MovePlaylistUpCommand = new AsyncRelayCommand<CollectionCard>(c => MovePlaylistOrderAsync(c, -1));
        MovePlaylistDownCommand = new AsyncRelayCommand<CollectionCard>(c => MovePlaylistOrderAsync(c, 1));
        MovePinUpCommand = new AsyncRelayCommand<CollectionCard>(c => MovePinAsync(c, -1));
        MovePinDownCommand = new AsyncRelayCommand<CollectionCard>(c => MovePinAsync(c, 1));
        SetCollectionArtworkCommand = new AsyncRelayCommand<CollectionCard>(SetCollectionArtworkAsync);
        RemoveFromCollectionCommand = new AsyncRelayCommand<Song>(RemoveFromCurrentCollectionAsync);
        ResetMetadataCommand = new AsyncRelayCommand<Song>(ResetMetadataAsync);
        RemoveIncludedFolderCommand = new AsyncRelayCommand<string>(RemoveIncludedFolderAsync);
        RemoveExcludedFolderCommand = new AsyncRelayCommand<string>(RemoveExcludedFolderAsync);
        StartRadioCommand = new AsyncRelayCommand<Song>(StartRadioAsync, s => s is not null);
        SongDetailsCommand = new AsyncRelayCommand<Song>(ShowSongDetailsAsync, s => s is not null);
        ClearArtworkCommand = new AsyncRelayCommand<Song>(ClearArtworkAsync, s => s is not null);
        OpenCompactPlayerCommand = new RelayCommand(host.Windows.ShowCompact);

        host.Playback.StateChanged += PlaybackChanged;
        host.Library.Changed += LibraryChanged;
        host.Settings.Changed += SettingsChanged;
        host.Localization.PropertyChanged += LocalizationChanged;
        host.Spectrum.Frame += SpectrumFrame;
        host.Analysis.Progress += AnalysisProgress;
        CollectionDetail.PropertyChanged += CollectionDetailChanged;
    }

    public async Task InitializeAsync()
    {
        if (initialized) return;
        initialized = true;
        await host.Theme.ApplyAsync(null);
        await LoadLibraryAsync();
        if (Songs.Count == 0 && Settings.IncludedFolders.Count > 0) await RescanAsync();
        else await RefreshDerivedAsync();
        await LoadSpeechModelsAsync();
        await UpdateCurrentAsync();
        CacheSize = host.Cache.Size();
    }

    async Task LoadLibraryAsync()
    {
        var songs = await db.GetSongsAsync();
        Replace(Songs, songs);
        FavoriteIds = await db.GetFavoriteIdsAsync();
        Replace(FavoriteSongs, songs.Where(x => FavoriteIds.Contains(x.Id)).Take(200));
        Replace(RecentSongs, songs.OrderByDescending(x => x.DateAdded).Take(100));
        Replace(Playlists, await db.GetPlaylistsAsync());
        Replace(Categories, await db.GetCategoriesAsync());
        Replace(PlaylistFolders, await db.GetPlaylistFoldersAsync());
        BuildCollectionCards();
        BuildFolderTree();
        Raise(nameof(LibraryCountText));
    }

    async Task RefreshDerivedAsync()
    {
        Replace(MostPlayed, await host.SmartMix.MostPlayedAsync(Songs, 60));
        Replace(RecentlyPlayed, await host.SmartMix.RecentlyPlayedAsync(Songs, 60));
        Replace(NeverPlayed, await host.SmartMix.NeverPlayedAsync(Songs, 60));
        Replace(ForgottenFavorites, await host.SmartMix.ForgottenFavoritesAsync(Songs, FavoriteIds, 60));
        Replace(DayMix, await host.SmartMix.TimeOfDayAsync(Songs, DateTime.Now.Hour, 60));
        var rec = await host.Recommendations.GetAsync(Songs, FavoriteIds, 30);
        Replace(Recommendations, rec.Select(x => new RecommendationItem(x.Song, x.Score, x.Reason)));
        Replace(OfflineBackupSongs, await host.OfflineBackup.GetAsync(Songs));
        await RefreshPinsAsync();
    }

    void BuildCollectionCards()
    {
        Replace(PlaylistCards, Playlists.Select(CardFor));
        Replace(CategoryCards, Categories.Select(CardFor));
        Replace(PlaylistFolderCards, PlaylistFolders.Select(CardFor));
        Replace(AlbumCards, Songs.GroupBy(x => x.Album ?? "", StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .OrderBy(g => g.Key).Select(g => new CollectionCard { Kind = CollectionKind.Album, Key = g.Key, Title = g.Key, Subtitle = $"{g.Count()} tracks · {g.First().Artist}" }));
        Replace(ArtistCards, Songs.GroupBy(x => x.Artist ?? "", StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .OrderBy(g => g.Key).Select(g => new CollectionCard { Kind = CollectionKind.Artist, Key = g.Key, Title = g.Key, Subtitle = $"{g.Count()} tracks" }));
        Replace(GenreCards, Songs.GroupBy(x => x.Genre ?? "", StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .OrderBy(g => g.Key).Select(g => new CollectionCard { Kind = CollectionKind.Genre, Key = g.Key, Title = g.Key, Subtitle = $"{g.Count()} tracks" }));
        Replace(FolderCards, Songs.GroupBy(x => Path.GetDirectoryName(x.Path) ?? "", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0).OrderBy(g => g.Key)
            .Select(g => new CollectionCard { Kind = CollectionKind.Folder, Key = g.Key, Title = Path.GetFileName(g.Key.TrimEnd(Path.DirectorySeparatorChar)), Subtitle = $"{g.Count()} tracks · {g.Key}" }));
    }

    void BuildFolderTree()
    {
        FolderTree.Clear();
        var map = PlaylistFolders.ToDictionary(x => x.Id, x => new PlaylistFolderNode { Folder = x });
        foreach (var folder in map.Values)
        {
            if (folder.Folder.ParentId is long p && map.TryGetValue(p, out var parent)) parent.Children.Add(folder);
            else FolderTree.Add(folder);
        }
        foreach (var playlist in Playlists)
            if (playlist.FolderId is long f && map.TryGetValue(f, out var node)) node.Playlists.Add(playlist);
    }

    async Task RefreshPinsAsync()
    {
        var pins = await db.GetPinsAsync();
        var favs = (await db.GetFavoriteCollectionsAsync()).ToHashSet();
        var list = new List<CollectionCard>();
        foreach (var p in pins)
        {
            var card = ResolveCard(p.Type, p.Key);
            if (card is null) continue;
            card.IsPinned = true;
            card.IsFavorite = favs.Contains((p.Type, p.Key));
            list.Add(card);
        }
        Replace(PinnedCards, list);
    }

    CollectionCard? ResolveCard(string type, string key)
    {
        if (type == "playlist" && long.TryParse(key, out var pid))
        {
            var p = Playlists.FirstOrDefault(x => x.Id == pid);
            return p is null ? null : new CollectionCard { Kind = CollectionKind.Playlist, Key = key, NumericId = pid, Title = p.Title, Subtitle = p.Description, ArtworkPath = p.ArtworkPath };
        }
        if (type == "category" && long.TryParse(key, out var cid))
        {
            var c = Categories.FirstOrDefault(x => x.Id == cid);
            return c is null ? null : new CollectionCard { Kind = CollectionKind.Category, Key = key, NumericId = cid, Title = c.Title, Subtitle = c.Description, ArtworkPath = c.ArtworkPath };
        }
        var source = type switch { "album" => AlbumCards, "artist" => ArtistCards, "genre" => GenreCards, "folder" => FolderCards, _ => null };
        return source?.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    public CollectionCard CardFor(Playlist p) => new() { Kind = CollectionKind.Playlist, Key = p.Id.ToString(), NumericId = p.Id, Title = p.Title, Subtitle = p.Description, ArtworkPath = p.ArtworkPath, ParentId = p.FolderId };
    public CollectionCard CardFor(Category c) => new() { Kind = CollectionKind.Category, Key = c.Id.ToString(), NumericId = c.Id, Title = c.Title, Subtitle = c.Description, ArtworkPath = c.ArtworkPath };
    public CollectionCard CardFor(PlaylistFolder f) => new() { Kind = CollectionKind.PlaylistFolder, Key = f.Id.ToString(), NumericId = f.Id, Title = f.Title, ParentId = f.ParentId };

    async Task PlaySongAsync(Song? song)
    {
        if (song is null) return;
        var source = CurrentPage == PageKind.Search && SearchResults.Count > 0 ? SearchResults : Songs;
        var index = Math.Max(0, source.IndexOf(song));
        await host.Playback.PlayQueueAsync(source, index);
        IsNowPlayingOpen = true;
    }

    async Task ToggleFavoriteAsync(Song? song)
    {
        if (song is null) return;
        var value = !FavoriteIds.Contains(song.Id);
        await db.SetFavoriteAsync(song.Id, value);
        if (value) FavoriteIds.Add(song.Id); else FavoriteIds.Remove(song.Id);
        Replace(FavoriteSongs, Songs.Where(x => FavoriteIds.Contains(x.Id)).Take(300));
        await RefreshDerivedAsync();
        Raise(nameof(FavoriteIds));
    }

    async Task RescanAsync()
    {
        IsBusy = true; Status = "Scanning library…";
        try { await host.Library.ScanAsync(lifetime.Token); await LoadLibraryAsync(); await RefreshDerivedAsync(); Status = $"Library updated: {Songs.Count:N0} tracks"; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = "Scan failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    async Task AddMusicFolderAsync()
    {
        var folder = host.Dialogs.PickFolder("Select a music source folder");
        if (folder is null) return;
        await host.Settings.AddIncludedFolderAsync(folder);
        await RescanAsync();
    }

    async Task ExcludeFolderAsync()
    {
        var folder = host.Dialogs.PickFolder("Select a folder to exclude");
        if (folder is null) return;
        await host.Settings.AddExcludedFolderAsync(folder);
        await RescanAsync();
    }

    async Task AnalyzeLibraryAsync()
    {
        analysisCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        IsBusy = true;
        try
        {
            await host.Analysis.AnalyzeLibraryAsync(Songs, analysisCts.Token);
            await RefreshDerivedAsync();
            Status = "Library analysis complete";
        }
        catch (OperationCanceledException) { Status = "Analysis cancelled"; }
        catch (Exception ex) { Status = "Analysis failed: " + ex.Message; }
        finally { analysisCts.Dispose(); analysisCts = null; IsBusy = false; }
    }

    async Task CreatePlaylistAsync()
    {
        var name = host.Dialogs.Prompt("New playlist", "Playlist name");
        if (string.IsNullOrWhiteSpace(name)) return;
        await db.CreatePlaylistAsync(name);
        await LoadLibraryAsync();
    }

    async Task CreateCategoryAsync()
    {
        var name = host.Dialogs.Prompt("New category", "Category name");
        if (string.IsNullOrWhiteSpace(name)) return;
        await db.CreateCategoryAsync(name);
        await LoadLibraryAsync();
    }

    async Task CreateFolderAsync()
    {
        var name = host.Dialogs.Prompt("New playlist folder", "Folder name");
        if (string.IsNullOrWhiteSpace(name)) return;
        await db.CreateFolderAsync(name);
        await LoadLibraryAsync();
    }

    async Task OpenCollectionAsync(CollectionCard? card)
    {
        if (card is null) return;
        CollectionDetail.Collection = card;
        CollectionDetail.Title = card.Title;
        CollectionDetail.Subtitle = card.Subtitle;
        IEnumerable<Song> songs = card.Kind switch
        {
            CollectionKind.Playlist when card.NumericId is long id => await db.GetPlaylistSongsAsync(id),
            CollectionKind.Category when card.NumericId is long id => await db.GetCategorySongsAsync(id),
            CollectionKind.Album => Songs.Where(x => x.Album.Equals(card.Key, StringComparison.OrdinalIgnoreCase)),
            CollectionKind.Artist => Songs.Where(x => x.Artist.Equals(card.Key, StringComparison.OrdinalIgnoreCase)),
            CollectionKind.Genre => Songs.Where(x => x.Genre.Equals(card.Key, StringComparison.OrdinalIgnoreCase)),
            CollectionKind.Folder => Songs.Where(x => string.Equals(Path.GetDirectoryName(x.Path), card.Key, StringComparison.OrdinalIgnoreCase)),
            CollectionKind.OfflineBackup => OfflineBackupSongs,
            _ => []
        };
        Replace(CollectionDetail.Songs, songs);
        if (card.Kind == CollectionKind.Playlist && card.NumericId is long pid)
        {
            var pref = await db.GetPlaylistPreferenceAsync(pid);
            CollectionDetail.SortMode = pref.SortMode;
            CollectionDetail.Ascending = pref.Ascending;
            CollectionDetail.ViewMode = pref.ViewMode;
        }
        else { CollectionDetail.SortMode = "title"; CollectionDetail.Ascending = true; CollectionDetail.ViewMode = Settings.LibraryViewMode == LibraryViewMode.Grid ? "grid" : "list"; }
        ApplyCollectionFilterSort();
        IsCollectionOpen = true;
    }

    void CollectionDetailChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CollectionDetailViewModel.SearchText) or nameof(CollectionDetailViewModel.SortMode) or nameof(CollectionDetailViewModel.Ascending))
            ApplyCollectionFilterSort();
        if (e.PropertyName == nameof(CollectionDetailViewModel.ViewMode) && CollectionDetail.Collection?.Kind == CollectionKind.Playlist && CollectionDetail.Collection.NumericId is long id)
            _ = db.SavePlaylistPreferenceAsync(id, CollectionDetail.SortMode, CollectionDetail.Ascending, CollectionDetail.ViewMode);
    }

    void ApplyCollectionFilterSort()
    {
        IEnumerable<Song> q = CollectionDetail.Songs;
        var search = CollectionDetail.SearchText.Trim();
        if (search.Length > 0) q = q.Where(x => SearchMatch(x, search));
        q = CollectionDetail.SortMode switch
        {
            "artist" => CollectionDetail.Ascending ? q.OrderBy(x => x.Artist).ThenBy(x => x.Title) : q.OrderByDescending(x => x.Artist).ThenByDescending(x => x.Title),
            "album" => CollectionDetail.Ascending ? q.OrderBy(x => x.Album).ThenBy(x => x.TrackNumber) : q.OrderByDescending(x => x.Album).ThenByDescending(x => x.TrackNumber),
            "date" => CollectionDetail.Ascending ? q.OrderBy(x => x.DateAdded) : q.OrderByDescending(x => x.DateAdded),
            "duration" => CollectionDetail.Ascending ? q.OrderBy(x => x.DurationMs) : q.OrderByDescending(x => x.DurationMs),
            "custom" => q,
            _ => CollectionDetail.Ascending ? q.OrderBy(x => x.Title) : q.OrderByDescending(x => x.Title)
        };
        Replace(CollectionDetail.VisibleSongs, q);
        if (CollectionDetail.Collection?.Kind == CollectionKind.Playlist && CollectionDetail.Collection.NumericId is long id)
            _ = db.SavePlaylistPreferenceAsync(id, CollectionDetail.SortMode, CollectionDetail.Ascending, CollectionDetail.ViewMode);
    }

    async Task TogglePinAsync(CollectionCard? card)
    {
        if (card is null) return;
        var type = TypeKey(card.Kind);
        var pins = await db.GetPinsAsync();
        var pinned = pins.Any(x => x.Type == type && x.Key == card.Key);
        await db.SetPinnedAsync(type, card.Key, !pinned);
        await RefreshPinsAsync();
    }

    async Task ToggleCollectionFavoriteAsync(CollectionCard? card)
    {
        if (card is null) return;
        var type = TypeKey(card.Kind);
        var all = await db.GetFavoriteCollectionsAsync();
        var value = !all.Contains((type, card.Key));
        await db.SetFavoriteCollectionAsync(type, card.Key, value);
        card.IsFavorite = value;
        await RefreshPinsAsync();
    }

    static string TypeKey(CollectionKind kind) => kind switch
    {
        CollectionKind.Playlist => "playlist", CollectionKind.Category => "category", CollectionKind.Album => "album",
        CollectionKind.Artist => "artist", CollectionKind.Genre => "genre", CollectionKind.Folder => "folder", _ => kind.ToString().ToLowerInvariant()
    };

    async Task FeedbackAsync(Song? song, int delta, bool dismissed)
    {
        if (song is null) return;
        await host.Recommendations.FeedbackAsync(song.Id, delta, dismissed);
        await RefreshDerivedAsync();
    }

    async Task HideSongAsync(Song? song)
    {
        if (song is null) return;
        await db.SetHiddenAsync(song.Id, true);
        await LoadLibraryAsync();
        await RefreshDerivedAsync();
    }

    async Task UnhideSongAsync(Song? song)
    {
        if (song is null) return;
        await db.SetHiddenAsync(song.Id, false);
        await OpenHiddenAsync();
        await LoadLibraryAsync();
    }

    async Task OpenHiddenAsync()
    {
        var ids = (await db.GetHiddenSongIdsAsync()).ToHashSet();
        Replace(HiddenSongs, (await db.GetSongsAsync(true)).Where(x => ids.Contains(x.Id)));
        IsHiddenOpen = true;
    }

    async Task EditMetadataAsync(Song? song)
    {
        if (song is null) return;
        var result = host.Dialogs.EditMetadata(song);
        if (result is null) return;
        await db.SetMetadataOverrideAsync(song.Id, result.Title, result.Artist, result.Album, result.Genre, result.Year);
        await LoadLibraryAsync();
        await RefreshDerivedAsync();
    }

    async Task RecycleFileAsync(Song? song)
    {
        if (song is null || !host.Dialogs.Confirm($"Move '{song.Title}' to the Recycle Bin?\n\nThis affects the source music file.")) return;
        try { host.Files.Recycle(song); await RescanAsync(); }
        catch (Exception ex) { host.Dialogs.Info(ex.Message, "Could not delete file"); }
    }

    async Task AddToPlaylistAsync(Song? song)
    {
        if (song is null) return;
        var options = Playlists.Select(x => x.Title).ToArray();
        var choice = host.Dialogs.PickOption("Add to playlist", "Choose a playlist", options);
        if (choice is null || choice < 0 || choice >= Playlists.Count) return;
        await db.AddToPlaylistAsync(Playlists[choice.Value].Id, song.Id);
    }

    async Task AddToCategoryAsync(Song? song)
    {
        if (song is null) return;
        var options = Categories.Select(x => x.Title).ToArray();
        var choice = host.Dialogs.PickOption("Add to category", "Choose a category", options);
        if (choice is null || choice < 0 || choice >= Categories.Count) return;
        await db.AddToCategoryAsync(Categories[choice.Value].Id, song.Id);
    }

    async Task RenameCollectionAsync(CollectionCard? card)
    {
        if (card?.NumericId is not long id) return;
        var name = host.Dialogs.Prompt("Rename", "New name", card.Title); if (string.IsNullOrWhiteSpace(name)) return;
        switch (card.Kind)
        {
            case CollectionKind.Playlist: await db.RenamePlaylistAsync(id, name); break;
            case CollectionKind.Category: await db.RenameCategoryAsync(id, name); break;
            case CollectionKind.PlaylistFolder: await db.RenameFolderAsync(id, name); break;
            default: return;
        }
        await LoadLibraryAsync(); await RefreshPinsAsync();
        if (CollectionDetail.Collection?.Kind == card.Kind && CollectionDetail.Collection.NumericId == id) CollectionDetail.Title = name;
    }

    async Task DeleteCollectionAsync(CollectionCard? card)
    {
        if (card?.NumericId is not long id) return;
        if (!host.Dialogs.Confirm($"Delete '{card.Title}' from NEO? Music files are never deleted by this action.")) return;
        switch (card.Kind)
        {
            case CollectionKind.Playlist: await db.DeletePlaylistAsync(id); break;
            case CollectionKind.Category: await db.DeleteCategoryAsync(id); break;
            case CollectionKind.PlaylistFolder: await db.DeleteFolderAsync(id); break;
            default: return;
        }
        if (IsCollectionOpen && CollectionDetail.Collection?.NumericId == id) IsCollectionOpen = false;
        await LoadLibraryAsync(); await RefreshPinsAsync();
    }

    async Task MovePlaylistAsync(CollectionCard? card)
    {
        if (card?.Kind != CollectionKind.Playlist || card.NumericId is not long id) return;
        var options = new List<string> { "No folder (root)" }; options.AddRange(PlaylistFolders.Select(x => x.Title));
        var selected = host.Dialogs.PickOption("Move playlist", "Choose destination folder", options); if (selected is null) return;
        long? folderId = selected == 0 ? null : PlaylistFolders[selected.Value - 1].Id;
        await db.MovePlaylistAsync(id, folderId); await LoadLibraryAsync();
    }

    async Task MoveFolderAsync(CollectionCard? card)
    {
        if (card?.Kind != CollectionKind.PlaylistFolder || card.NumericId is not long id) return;
        var candidates = PlaylistFolders.Where(x => x.Id != id).ToList();
        var options = new List<string> { "Root" }; options.AddRange(candidates.Select(x => x.Title));
        var selected = host.Dialogs.PickOption("Move folder", "Choose parent folder", options); if (selected is null) return;
        long? parent = selected == 0 ? null : candidates[selected.Value - 1].Id;
        try { await db.MoveFolderAsync(id, parent); await LoadLibraryAsync(); }
        catch (InvalidOperationException ex) { host.Dialogs.Info(ex.Message, "Invalid folder move"); }
    }

    async Task NewChildFolderAsync(CollectionCard? card)
    {
        if (card?.Kind != CollectionKind.PlaylistFolder || card.NumericId is not long id) return;
        var name = host.Dialogs.Prompt("New nested folder", "Folder name"); if (string.IsNullOrWhiteSpace(name)) return;
        await db.CreateFolderAsync(name, id); await LoadLibraryAsync();
    }

    async Task MovePlaylistOrderAsync(CollectionCard? card, int delta)
    {
        if (card?.Kind != CollectionKind.Playlist || card.NumericId is not long id) return;
        var list = Playlists.Select(x => x.Id).ToList(); var index = list.IndexOf(id); var target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]); await db.ReorderPlaylistsAsync(list); await LoadLibraryAsync();
    }

    async Task MovePinAsync(CollectionCard? card, int delta)
    {
        if (card is null) return;
        var list = PinnedCards.Select(x => (TypeKey(x.Kind), x.Key)).ToList();
        var index = list.FindIndex(x => x.Item1 == TypeKey(card.Kind) && x.Item2 == card.Key); var target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]); await db.ReorderPinsAsync(list.Select(x => (Type:x.Item1, Key:x.Item2)).ToList()); await RefreshPinsAsync();
    }

    async Task SetCollectionArtworkAsync(CollectionCard? card)
    {
        if (card?.NumericId is not long id || card.Kind is not (CollectionKind.Playlist or CollectionKind.Category)) return;
        var file = host.Dialogs.OpenFile("Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp|All files|*.*", "Choose collection artwork"); if (file is null) return;
        var ext = Path.GetExtension(file); var destination = Path.Combine(AppPaths.Assets, $"collection-{card.Kind.ToString().ToLowerInvariant()}-{id}{ext}"); File.Copy(file, destination, true);
        if (card.Kind == CollectionKind.Playlist)
        {
            var p = Playlists.First(x => x.Id == id); await db.UpdatePlaylistAsync(id, p.Title, p.Description, destination);
        }
        else
        {
            var c = Categories.First(x => x.Id == id); await db.UpdateCategoryAsync(id, c.Title, c.Description, destination);
        }
        await LoadLibraryAsync();
    }

    async Task RemoveFromCurrentCollectionAsync(Song? song)
    {
        if (song is null || CollectionDetail.Collection?.NumericId is not long id) return;
        if (CollectionDetail.Collection.Kind == CollectionKind.Playlist) await db.RemoveFromPlaylistAsync(id, song.Id);
        else if (CollectionDetail.Collection.Kind == CollectionKind.Category) await db.RemoveFromCategoryAsync(id, song.Id);
        else return;
        await OpenCollectionAsync(CollectionDetail.Collection);
    }

    async Task ResetMetadataAsync(Song? song)
    {
        if (song is null) return; await db.ClearMetadataOverrideAsync(song.Id); await LoadLibraryAsync(); await RefreshDerivedAsync();
    }

    async Task RemoveIncludedFolderAsync(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return; await host.Settings.RemoveIncludedFolderAsync(folder); await RescanAsync();
    }
    async Task RemoveExcludedFolderAsync(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return; await host.Settings.RemoveExcludedFolderAsync(folder); await RescanAsync();
    }

    async Task ImportM3uAsync()
    {
        var file = host.Dialogs.OpenFile("M3U playlists|*.m3u;*.m3u8|All files|*.*", "Import playlist");
        if (file is null) return;
        await host.Playlists.ImportM3u8Async(file);
        await LoadLibraryAsync();
    }

    async Task ExportM3uAsync()
    {
        if (Playlists.Count == 0) return;
        var idText = host.Dialogs.Prompt("Export playlist", "Enter playlist ID:\n" + string.Join("\n", Playlists.Take(15).Select(x => $"{x.Id}: {x.Title}")));
        if (!long.TryParse(idText?.Split(':')[0], out var id)) return;
        var playlist = Playlists.FirstOrDefault(x => x.Id == id); if (playlist is null) return;
        var path = host.Dialogs.SaveFile("M3U8 playlist|*.m3u8", ".m3u8", playlist.Title + ".m3u8"); if (path is null) return;
        await host.Playlists.ExportM3u8Async(playlist, path);
    }

    async Task CreateBackupAsync()
    {
        var path = host.Dialogs.SaveFile("NEO backup|*.neobackup", ".neobackup", $"NEO-{DateTime.Now:yyyyMMdd-HHmmss}.neobackup");
        if (path is null) return;
        IsBusy = true;
        try { await host.Backup.CreateAsync(path, lifetime.Token); Status = "Backup created"; }
        finally { IsBusy = false; }
    }

    async Task RestoreBackupAsync()
    {
        var path = host.Dialogs.OpenFile("NEO backup|*.neobackup", "Restore backup");
        if (path is null || !host.Dialogs.Confirm("Restore this NEO backup? Current NEO database/settings will be replaced.")) return;
        IsBusy = true;
        try { await host.Backup.RestoreAsync(path, lifetime.Token); await LoadLibraryAsync(); await RefreshDerivedAsync(); await host.Theme.ApplyAsync(Playback.Current); Status = "Backup restored"; }
        finally { IsBusy = false; }
    }

    async Task ClearCacheAsync()
    {
        await host.Cache.ClearAsync();
        CacheSize = host.Cache.Size();
        Status = "Regeneratable cache cleared";
    }

    async Task RefreshOfflineBackupAsync()
    {
        IsBusy = true;
        try { Replace(OfflineBackupSongs, await host.OfflineBackup.RefreshAsync(Songs, FavoriteIds)); Status = $"Offline set refreshed: {OfflineBackupSongs.Count} tracks"; }
        finally { IsBusy = false; }
    }

    async Task SetLibraryViewAsync(LibraryViewMode mode)
    {
        Settings.LibraryViewMode = mode;
        await host.Settings.SaveAsync();
        Raise(nameof(Settings));
    }

    async Task ApplyEqAsync()
    {
        var song = Playback.Current; if (song is null) return;
        var profile = new EqProfile(song.Id, EqPreset, EqBass, EqVirtualizer, EqLoudness, EqBands.Select(x => x.Gain).ToArray());
        await host.Playback.ApplyEqAsync(profile);
        Status = "Per-track EQ saved";
    }

    async Task ApplyEqPresetAsync(string? preset)
    {
        preset ??= "Normal";
        EqPreset = preset;
        var values = preset switch
        {
            "Bass Boost" => new float[] { 6, 5, 4, 2, 0, -1, -1, 0, 1, 1 },
            "Vocal" => new float[] { -2, -1, 0, 2, 4, 5, 4, 2, 0, -1 },
            "Rock" => new float[] { 4, 3, 1, -1, -2, 1, 3, 4, 4, 3 },
            "Electronic" => new float[] { 5, 4, 1, 0, -1, 1, 2, 4, 5, 4 },
            "Classical" => new float[] { 0, 0, -1, -1, 0, 2, 3, 3, 2, 1 },
            "Treble" => new float[] { -2, -2, -1, 0, 1, 2, 4, 5, 6, 6 },
            "Warm" => new float[] { 3, 3, 2, 1, 0, 0, -1, -2, -2, -2 },
            _ => new float[10]
        };
        for (var i = 0; i < EqBands.Count; i++) EqBands[i].Gain = values[i];
        await ApplyEqAsync();
    }

    async Task LoadEqAsync(Song song)
    {
        var eq = await db.GetEqAsync(song.Id) ?? new EqProfile(song.Id, "Normal", 0, 0, 0, new float[10]);
        EqPreset = eq.Preset; EqBass = eq.Bass; EqVirtualizer = eq.Virtualizer; EqLoudness = eq.LoudnessDb;
        for (var i = 0; i < EqBands.Count; i++) EqBands[i].Gain = i < eq.Bands.Length ? eq.Bands[i] : 0;
    }

    async Task ImportLyricsAsync()
    {
        var song = Playback.Current; if (song is null) return;
        var file = host.Dialogs.OpenFile("Lyrics|*.lrc;*.txt|All files|*.*", "Import lyrics"); if (file is null) return;
        await host.Lyrics.ImportAsync(song, file, lifetime.Token); await LoadLyricsAsync(song);
    }

    void OpenLyricsEditor()
    {
        if (Playback.Current is null) return;
        BuildLyricsDraft(); IsLyricsEditorOpen = true;
    }

    void BuildLyricsDraft()
    {
        LyricDraftLines.Clear();
        if (CurrentLyrics is null) { LyricDraftLines.Add(new LyricDraftLine { Text = "" }); return; }
        if (CurrentLyrics.Synchronized)
            foreach (var l in host.Lyrics.Parse(CurrentLyrics.Original)) LyricDraftLines.Add(new LyricDraftLine { TimeMs = l.TimeMs, Text = l.Text });
        else foreach (var line in CurrentLyrics.Original.Replace("\r", "").Split('\n')) LyricDraftLines.Add(new LyricDraftLine { Text = line });
        LyricsTranslation = CurrentLyrics.Translation; LyricsRomanization = CurrentLyrics.Romanization;
        SelectedLyricDraft = LyricDraftLines.FirstOrDefault();
    }

    void StampSelectedLyric()
    {
        if (SelectedLyricDraft is null) return;
        SelectedLyricDraft.TimeMs = Playback.PositionMs;
        var i = LyricDraftLines.IndexOf(SelectedLyricDraft);
        if (i >= 0 && i + 1 < LyricDraftLines.Count) SelectedLyricDraft = LyricDraftLines[i + 1];
    }

    async Task SaveAuthoredLyricsAsync()
    {
        var song = Playback.Current; if (song is null) return;
        await host.Lyrics.SaveAuthoredAsync(song, LyricDraftLines.Select(x => new LrcLine(x.TimeMs, x.Text)), LyricsTranslation, LyricsRomanization);
        IsLyricsEditorOpen = false; await LoadLyricsAsync(song);
    }

    async Task LoadSpeechModelsAsync()
    {
        Replace(SpeechModels, await host.Speech.ModelsAsync());
        var pref = await db.GetSpeechPreferenceAsync();
        var selected = SpeechModels.FirstOrDefault(x => x.Id == pref.ModelId) ?? SpeechModels.FirstOrDefault(x => x.Language == pref.Language) ?? SpeechModels.FirstOrDefault();
        selectedSpeechModelId = selected?.Id ?? ""; Raise(nameof(SelectedSpeechModelId));
    }

    async Task SaveSpeechPreferenceAsync()
    {
        var model = SpeechModels.FirstOrDefault(x => x.Id == SelectedSpeechModelId);
        if (model is not null) await db.SaveSpeechPreferenceAsync(model.Id, model.Language);
    }

    async Task ImportSpeechModelAsync()
    {
        var folder = host.Dialogs.PickFolder("Select an extracted Vosk model folder"); if (folder is null) return;
        var language = host.Dialogs.Prompt("Speech model", "Language code (en/fa)", "en"); if (string.IsNullOrWhiteSpace(language)) return;
        var name = host.Dialogs.Prompt("Speech model", "Display name", Path.GetFileName(folder)); if (string.IsNullOrWhiteSpace(name)) return;
        IsBusy = true;
        try { var imported = await host.Speech.ImportModelAsync(folder, language, name, lifetime.Token); await LoadSpeechModelsAsync(); SelectedSpeechModelId = imported.Id; Status = "Offline speech model imported"; }
        finally { IsBusy = false; }
    }

    async Task TranscribeAsync()
    {
        var song = Playback.Current; if (song is null) return;
        var models = await host.Speech.ModelsAsync(); if (models.Count == 0) { host.Dialogs.Info("Import a Vosk model first."); return; }
        var preferred = models.FirstOrDefault(x => x.Id == SelectedSpeechModelId) ?? models.FirstOrDefault(x => x.Language.Equals(Settings.Language, StringComparison.OrdinalIgnoreCase)) ?? models[0];
        IsBusy = true; try { await host.Speech.TranscribeAsync(song, preferred.Id, lifetime.Token); await LoadLyricsAsync(song); Status = "Offline transcription complete"; } finally { IsBusy = false; }
    }

    async Task ForceAlignAsync()
    {
        var song = Playback.Current; if (song is null) return;
        var plain = host.Dialogs.Prompt("Force align lyrics", "Paste/plain lyrics (use \\n for line breaks)", CurrentLyrics?.Synchronized == false ? CurrentLyrics.Original : "");
        if (string.IsNullOrWhiteSpace(plain)) return;
        plain = plain.Replace("\\n", "\n");
        var models = await host.Speech.ModelsAsync(); if (models.Count == 0) { host.Dialogs.Info("Import a Vosk model first."); return; }
        var model = models.FirstOrDefault(x => x.Id == SelectedSpeechModelId) ?? models[0];
        IsBusy = true; try { await host.Speech.ForceAlignAsync(song, plain, model.Id, lifetime.Token); await LoadLyricsAsync(song); Status = "Lyrics aligned"; } finally { IsBusy = false; }
    }

    async Task LoadLyricsAsync(Song song)
    {
        if (Settings.LyricsMode.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            CurrentLyrics = null; BuildLyricsDraft(); return;
        }
        CurrentLyrics = await host.Lyrics.LoadAsync(song, lifetime.Token);
        if (CurrentLyrics is null && !Settings.StrictOfflineMode &&
            (Settings.LyricsMode.Equals("auto", StringComparison.OrdinalIgnoreCase) || Settings.LyricsMode.Equals("provider", StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate(Settings.LyricsProviderEndpoint, UriKind.Absolute, out var endpoint))
        {
            try { CurrentLyrics = await host.Lyrics.FetchConfiguredProviderAsync(song, endpoint.ToString(), Settings.StrictOfflineMode, lifetime.Token); }
            catch { /* provider is optional; local playback must remain unaffected */ }
        }
        BuildLyricsDraft();
    }

    async Task PickCanvasAsync()
    {
        var song = Playback.Current; if (song is null) return;
        var file = host.Dialogs.OpenFile("Video|*.mp4;*.webm;*.mkv;*.mov|All files|*.*", "Select Canvas video"); if (file is null) return;
        TrackPlus.CanvasPath = await host.TrackExperience.ImportAssetAsync(song.Id, file, "canvas"); TrackPlus.CanvasEnabled = true;
    }

    async Task PickBackgroundAsync()
    {
        var song = Playback.Current; if (song is null) return;
        var file = host.Dialogs.OpenFile("Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp|All files|*.*", "Select track wallpaper"); if (file is null) return;
        TrackPlus.BackgroundImagePath = await host.TrackExperience.ImportAssetAsync(song.Id, file, "background");
    }

    async Task PickArtworkAsync()
    {
        var song = Playback.Current; if (song is null) return;
        var file = host.Dialogs.OpenFile("Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp|All files|*.*", "Select custom artwork"); if (file is null) return;
        await host.TrackExperience.SetArtworkAsync(song, file); await LoadLibraryAsync(); await UpdateCurrentAsync();
    }

    async Task LoadTrackPlusAsync(Song song)
    {
        var v = await host.TrackExperience.LoadAsync(song.Id) ?? new TrackVisualProfile(song.Id, null, false, "crop", 0, 0, 1, "inherit", 0, 0, 0, "waveform", 1, 1, "", "overlay", .28f, 18, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        TrackPlus.CanvasPath = v.CanvasPath; TrackPlus.CanvasEnabled = v.CanvasEnabled; TrackPlus.CanvasFit = v.CanvasFit;
        TrackPlus.CanvasStartMs = v.CanvasStartMs; TrackPlus.CanvasEndMs = v.CanvasEndMs; TrackPlus.CanvasPlaybackSpeed = v.CanvasPlaybackSpeed;
        TrackPlus.ThemeMode = v.ThemeMode; TrackPlus.AccentHex = ToHex(v.AccentArgb); TrackPlus.BackgroundHex = ToHex(v.BackgroundArgb); TrackPlus.SecondaryHex = ToHex(v.SecondaryArgb);
        TrackPlus.VisualizerMode = v.VisualizerMode; TrackPlus.VisualizerSensitivity = v.VisualizerSensitivity; TrackPlus.AnimationIntensity = v.AnimationIntensity;
        TrackPlus.BackgroundImagePath = v.BackgroundImagePath; TrackPlus.BackgroundMode = v.BackgroundMode; TrackPlus.BackgroundOpacity = v.BackgroundOpacity; TrackPlus.BackgroundBlurDp = v.BackgroundBlurDp;
    }

    async Task SaveTrackPlusAsync()
    {
        var song = Playback.Current; if (song is null) return;
        var v = new TrackVisualProfile(song.Id, TrackPlus.CanvasPath, TrackPlus.CanvasEnabled, TrackPlus.CanvasFit, TrackPlus.CanvasStartMs, TrackPlus.CanvasEndMs,
            TrackPlus.CanvasPlaybackSpeed, TrackPlus.ThemeMode, ParseArgb(TrackPlus.AccentHex), ParseArgb(TrackPlus.BackgroundHex), ParseArgb(TrackPlus.SecondaryHex),
            TrackPlus.VisualizerMode, TrackPlus.VisualizerSensitivity, TrackPlus.AnimationIntensity, TrackPlus.BackgroundImagePath, TrackPlus.BackgroundMode,
            TrackPlus.BackgroundOpacity, TrackPlus.BackgroundBlurDp, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await host.TrackExperience.SaveAsync(v); await host.Theme.ApplyAsync(song); Status = "Track+ profile saved";
    }

    async Task StartRadioAsync(Song? seed)
    {
        if (seed is null) return;
        var radio = await host.Recommendations.BuildRadioAsync(seed, Songs, FavoriteIds, 60);
        if (radio.Count == 0) return;
        await host.Playback.PlayQueueAsync(radio);
        IsNowPlayingOpen = true;
        Status = $"Smart Local Radio: {radio.Count} tracks";
    }

    async Task ShowSongDetailsAsync(Song? song)
    {
        if (song is null) return;
        var a = await db.GetAdvancedAnalysisAsync(song.Id);
        var rg = await db.GetReplayGainAsync(song.Id);
        var lines = new List<string>
        {
            $"Title: {song.Title}", $"Artist: {song.Artist}", $"Album: {song.Album}", $"Album artist: {song.AlbumArtist}",
            $"Genre: {song.Genre}", $"Year: {song.Year}", $"Composer: {song.Composer}", $"Duration: {song.DurationText}",
            $"Bitrate: {(song.Bitrate > 0 ? song.Bitrate / 1000 + " kbps" : "Unknown")}", $"Format: {song.MimeType}", $"Size: {FormatBytes(song.SizeBytes)}",
            $"Path: {song.Path}"
        };
        if (a is not null) lines.AddRange([$"BPM: {a.Bpm:0.##}", $"Key: {a.MusicalKey} / {a.CamelotKey}", $"Energy: {a.Energy:0.00}", $"Mood: {a.Mood}", $"Dynamic range: {a.DynamicRangeDb:0.0} dB"]);
        if (rg?.PreferredGainDb is float gain) lines.Add($"ReplayGain: {gain:+0.00;-0.00;0.00} dB ({rg.Source})");
        host.Dialogs.Info(string.Join(Environment.NewLine, lines), "Song details");
    }

    async Task ClearArtworkAsync(Song? song)
    {
        if (song is null) return;
        await db.SetSongArtworkAsync(song.Id, "");
        await LoadLibraryAsync();
        if (Playback.Current?.Id == song.Id) await UpdateCurrentAsync();
    }

    async Task UpdateCurrentAsync()
    {
        var song = Playback.Current;
        await host.Theme.ApplyAsync(song);
        CurrentArtwork = song is null ? null : await host.Artwork.ResolveAsync(song, lifetime.Token);
        if (song is null) { CurrentLyrics = null; WaveformPoints.Clear(); return; }
        await LoadLyricsAsync(song); await LoadEqAsync(song); await LoadTrackPlusAsync(song);
        try { Replace(WaveformPoints, (await host.Waveform.GetAsync(song, 320, lifetime.Token)).Select(x => (double)x)); } catch { WaveformPoints.Clear(); }
    }

    void PlaybackChanged(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(async () =>
        {
            Raise(nameof(Playback)); Raise(nameof(PositionText)); Raise(nameof(DurationText)); Raise(nameof(SeekMaximum)); Raise(nameof(SeekValue)); Raise(nameof(PlayPauseGlyph)); Raise(nameof(RepeatText)); Raise(nameof(ShuffleGlyph));
            UpdateActiveLyric();
            await UpdateCurrentIfSongChangedAsync();
        });
    }

    long lastCurrentId = long.MinValue;
    async Task UpdateCurrentIfSongChangedAsync()
    {
        var id = Playback.Current?.Id ?? -1;
        if (id == lastCurrentId) return;
        lastCurrentId = id;
        await UpdateCurrentAsync();
    }

    void UpdateActiveLyric()
    {
        if (LyricDraftLines.Count == 0) return;
        var p = Playback.PositionMs;
        var active = -1;
        for (var i = 0; i < LyricDraftLines.Count; i++) if (LyricDraftLines[i].TimeMs <= p) active = i; else break;
        for (var i = 0; i < LyricDraftLines.Count; i++) LyricDraftLines[i].IsActive = i == active;
    }

    public async Task CommitSeekAsync(double value)
    {
        IsSeeking = false;
        await host.Playback.SeekAsync((long)Math.Clamp(value, 0, Playback.DurationMs));
    }

    public Task SetPlaybackSpeedAsync(double value) => host.Playback.SetSpeedAsync((float)Math.Clamp(value, .25, 3));
    public void SetPlaybackVolume(double value) => host.Playback.SetVolume(Math.Clamp(value, 0, 1));

    public async Task MoveQueueAsync(int from, int to) => await host.Playback.MoveQueueAsync(from, to);
    public async Task ReorderCollectionAsync(int from, int to)
    {
        if (from < 0 || to < 0 || from >= CollectionDetail.VisibleSongs.Count || to >= CollectionDetail.VisibleSongs.Count || from == to) return;
        var item = CollectionDetail.VisibleSongs[from]; CollectionDetail.VisibleSongs.RemoveAt(from); CollectionDetail.VisibleSongs.Insert(to, item);
        if (CollectionDetail.Collection?.Kind == CollectionKind.Playlist && CollectionDetail.Collection.NumericId is long pid && CollectionDetail.SortMode == "custom")
            await db.ReorderPlaylistAsync(pid, CollectionDetail.VisibleSongs.Select(x => x.Id).ToArray());
        if (CollectionDetail.Collection?.Kind == CollectionKind.Category && CollectionDetail.Collection.NumericId is long cid)
            await db.ReorderCategoryAsync(cid, CollectionDetail.VisibleSongs.Select(x => x.Id).ToArray());
    }

    void LibraryChanged(object? s, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(async () => { await LoadLibraryAsync(); await RefreshDerivedAsync(); });
    void SettingsChanged(object? s, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(async () => { host.Windows.ApplyStartup(Settings.StartWithWindows); await host.Theme.ApplyAsync(Playback.Current); Raise(nameof(Settings)); Raise(nameof(FlowDirection)); });
    void LocalizationChanged(object? s, PropertyChangedEventArgs e) => Application.Current.Dispatcher.BeginInvoke(() => { Raise(nameof(L)); Raise(nameof(FlowDirection)); });
    void AnalysisProgress(int current, int total, string track) => Application.Current.Dispatcher.BeginInvoke(() => { AnalysisCurrent = current; AnalysisTotal = total; AnalysisTrack = track; });
    void SpectrumFrame(float[] frame) => Application.Current.Dispatcher.BeginInvoke(() =>
    {
        var sens = Theme.VisualizerSensitivity;
        double sum = 0;
        for (var i = 0; i < SpectrumBars.Count && i < frame.Length; i++) { var v = Math.Clamp(frame[i] * sens, 0, 1); SpectrumBars[i] = v; sum += v; }
        VisualPulse = Math.Clamp((sum / Math.Max(1, Math.Min(SpectrumBars.Count, frame.Length))) * Theme.AnimationIntensity, 0, 1);
    });

    void DebounceSearch()
    {
        searchCts?.Cancel(); searchCts?.Dispose(); searchCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var token = searchCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(180, token);
                var q = SearchText.Trim();
                var results = q.Length == 0 ? new List<Song>() : Songs.Where(x => SearchMatch(x, q)).Take(300).ToList();
                var suggestions = BuildSuggestions(q).Take(12).ToList();
                await Application.Current.Dispatcher.InvokeAsync(() => { Replace(SearchResults, results); Replace(SearchSuggestions, suggestions); });
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    IEnumerable<SearchSuggestion> BuildSuggestions(string q)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var recent in Settings.RecentSearches.Where(x => q.Length == 0 || x.Contains(q, StringComparison.OrdinalIgnoreCase))) if (seen.Add(recent)) yield return new(recent, "Recent");
        if (q.Length == 0) yield break;
        foreach (var x in Songs.SelectMany(s => new[] { (s.Title, "Song"), (s.Artist, "Artist"), (s.Album, "Album"), (s.Genre, "Genre") }))
            if (!string.IsNullOrWhiteSpace(x.Item1) && x.Item1.Contains(q, StringComparison.OrdinalIgnoreCase) && seen.Add(x.Item1)) yield return new(x.Item1, x.Item2);
    }

    static bool SearchMatch(Song s, string q) => s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Artist.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Album.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Genre.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Composer.Contains(q, StringComparison.OrdinalIgnoreCase);

    public async Task ApplySearchSuggestionAsync(SearchSuggestion suggestion) { SearchText = suggestion.Text; await host.Settings.AddRecentSearchAsync(suggestion.Text); }
    public Task CommitSearchAsync() => SearchText.Trim().Length < 2 ? Task.CompletedTask : host.Settings.AddRecentSearchAsync(SearchText.Trim());

    static string Time(long ms) => TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(ms >= 3_600_000 ? @"h\:mm\:ss" : @"m\:ss");
    static string FormatBytes(long value) { string[] units = { "B", "KB", "MB", "GB" }; double n = value; var i = 0; while (n >= 1024 && i < units.Length - 1) { n /= 1024; i++; } return $"{n:0.##} {units[i]}"; }
    static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source) { target.Clear(); foreach (var x in source) target.Add(x); }
    static string ToHex(int argb) => argb == 0 ? "" : $"#{unchecked((uint)argb):X8}";
    static int ParseArgb(string? hex) { if (string.IsNullOrWhiteSpace(hex)) return 0; hex = hex.Trim().TrimStart('#'); if (hex.Length == 6) hex = "FF" + hex; return uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? unchecked((int)v) : 0; }
}
