namespace NeoPlayer.Windows.Models;

public sealed record Song(
    long Id,
    string Path,
    string Title,
    string Artist,
    string Album,
    string Genre,
    long DurationMs,
    int TrackNumber,
    int Year,
    string? ArtworkPath,
    bool Favorite,
    bool Hidden,
    long DateAddedUnix,
    long LastPlayedUnix,
    int PlayCount);

public sealed record Playlist(long Id, string Name, long? FolderId, int SortOrder, string SortMode, bool SortDescending, string ViewMode);
public sealed record PlaylistFolder(long Id, string Name, long? ParentId, int SortOrder);
public sealed record Category(long Id, string Name, int SortOrder);
public sealed record LyricLine(TimeSpan Time, string Text);
public sealed record SearchSuggestion(string Text, string Kind);
public sealed record AudioAnalysis(double Rms, double Peak, double EstimatedLufs, double Bpm, double Energy, long AnalyzedAtUnix);

public enum RepeatMode { Off, One, All }
public enum NavigationPage { Home, Search, Library, Playlists, Settings }
public enum ThemeMode { System, Light, Dark, Amoled }

public sealed class AppSettings
{
    public string Language { get; set; } = "en";
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public string Accent { get; set; } = "Orange";
    public List<string> SourceFolders { get; set; } = new();
    public List<string> ExcludedFolders { get; set; } = new();
    public int MinimumDurationSeconds { get; set; } = 5;
    public double Volume { get; set; } = 0.85;
    public double PlaybackSpeed { get; set; } = 1.0;
    public bool Shuffle { get; set; }
    public RepeatMode Repeat { get; set; }
    public bool Gapless { get; set; } = true;
    public bool CrossfadeEnabled { get; set; } = true;
    public double CrossfadeSeconds { get; set; } = 5;
    public bool Normalize { get; set; }
    public double TargetLufs { get; set; } = -14;
    public string LibraryView { get; set; } = "List";
    public List<string> RecentSearches { get; set; } = new();
}
