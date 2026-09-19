using System.Windows;
using NeoPlayer.Windows.Core;
using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Services;
using NeoPlayer.Windows.ViewModels;

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
                var smoke = new MainWindow { DataContext = vm };
                smoke.Measure(new Size(1000, 700)); smoke.Arrange(new Rect(0, 0, 1000, 700)); smoke.UpdateLayout(); smoke.Close();
                Shutdown(0); return;
            }

            var window = new MainWindow { DataContext = vm };
            MainWindow = window; window.Show();
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
    public SettingsService Settings { get; } = new();
    public NeoDatabase Database { get; }
    public LibraryScanner Scanner { get; }
    public PlaybackEngine Playback { get; }
    public AudioAnalysisService Analysis { get; }
    public SmartMixService SmartMix { get; }
    public BackupService Backup { get; } = new();
    public LocalizationService Localization { get; }
    public ThemeService Theme { get; }

    public AppHost()
    {
        AppPaths.Ensure();
        Database = new NeoDatabase();
        Scanner = new LibraryScanner(Database, Settings);
        Playback = new PlaybackEngine(Database, Settings);
        Analysis = new AudioAnalysisService(Database);
        SmartMix = new SmartMixService(Database);
        Localization = new LocalizationService(Settings);
        Theme = new ThemeService(Settings);
    }

    public async Task InitializeAsync()
    {
        await Settings.LoadAsync();
        await Database.InitializeAsync();
        Localization.Apply();
        Theme.Apply();
    }

    public void Dispose() => Playback.Dispose();
}
