using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NeoPlayer.Windows.Models;
using NeoPlayer.Windows.ViewModels;

namespace NeoPlayer.Windows;

public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); PreviewKeyDown += OnPreviewKeyDown; }

    private MainViewModel? Vm => DataContext as MainViewModel;
    private void SongList_DoubleClick(object sender, MouseButtonEventArgs e) { if (sender is ListBox { SelectedItem: Song song } && Vm is not null) Vm.PlaySongCommand.Execute(song); }
    private void SeekSlider_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Vm?.CommitSeek();
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is null || Keyboard.FocusedElement is TextBox) return;
        if (e.Key == Key.Space) { Vm.PlayPauseCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Right && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Vm.NextCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Vm.PreviousCommand.Execute(null); e.Handled = true; }
    }
}
