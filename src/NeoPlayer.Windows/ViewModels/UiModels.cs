using NeoPlayer.Windows.Models;
using System.Collections.ObjectModel;

namespace NeoPlayer.Windows.ViewModels;

public enum CollectionKind { Playlist, Category, Album, Artist, Genre, Folder, PlaylistFolder, Pinned, OfflineBackup }

public sealed class CollectionCard : BindableBase
{
    bool pinned;
    bool favorite;
    public CollectionKind Kind { get; init; }
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string? ArtworkPath { get; init; }
    public long? NumericId { get; init; }
    public long? ParentId { get; init; }
    public bool IsPinned { get => pinned; set => Set(ref pinned, value); }
    public bool IsFavorite { get => favorite; set => Set(ref favorite, value); }
}

public sealed class PlaylistFolderNode : BindableBase
{
    public PlaylistFolder Folder { get; init; } = new(0, "", null, 0, 0);
    public ObservableCollection<PlaylistFolderNode> Children { get; } = [];
    public ObservableCollection<Playlist> Playlists { get; } = [];
    public string Title => Folder.Title;
    public long Id => Folder.Id;
}

public sealed class CollectionDetailViewModel : BindableBase
{
    string title = "";
    string subtitle = "";
    string searchText = "";
    string sortMode = "custom";
    bool ascending = true;
    string viewMode = "list";
    CollectionCard? collection;
    public CollectionCard? Collection { get => collection; set => Set(ref collection, value); }
    public string Title { get => title; set => Set(ref title, value); }
    public string Subtitle { get => subtitle; set => Set(ref subtitle, value); }
    public string SearchText { get => searchText; set => Set(ref searchText, value); }
    public string SortMode { get => sortMode; set => Set(ref sortMode, value); }
    public bool Ascending { get => ascending; set => Set(ref ascending, value); }
    public string ViewMode { get => viewMode; set => Set(ref viewMode, value); }
    public ObservableCollection<Song> Songs { get; } = [];
    public ObservableCollection<Song> VisibleSongs { get; } = [];
}

public sealed class LyricDraftLine : BindableBase
{
    long timeMs;
    string text = "";
    bool active;
    public long TimeMs { get => timeMs; set { if (Set(ref timeMs, value)) Raise(nameof(TimeText)); } }
    public string TimeText => TimeSpan.FromMilliseconds(TimeMs).ToString(@"mm\:ss\.ff");
    public string Text { get => text; set => Set(ref text, value); }
    public bool IsActive { get => active; set => Set(ref active, value); }
}

public sealed class EqBandViewModel : BindableBase
{
    float gain;
    public int Index { get; init; }
    public string Label { get; init; } = "";
    public float Gain { get => gain; set => Set(ref gain, Math.Clamp(value, -12, 12)); }
}

public sealed class TrackPlusEditorViewModel : BindableBase
{
    string? canvasPath;
    bool canvasEnabled;
    string canvasFit = "crop";
    long canvasStartMs;
    long canvasEndMs;
    float canvasPlaybackSpeed = 1;
    string themeMode = "inherit";
    string accentHex = "";
    string backgroundHex = "";
    string secondaryHex = "";
    string visualizerMode = "waveform";
    float visualizerSensitivity = 1;
    float animationIntensity = 1;
    string backgroundImagePath = "";
    string backgroundMode = "overlay";
    float backgroundOpacity = .28f;
    int backgroundBlurDp = 18;

    public string? CanvasPath { get => canvasPath; set => Set(ref canvasPath, value); }
    public bool CanvasEnabled { get => canvasEnabled; set => Set(ref canvasEnabled, value); }
    public string CanvasFit { get => canvasFit; set => Set(ref canvasFit, value); }
    public long CanvasStartMs { get => canvasStartMs; set => Set(ref canvasStartMs, Math.Max(0, value)); }
    public long CanvasEndMs { get => canvasEndMs; set => Set(ref canvasEndMs, Math.Max(0, value)); }
    public float CanvasPlaybackSpeed { get => canvasPlaybackSpeed; set => Set(ref canvasPlaybackSpeed, Math.Clamp(value, .25f, 3f)); }
    public string ThemeMode { get => themeMode; set => Set(ref themeMode, value); }
    public string AccentHex { get => accentHex; set => Set(ref accentHex, value); }
    public string BackgroundHex { get => backgroundHex; set => Set(ref backgroundHex, value); }
    public string SecondaryHex { get => secondaryHex; set => Set(ref secondaryHex, value); }
    public string VisualizerMode { get => visualizerMode; set => Set(ref visualizerMode, value); }
    public float VisualizerSensitivity { get => visualizerSensitivity; set => Set(ref visualizerSensitivity, Math.Clamp(value, .1f, 4f)); }
    public float AnimationIntensity { get => animationIntensity; set => Set(ref animationIntensity, Math.Clamp(value, 0, 2f)); }
    public string BackgroundImagePath { get => backgroundImagePath; set => Set(ref backgroundImagePath, value); }
    public string BackgroundMode { get => backgroundMode; set => Set(ref backgroundMode, value); }
    public float BackgroundOpacity { get => backgroundOpacity; set => Set(ref backgroundOpacity, Math.Clamp(value, 0, .85f)); }
    public int BackgroundBlurDp { get => backgroundBlurDp; set => Set(ref backgroundBlurDp, Math.Clamp(value, 0, 48)); }
}

public sealed record RecommendationItem(Song Song, float Score, string Reason);
public sealed record SearchSuggestion(string Text, string Kind);
