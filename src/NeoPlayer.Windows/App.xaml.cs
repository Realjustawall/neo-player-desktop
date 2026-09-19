using System.Windows;
using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Services;
using NeoPlayer.Windows.ViewModels;

namespace NeoPlayer.Windows;

public partial class App : Application
{
    private AppHost? _host;
    public static AppHost Host => ((App)Current)._host ?? throw new InvalidOperationException("App host not initialized");
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _host = new AppHost();
        await _host.InitializeAsync();
        var vm = new MainViewModel(_host);
        await vm.InitializeAsync();
        var window = new MainWindow { DataContext = vm };
        _host.Windows.Attach(window, vm);
        MainWindow = window;
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null) await _host.DisposeAsync();
        base.OnExit(e);
    }
}

public sealed class AppHost : IAsyncDisposable
{
    public NeoDatabase Database { get; } = new();
    public SettingsService Settings { get; }
    public ArtworkService Artwork { get; }
    public LibraryScanner Library { get; }
    public AudioAnalysisService Analysis { get; }
    public LyricsService Lyrics { get; }
    public SpeechModelService Speech { get; }
    public RecommendationService Recommendations { get; }
    public PlaybackEngine Playback { get; }
    public BackupService Backup { get; }
    public CacheService Cache { get; }
    public WindowsIntegrationService Windows { get; }
    public PlaylistTransferService Playlists { get; }
    public LocalizationService Localization { get; }
    public SleepTimerService SleepTimer { get; }
    public WaveformService Waveform { get; } = new();
    public FileOperationsService Files { get; } = new();
    public SmartMixService SmartMix { get; }
    public OfflineBackupEngine OfflineBackup { get; }
    public ThemeService Theme { get; }
    public SpectrumAnalyzerService Spectrum { get; }
    public TrackExperienceService TrackExperience { get; }
    public DialogService Dialogs { get; } = new();

    public AppHost()
    {
        Settings = new SettingsService();
        Artwork = new ArtworkService(Database);
        Library = new LibraryScanner(Database, Settings, Artwork);
        Analysis = new AudioAnalysisService(Database);
        Lyrics = new LyricsService(Database);
        Speech = new SpeechModelService(Database, Lyrics);
        Recommendations = new RecommendationService(Database);
        Playback = new PlaybackEngine(Database, Settings, Analysis);
        Backup = new BackupService(Database, Settings);
        Cache = new CacheService(Database);
        Windows = new WindowsIntegrationService(Playback, Artwork);
        Playlists = new PlaylistTransferService(Database);
        Localization = new LocalizationService(Settings);
        SleepTimer = new SleepTimerService(Playback);
        SmartMix = new SmartMixService(Database);
        OfflineBackup = new OfflineBackupEngine(Database, Recommendations, Settings);
        Theme = new ThemeService(Settings, Database, Artwork);
        Spectrum = new SpectrumAnalyzerService(Playback);
        TrackExperience = new TrackExperienceService(Database, Artwork);
    }

    public async Task InitializeAsync()
    {
        AppPaths.Ensure();
        await Database.InitializeAsync();
        await Settings.LoadAsync();
        Localization.Apply();
        await Speech.DiscoverBundledModelsAsync();
        await Playback.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        SleepTimer.Dispose();
        Spectrum.Dispose();
        Windows.Dispose();
        Library.Dispose();
        await Playback.DisposeAsync();
        await Settings.DisposeAsync();
        await Database.DisposeAsync();
    }
}
