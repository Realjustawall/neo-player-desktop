using NeoPlayer.Windows.Models;
using NeoPlayer.Windows.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NeoPlayer.Windows;

public partial class MainWindow : Window
{
    readonly DispatcherTimer uiTimer;
    Point queueDragStart;
    Point collectionDragStart;
    Point playerDragStart;
    CancellationTokenSource? speedDebounce;

    MainViewModel? Vm => DataContext as MainViewModel;

    public MainWindow()
    {
        InitializeComponent();
        uiTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        uiTimer.Tick += UiTimer_Tick;
        uiTimer.Start();
    }

    void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void MiniPlayer_Click(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.Playback.Current is not null) Vm.IsNowPlayingOpen = true;
    }

    void PlayerGestureArea_Down(object sender, MouseButtonEventArgs e) => playerDragStart = e.GetPosition(PlayerGestureArea);
    async void PlayerGestureArea_Up(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null) return;
        var delta = e.GetPosition(PlayerGestureArea) - playerDragStart;
        if (Math.Abs(delta.X) > Math.Abs(delta.Y) && Math.Abs(delta.X) > 75)
        {
            if (delta.X < 0) await App.Host.Playback.NextAsync(); else await App.Host.Playback.PreviousAsync();
            e.Handled = true;
        }
        else if (Math.Abs(delta.Y) > 75)
        {
            Vm.SetPlaybackVolume(Math.Clamp(Vm.Playback.Volume + (delta.Y < 0 ? .08 : -.08), 0, 1));
            e.Handled = true;
        }
    }

    async void SeekSlider_Up(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null) return;
        await Vm.CommitSeekAsync(SeekSlider.Value);
    }
    void SeekSlider_Down(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not null) Vm.IsSeeking = true;
    }

    void Create_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var menu = new ContextMenu();
        Add(menu, "New playlist", Vm.CreatePlaylistCommand);
        Add(menu, "New category", Vm.CreateCategoryCommand);
        Add(menu, "New playlist folder", Vm.CreateFolderCommand);
        menu.PlacementTarget = sender as UIElement;
        menu.IsOpen = true;
    }

    void SongMore_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not Button { Tag: Song song } button) return;
        var menu = new ContextMenu();
        Add(menu, "Play now", Vm.PlaySongCommand, song);
        Add(menu, "Play next", Vm.PlayNextCommand, song);
        Add(menu, "Add to queue", Vm.AddQueueCommand, song);
        menu.Items.Add(new Separator());
        Add(menu, Vm.FavoriteIds.Contains(song.Id) ? "Remove from liked songs" : "Like song", Vm.ToggleFavoriteCommand, song);
        Add(menu, "Add to playlist…", Vm.AddToPlaylistCommand, song);
        Add(menu, "Add to category…", Vm.AddToCategoryCommand, song);
        Add(menu, "Start Smart Local Radio", Vm.StartRadioCommand, song);
        Add(menu, "More like this", Vm.MoreLikeCommand, song);
        Add(menu, "Less like this", Vm.LessLikeCommand, song);
        menu.Items.Add(new Separator());
        Add(menu, "Edit library metadata…", Vm.EditMetadataCommand, song);
        Add(menu, "Reset metadata override", Vm.ResetMetadataCommand, song);
        Add(menu, "Song details", Vm.SongDetailsCommand, song);
        Add(menu, "Clear custom artwork", Vm.ClearArtworkCommand, song);
        if (Vm.IsCollectionOpen && Vm.CollectionDetail.Collection?.Kind is CollectionKind.Playlist or CollectionKind.Category)
            Add(menu, "Remove from this collection", Vm.RemoveFromCollectionCommand, song);
        Add(menu, "Reveal in Explorer", Vm.RevealFileCommand, song);
        Add(menu, "Share file", Vm.ShareFileCommand, song);
        Add(menu, "Hide from NEO", Vm.HideSongCommand, song);
        menu.Items.Add(new Separator());
        Add(menu, "Move source file to Recycle Bin…", Vm.RecycleFileCommand, song);
        menu.PlacementTarget = button;
        menu.IsOpen = true;
        e.Handled = true;
    }

    void CollectionMore_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not Button { Tag: CollectionCard card } button) return;
        ShowCollectionMenu(button, card); e.Handled = true;
    }

    void CurrentCollectionMore_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.CollectionDetail.Collection is CollectionCard card && sender is Button button) ShowCollectionMenu(button, card);
    }

    void FolderMore_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not Button { Tag: PlaylistFolder folder } button) return;
        ShowCollectionMenu(button, Vm.CardFor(folder)); e.Handled = true;
    }

    void OpenPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not Button { Tag: Playlist playlist }) return;
        if (Vm.OpenCollectionCommand.CanExecute(Vm.CardFor(playlist))) Vm.OpenCollectionCommand.Execute(Vm.CardFor(playlist));
    }

    void ShowCollectionMenu(UIElement target, CollectionCard card)
    {
        if (Vm is null) return;
        var menu = new ContextMenu();
        Add(menu, "Open", Vm.OpenCollectionCommand, card);
        Add(menu, card.IsPinned ? "Unpin" : "Pin", Vm.PinCollectionCommand, card);
        Add(menu, "Move pin up", Vm.MovePinUpCommand, card); Add(menu, "Move pin down", Vm.MovePinDownCommand, card);
        if (card.Kind is CollectionKind.Playlist or CollectionKind.Category or CollectionKind.PlaylistFolder)
        {
            menu.Items.Add(new Separator()); Add(menu, "Rename…", Vm.RenameCollectionCommand, card);
            if (card.Kind is CollectionKind.Playlist or CollectionKind.Category) Add(menu, "Custom artwork…", Vm.SetCollectionArtworkCommand, card);
            if (card.Kind == CollectionKind.Playlist)
            {
                Add(menu, "Move to folder…", Vm.MovePlaylistCommand, card);
                Add(menu, "Move up", Vm.MovePlaylistUpCommand, card); Add(menu, "Move down", Vm.MovePlaylistDownCommand, card);
            }
            if (card.Kind == CollectionKind.PlaylistFolder)
            {
                Add(menu, "New child folder…", Vm.NewChildFolderCommand, card); Add(menu, "Move under…", Vm.MoveFolderCommand, card);
            }
            menu.Items.Add(new Separator()); Add(menu, "Delete…", Vm.DeleteCollectionCommand, card);
        }
        menu.PlacementTarget = target; menu.IsOpen = true;
    }

    static void Add(ContextMenu menu, string title, ICommand command, object? parameter = null)
    {
        menu.Items.Add(new MenuItem { Header = title, Command = command, CommandParameter = parameter });
    }

    void QueueList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => queueDragStart = e.GetPosition(QueueList);

    void QueueList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(QueueList) - queueDragStart).Length > 6)
        {
            if (ItemUnderMouse(QueueList, e.GetPosition(QueueList)) is Song song)
                DragDrop.DoDragDrop(QueueList, song, DragDropEffects.Move);
        }
        else if (e.LeftButton == MouseButtonState.Released) queueDragStart = e.GetPosition(QueueList);
    }

    async void QueueList_Drop(object sender, DragEventArgs e)
    {
        if (Vm is null || !e.Data.GetDataPresent(typeof(Song))) return;
        var source = e.Data.GetData(typeof(Song)) as Song;
        var target = ItemUnderMouse(QueueList, e.GetPosition(QueueList)) as Song;
        if (source is null || target is null) return;
        var from = Vm.Playback.Queue.IndexOf(source);
        var to = Vm.Playback.Queue.IndexOf(target);
        await Vm.MoveQueueAsync(from, to);
    }

    void CollectionList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => collectionDragStart = e.GetPosition(CollectionList);

    void CollectionList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(CollectionList) - collectionDragStart).Length > 6)
        {
            if (ItemUnderMouse(CollectionList, e.GetPosition(CollectionList)) is Song song)
                DragDrop.DoDragDrop(CollectionList, song, DragDropEffects.Move);
        }
        else if (e.LeftButton == MouseButtonState.Released) collectionDragStart = e.GetPosition(CollectionList);
    }

    async void CollectionList_Drop(object sender, DragEventArgs e)
    {
        if (Vm is null || !e.Data.GetDataPresent(typeof(Song))) return;
        var source = e.Data.GetData(typeof(Song)) as Song;
        var target = ItemUnderMouse(CollectionList, e.GetPosition(CollectionList)) as Song;
        if (source is null || target is null) return;
        await Vm.ReorderCollectionAsync(Vm.CollectionDetail.VisibleSongs.IndexOf(source), Vm.CollectionDetail.VisibleSongs.IndexOf(target));
    }

    static object? ItemUnderMouse(ItemsControl control, Point point)
    {
        var hit = control.InputHitTest(point) as DependencyObject;
        while (hit is not null && hit is not ListBoxItem) hit = VisualTreeHelper.GetParent(hit);
        return hit is ListBoxItem item ? item.DataContext : null;
    }

    async void EqPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is null || sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item || item.Content is not string preset) return;
        if (Vm.EqPreset == preset) return;
        if (Vm.EqPresetCommand.CanExecute(preset)) Vm.EqPresetCommand.Execute(preset);
        await Task.CompletedTask;
    }

    void CanvasMedia_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        try
        {
            CanvasMedia.Position = TimeSpan.FromMilliseconds(Vm.Theme.CanvasStartMs);
            CanvasMedia.SpeedRatio = Vm.Theme.CanvasSpeed;
            if (Vm.Playback.IsPlaying) CanvasMedia.Play(); else CanvasMedia.Pause();
        }
        catch { }
    }

    void UiTimer_Tick(object? sender, EventArgs e)
    {
        var vm = Vm;
        if (vm is null) return;
        if (vm.Theme.CanvasEnabled && vm.IsNowPlayingOpen)
        {
            try
            {
                CanvasMedia.SpeedRatio = vm.Theme.CanvasSpeed;
                if (vm.Playback.IsPlaying) CanvasMedia.Play(); else CanvasMedia.Pause();
                var end = vm.Theme.CanvasEndMs;
                if (end > vm.Theme.CanvasStartMs && CanvasMedia.Position.TotalMilliseconds >= end)
                    CanvasMedia.Position = TimeSpan.FromMilliseconds(vm.Theme.CanvasStartMs);
            }
            catch { }
        }
        else { try { CanvasMedia.Pause(); } catch { } }

        if (vm.IsLyricsOpen && vm.Settings.LyricsAutoScroll)
        {
            var active = vm.LyricDraftLines.FirstOrDefault(x => x.IsActive);
            if (active is not null) LyricsList.ScrollIntoView(active);
        }
    }

    async void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || Vm is null) return;
        speedDebounce?.Cancel(); speedDebounce?.Dispose();
        speedDebounce = new CancellationTokenSource(); var token = speedDebounce.Token;
        try { await Task.Delay(220, token); if (!token.IsCancellationRequested) await Vm.SetPlaybackSpeedAsync(e.NewValue); }
        catch (OperationCanceledException) { }
    }

    void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded && Vm is not null) Vm.SetPlaybackVolume(e.NewValue);
    }

    async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Vm is not null) { await Vm.CommitSearchAsync(); e.Handled = true; }
    }

    async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is null || Keyboard.FocusedElement is TextBox or PasswordBox or ComboBox) return;
        switch (e.Key)
        {
            case Key.Space: Vm.TogglePlayCommand.Execute(null); e.Handled = true; break;
            case Key.Right when Keyboard.Modifiers.HasFlag(ModifierKeys.Control): await App.Host.Playback.NextAsync(); e.Handled = true; break;
            case Key.Left when Keyboard.Modifiers.HasFlag(ModifierKeys.Control): await App.Host.Playback.PreviousAsync(); e.Handled = true; break;
            case Key.Right: await App.Host.Playback.SeekAsync(Math.Min(Vm.Playback.DurationMs, Vm.Playback.PositionMs + 5000)); e.Handled = true; break;
            case Key.Left: await App.Host.Playback.SeekAsync(Math.Max(0, Vm.Playback.PositionMs - 5000)); e.Handled = true; break;
            case Key.Up: Vm.SetPlaybackVolume(Math.Min(1, Vm.Playback.Volume + .05)); e.Handled = true; break;
            case Key.Down: Vm.SetPlaybackVolume(Math.Max(0, Vm.Playback.Volume - .05)); e.Handled = true; break;
            case Key.Escape:
                if (Vm.IsLyricsEditorOpen) Vm.IsLyricsEditorOpen = false;
                else if (Vm.IsHiddenOpen) Vm.IsHiddenOpen = false;
                else if (Vm.IsCollectionOpen) Vm.IsCollectionOpen = false;
                else if (Vm.IsSettingsOpen) Vm.IsSettingsOpen = false;
                else if (Vm.IsNowPlayingOpen) Vm.IsNowPlayingOpen = false;
                e.Handled = true; break;
        }
    }

    void CloseHidden_Click(object sender, RoutedEventArgs e) { if (Vm is not null) Vm.IsHiddenOpen = false; }
    void CloseLyricsEditor_Click(object sender, RoutedEventArgs e) { if (Vm is not null) Vm.IsLyricsEditorOpen = false; }

    protected override void OnClosed(EventArgs e)
    {
        uiTimer.Stop();
        speedDebounce?.Cancel(); speedDebounce?.Dispose();
        base.OnClosed(e);
    }
}
