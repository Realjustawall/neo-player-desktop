using System.Windows;
using System.Windows.Threading;
using NeoPlayer.Windows.Core;
using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Services;
using NeoPlayer.Windows.ViewModels;
using NeoPlayer.Windows.Views;

namespace NeoPlayer.Windows;

public partial class App : Application
{
    private AppHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (e.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            {
                var code = await SelfTestRunner.RunAsync();
                Shutdown(code); return;
            }

            _host = new AppHost();
            await _host.InitializeAsync();
            var vm = new MainViewModel(_host); await vm.InitializeAsync();

            if (e.Args.Contains("--ui-smoke", StringComparer.OrdinalIgnoreCase))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var smoke = new MainWindow { DataContext = vm, ShowInTaskbar = false };
                MainWindow = smoke;
                smoke.Show();
                await Dispatcher.InvokeAsync(smoke.UpdateLayout, DispatcherPriority.ApplicationIdle);

                var compact = new CompactPlayerWindow { Owner = smoke, DataContext = vm, ShowInTaskbar = false };
                compact.Show();
                await Dispatcher.InvokeAsync(compact.UpdateLayout, DispatcherPriority.ApplicationIdle);
                compact.Close();

                var advanced = new AdvancedToolsWindow { Owner = smoke, ShowInTaskbar = false };
                advanced.Show();
                await Dispatcher.InvokeAsync(advanced.UpdateLayout, DispatcherPriority.ApplicationIdle);
                advanced.Close();

                smoke.Close();
                Console.WriteLine("NEO_UI_SMOKE_OK");
                Shutdown(0); return;
            }

            var window = new MainWindow { DataContext = vm };
            MainWindow = window;
            vm.RestoreWindowPlacement(window);
            window.Show();
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "neo-player-startup-error.txt"), ex.ToString()); } catch { }
            if (!e.Args.Any(a => a.StartsWith("--", StringComparison.Ordinal))) MessageBox.Show(ex.ToString(), "NEO Player failed to start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}

public sealed class AppHost : IDisposable
{
    public static AppHost? Current { get; private set; }

    public SettingsService Settings { get; } = new();
    public NeoDatabase Database { get; }
    public LibraryCollectionsService Collections { get; }
    public PlaylistFolderManager PlaylistFolders { get; }
    public PlaylistTransferService PlaylistTransfer { get; }
    public LibraryScanner Scanner { get; }
    public PlaybackEngine Playback { get; }
    public AudioAnalysisService Analysis { get; }
    public SmartMixService SmartMix { get; }
    public BackupService Backup { get; } = new();
    public LocalizationService Localization { get; }
    public ThemeService Theme { get; }
    public WindowsIntegrationService Windows { get; } = new();
    public SleepTimerService SleepTimer { get; } = new();

    public AppHost()
    {
        Current = this;
        AppPaths.Ensure();
        Database = new NeoDatabase();
        Collections = new LibraryCollectionsService();
        PlaylistFolders = new PlaylistFolderManager();
        PlaylistTransfer = new PlaylistTransferService(Database, Collections);
        Scanner = new LibraryScanner(Database, Settings);
        Playback = new PlaybackEngine(Database, Settings);
        Analysis = new AudioAnalysisService(Database);
        SmartMix = new SmartMixService(Database);
        Localization = new LocalizationService(Settings);
        Theme = new ThemeService(Settings);
        SleepTimer.Elapsed += (_, _) => Playback.Pause();
    }

    public async Task InitializeAsync()
    {
        await Settings.LoadAsync();
        await Database.InitializeAsync();
        Localization.Apply();
        Theme.Apply();
        if (Settings.Value.StartWithWindows != Windows.IsStartupEnabled())
        {
            try { Windows.SetStartupEnabled(Settings.Value.StartWithWindows); } catch { }
        }
    }

    public void Dispose()
    {
        SleepTimer.Dispose();
        Playback.Dispose();
        if (ReferenceEquals(Current, this)) Current = null;
    }
}
