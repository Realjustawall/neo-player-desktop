using Microsoft.Win32;
using NeoPlayer.Windows.Models;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace NeoPlayer.Windows.Services;

public sealed class DialogService
{
    public string? PickFolder(string description = "Select music folder")
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = description, UseDescriptionForTitle = true, ShowNewFolderButton = false };
        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    public string? OpenFile(string filter, string? title = null)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = filter, Multiselect = false, Title = title ?? "Open" };
        return d.ShowDialog() == true ? d.FileName : null;
    }

    public string? SaveFile(string filter, string defaultExt, string fileName)
    {
        var d = new Microsoft.Win32.SaveFileDialog { Filter = filter, DefaultExt = defaultExt, FileName = fileName, AddExtension = true };
        return d.ShowDialog() == true ? d.FileName : null;
    }



    public int? PickOption(string title, string label, IReadOnlyList<string> options, int selectedIndex = 0)
    {
        var window = new Window
        {
            Title = title, Width = 470, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow, Background = (System.Windows.Media.Brush)Application.Current.Resources["NeoBackground"]
        };
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var caption = new TextBlock { Text = label, Margin = new Thickness(0,0,0,10) };
        var list = new System.Windows.Controls.ListBox { ItemsSource = options, SelectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, options.Count-1)) };
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", MinWidth = 90 }; var ok = new System.Windows.Controls.Button { Content = "Choose", MinWidth = 90, IsDefault = true };
        cancel.Click += (_,_) => window.DialogResult=false; ok.Click += (_,_) => window.DialogResult=true; list.MouseDoubleClick += (_,_) => window.DialogResult=true;
        buttons.Children.Add(cancel); buttons.Children.Add(ok); Grid.SetRow(caption,0); Grid.SetRow(list,1); Grid.SetRow(buttons,2); root.Children.Add(caption); root.Children.Add(list); root.Children.Add(buttons); window.Content=root;
        return window.ShowDialog()==true && list.SelectedIndex>=0 ? list.SelectedIndex : null;
    }

    public int? PickColor(int initialArgb)
    {
        using var d = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(initialArgb) };
        if (d.ShowDialog() != Forms.DialogResult.OK) return null;
        return d.Color.ToArgb();
    }

    public bool Confirm(string message, string title = "NEO Player") =>
        System.Windows.MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Info(string message, string title = "NEO Player") =>
        System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public string? Prompt(string title, string label, string initial = "")
    {
        var window = new Window
        {
            Title = title,
            Width = 430,
            Height = 190,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["NeoBackground"]
        };
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) };
        var box = new System.Windows.Controls.TextBox { Text = initial, MinWidth = 360 };
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", MinWidth = 90 };
        var ok = new System.Windows.Controls.Button { Content = "OK", MinWidth = 90, IsDefault = true };
        cancel.Click += (_, _) => window.DialogResult = false;
        ok.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        Grid.SetRow(text, 0); Grid.SetRow(box, 1); Grid.SetRow(buttons, 2);
        root.Children.Add(text); root.Children.Add(box); root.Children.Add(buttons); window.Content = root;
        box.SelectAll(); box.Focus();
        return window.ShowDialog() == true ? box.Text.Trim() : null;
    }

    public MetadataEditResult? EditMetadata(Song song)
    {
        var window = new Window
        {
            Title = "Edit library metadata",
            Width = 520,
            Height = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["NeoBackground"]
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        var title = Field(panel, "Title", song.Title);
        var artist = Field(panel, "Artist", song.Artist);
        var album = Field(panel, "Album", song.Album);
        var genre = Field(panel, "Genre", song.Genre);
        var year = Field(panel, "Year", song.Year == 0 ? "" : song.Year.ToString());
        var note = new TextBlock { Text = "Changes are reversible NEO library overrides; the source audio file is not rewritten.", TextWrapping = TextWrapping.Wrap, Opacity = .7, Margin = new Thickness(0, 12, 0, 12) };
        panel.Children.Add(note);
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", MinWidth = 90 };
        var save = new System.Windows.Controls.Button { Content = "Save", MinWidth = 90, IsDefault = true };
        cancel.Click += (_, _) => window.DialogResult = false; save.Click += (_, _) => window.DialogResult = true;
        row.Children.Add(cancel); row.Children.Add(save); panel.Children.Add(row); window.Content = new ScrollViewer { Content = panel };
        if (window.ShowDialog() != true) return null;
        _ = int.TryParse(year.Text, out var y);
        return new(title.Text.Trim(), artist.Text.Trim(), album.Text.Trim(), genre.Text.Trim(), y);
    }

    static System.Windows.Controls.TextBox Field(Panel panel, string label, string value)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
        var box = new System.Windows.Controls.TextBox { Text = value };
        panel.Children.Add(box);
        return box;
    }
}

public sealed record MetadataEditResult(string Title, string Artist, string Album, string Genre, int Year);
