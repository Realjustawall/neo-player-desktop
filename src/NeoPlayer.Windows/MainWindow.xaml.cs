using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NeoPlayer.Windows.Models;
using NeoPlayer.Windows.Services;
using NeoPlayer.Windows.ViewModels;
using NeoPlayer.Windows.Views;

namespace NeoPlayer.Windows;

public partial class MainWindow : Window
{
    private readonly GlobalMediaKeyService _mediaKeys = new();
    private CompactPlayerWindow? _compact;
    private AdvancedToolsWindow? _advanced;

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;

        var menu = new ContextMenu();
        var advanced = new MenuItem { Header = "Advanced tools…\tCtrl+Shift+T" };
        advanced.Click += (_, _) => OpenAdvancedTools();
        menu.Items.Add(advanced);
        ContextMenu = menu;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            _mediaKeys.Attach(this);
            _mediaKeys.PlayPausePressed += (_, _) => Dispatcher.BeginInvoke(() => Vm?.PlayPauseCommand.Execute(null));
            _mediaKeys.NextPressed += (_, _) => Dispatcher.BeginInvoke(() => Vm?.NextCommand.Execute(null));
            _mediaKeys.PreviousPressed += (_, _) => Dispatcher.BeginInvoke(() => Vm?.PreviousCommand.Execute(null));
        }
        catch { }
    }

    private void SongList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: Song song } && Vm is not null) Vm.PlaySongCommand.Execute(song);
    }

    private void SeekSlider_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Vm?.CommitSeek();

    private void OpenCompact_Click(object sender, RoutedEventArgs e)
    {
        if (_compact is not null)
        {
            if (_compact.WindowState == WindowState.Minimized) _compact.WindowState = WindowState.Normal;
            _compact.Activate();
            return;
        }
        _compact = new CompactPlayerWindow { Owner = this, DataContext = DataContext };
        _compact.Closed += (_, _) => _compact = null;
        _compact.Show();
    }

    private void OpenAdvancedTools()
    {
        if (_advanced is not null)
        {
            if (_advanced.WindowState == WindowState.Minimized) _advanced.WindowState = WindowState.Normal;
            _advanced.Activate();
            return;
        }
        _advanced = new AdvancedToolsWindow { Owner = this };
        _advanced.Closed += (_, _) => _advanced = null;
        _advanced.Show();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && e.Key == Key.T)
        {
            OpenAdvancedTools(); e.Handled = true; return;
        }
        if (Vm is null || Keyboard.FocusedElement is TextBox) return;
        if (e.Key is Key.Space or Key.MediaPlayPause) { Vm.PlayPauseCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.MediaNextTrack || (e.Key == Key.Right && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))) { Vm.NextCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.MediaPreviousTrack || (e.Key == Key.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))) { Vm.PreviousCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.L && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Vm.LyricsPageCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Q && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Vm.QueuePageCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Vm.SearchPageCommand.Execute(null); e.Handled = true; }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _advanced?.Close();
        _compact?.Close();
        _mediaKeys.Dispose();
        if (Vm is not null) _ = Vm.SaveWindowPlacementAsync(this);
    }
}
