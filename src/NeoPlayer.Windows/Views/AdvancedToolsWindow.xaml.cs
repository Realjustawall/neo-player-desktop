using System.Windows;
using Microsoft.Win32;
using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.Views;

public partial class AdvancedToolsWindow : Window
{
    private bool _loading;
    private AppHost Host => AppHost.Current ?? throw new InvalidOperationException("NEO Player services are not available.");

    public AdvancedToolsWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        _loading = true;
        try
        {
            GaplessCheck.IsChecked = Host.Settings.Value.Gapless;
            NormalizeCheck.IsChecked = Host.Settings.Value.Normalize;
            TargetLufsSlider.Value = Host.Settings.Value.TargetLufs;
            TargetLufsText.Text = $"{Host.Settings.Value.TargetLufs:0.0}";
            await RefreshAsync();
        }
        finally { _loading = false; }
    }

    private async Task RefreshAsync()
    {
        var folders = await Host.PlaylistFolders.GetFoldersAsync();
        var playlists = await Host.Database.GetPlaylistsAsync();
        FolderList.ItemsSource = folders;
        ParentFolderCombo.ItemsSource = folders;
        DestinationFolderCombo.ItemsSource = folders;
        PlaylistCombo.ItemsSource = playlists;
        StatusText.Text = $"{playlists.Count} playlists · {folders.Count} folders";
    }

    private async void CreateFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var parent = ParentFolderCombo.SelectedItem as PlaylistFolder;
            await Host.PlaylistFolders.CreateAsync(FolderNameBox.Text, parent?.Id);
            FolderNameBox.Clear();
            await RefreshAsync();
            StatusText.Text = "Playlist folder created";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is not PlaylistFolder folder) return;
        if (MessageBox.Show($"Delete playlist folder '{folder.Name}'? Child folders and playlists will move to root.", "NEO Player", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await Host.PlaylistFolders.DeleteAsync(folder.Id);
            await RefreshAsync();
            StatusText.Text = "Playlist folder deleted";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void MoveFolderRoot_Click(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is not PlaylistFolder folder) return;
        try
        {
            await Host.PlaylistFolders.MoveAsync(folder.Id, null);
            await RefreshAsync();
            StatusText.Text = "Folder moved to root";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void AssignPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistCombo.SelectedItem is not Playlist playlist || DestinationFolderCombo.SelectedItem is not PlaylistFolder folder) return;
        try
        {
            await Host.PlaylistFolders.AssignPlaylistAsync(playlist.Id, folder.Id);
            await RefreshAsync();
            StatusText.Text = $"'{playlist.Name}' moved to '{folder.Name}'";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void UnassignPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistCombo.SelectedItem is not Playlist playlist) return;
        try
        {
            await Host.PlaylistFolders.AssignPlaylistAsync(playlist.Id, null);
            await RefreshAsync();
            StatusText.Text = $"'{playlist.Name}' moved to root";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void ImportM3u_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "M3U playlists|*.m3u;*.m3u8|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var playlist = await Host.PlaylistTransfer.ImportM3u8Async(dialog.FileName);
            await RefreshAsync();
            StatusText.Text = $"Imported '{playlist.Name}'";
        }
        catch (Exception ex) { StatusText.Text = $"Import failed: {ex.Message}"; }
    }

    private async void ExportM3u_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistCombo.SelectedItem is not Playlist playlist) { StatusText.Text = "Select a playlist first"; return; }
        var dialog = new SaveFileDialog { Filter = "M3U8 playlist|*.m3u8", FileName = playlist.Name + ".m3u8" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await Host.PlaylistTransfer.ExportM3u8Async(playlist, dialog.FileName);
            StatusText.Text = "Playlist exported";
        }
        catch (Exception ex) { StatusText.Text = $"Export failed: {ex.Message}"; }
    }

    private void GaplessCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Host.Playback.SetGapless(GaplessCheck.IsChecked == true);
        StatusText.Text = GaplessCheck.IsChecked == true ? "Gapless preload enabled" : "Gapless preload disabled";
    }

    private void NormalizeCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Host.Playback.SetNormalize(NormalizeCheck.IsChecked == true);
        StatusText.Text = NormalizeCheck.IsChecked == true ? "Loudness normalization enabled for new tracks" : "Loudness normalization disabled";
    }

    private async void TargetLufsSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        TargetLufsText.Text = $"{e.NewValue:0.0}";
        if (_loading || AppHost.Current is null) return;
        Host.Settings.Value.TargetLufs = Math.Clamp(e.NewValue, -20, -8);
        await Host.Settings.SaveAsync();
    }

    private void FolderList_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (FolderList.SelectedItem is not PlaylistFolder selected) return;
        var folders = (ParentFolderCombo.ItemsSource as IEnumerable<PlaylistFolder>)?.Where(x => x.Id != selected.Id).ToList();
        if (folders is not null) ParentFolderCombo.ItemsSource = folders;
    }

    private void PlaylistCombo_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PlaylistCombo.SelectedItem is Playlist playlist)
            DestinationFolderCombo.SelectedItem = (DestinationFolderCombo.ItemsSource as IEnumerable<PlaylistFolder>)?.FirstOrDefault(x => x.Id == playlist.FolderId);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
