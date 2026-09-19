using NeoPlayer.Windows.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NeoPlayer.Windows.Controls;

public sealed class ArtworkImage : Image
{
    CancellationTokenSource? cts;
    public static readonly DependencyProperty SongProperty = DependencyProperty.Register(
        nameof(Song), typeof(Song), typeof(ArtworkImage), new PropertyMetadata(null, OnSongChanged));
    public Song? Song { get => (Song?)GetValue(SongProperty); set => SetValue(SongProperty, value); }

    static void OnSongChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ArtworkImage)d).LoadAsync(e.NewValue as Song);

    async void LoadAsync(Song? song)
    {
        cts?.Cancel(); cts?.Dispose(); cts = new();
        Source = null;
        if (song is null) return;
        try
        {
            var path = await App.Host.Artwork.ResolveAsync(song, cts.Token);
            if (path is null || cts.IsCancellationRequested) return;
            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(path, UriKind.Absolute);
                    image.DecodePixelWidth = 420;
                    image.EndInit();
                    image.Freeze();
                    Source = image;
                }
                catch { Source = null; }
            });
        }
        catch { Source = null; }
    }
}
