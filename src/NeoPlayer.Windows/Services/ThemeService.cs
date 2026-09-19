using Microsoft.Win32;
using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace NeoPlayer.Windows.Services;

public sealed class ThemeService : INotifyPropertyChanged
{
    readonly SettingsService settings;
    readonly NeoDatabase db;
    readonly ArtworkService artwork;
    string? wallpaperPath;
    double wallpaperOpacity = .28;
    double wallpaperBlur = 18;
    string? canvasPath;
    bool canvasEnabled;
    string canvasFit = "crop";
    double canvasSpeed = 1;
    long canvasStartMs;
    long canvasEndMs;
    string visualizerMode = "waveform";
    double visualizerSensitivity = 1;
    double animationIntensity = 1;

    public event PropertyChangedEventHandler? PropertyChanged;
    public string? WallpaperPath { get => wallpaperPath; private set => Set(ref wallpaperPath, value); }
    public double WallpaperOpacity { get => wallpaperOpacity; private set => Set(ref wallpaperOpacity, value); }
    public double WallpaperBlur { get => wallpaperBlur; private set => Set(ref wallpaperBlur, value); }
    public string? CanvasPath { get => canvasPath; private set => Set(ref canvasPath, value); }
    public bool CanvasEnabled { get => canvasEnabled; private set => Set(ref canvasEnabled, value); }
    public string CanvasFit { get => canvasFit; private set => Set(ref canvasFit, value); }
    public double CanvasSpeed { get => canvasSpeed; private set => Set(ref canvasSpeed, value); }
    public long CanvasStartMs { get => canvasStartMs; private set => Set(ref canvasStartMs, value); }
    public long CanvasEndMs { get => canvasEndMs; private set => Set(ref canvasEndMs, value); }
    public string VisualizerMode { get => visualizerMode; private set => Set(ref visualizerMode, value); }
    public double VisualizerSensitivity { get => visualizerSensitivity; private set => Set(ref visualizerSensitivity, value); }
    public double AnimationIntensity { get => animationIntensity; private set => Set(ref animationIntensity, value); }

    public ThemeService(SettingsService settings, NeoDatabase db, ArtworkService artwork)
    {
        this.settings = settings;
        this.db = db;
        this.artwork = artwork;
        settings.Changed += (_, _) => _ = ApplyAsync(null);
    }

    public async Task ApplyAsync(Song? current)
    {
        var s = settings.Current;
        TrackVisualProfile? visual = current is null ? null : await db.GetTrackVisualAsync(current.Id);
        var effectiveTheme = (visual?.ThemeMode ?? "inherit").ToLowerInvariant() switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            "amoled" => ThemeMode.Amoled,
            "system" => ThemeMode.System,
            _ => s.ThemeMode
        };
        var isDark = effectiveTheme switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark or ThemeMode.Amoled => true,
            _ => !WindowsUsesLightTheme()
        };

        var baseBackground = effectiveTheme == ThemeMode.Amoled && isDark
            ? Color.FromRgb(0, 0, 0)
            : isDark ? Color.FromRgb(0x10, 0x10, 0x10) : Color.FromRgb(0xFF, 0xFB, 0xF7);
        var background = visual?.BackgroundArgb is int bg && bg != 0 ? FromArgb(bg) : baseBackground;
        isDark = Luminance(background) < .48;
        var surface = visual?.BackgroundArgb is int customBg && customBg != 0
            ? Mix(background, isDark ? Colors.White : Colors.Black, isDark ? .07 : .035)
            : effectiveTheme == ThemeMode.Amoled && isDark ? Colors.Black : isDark ? Color.FromRgb(0x18, 0x18, 0x18) : Colors.White;
        var accent = AccentColor(s);
        if (visual?.AccentArgb is int va && va != 0) accent = FromArgb(va);
        else if (current is not null && s.DynamicArtwork) accent = FromArgb(await artwork.DominantColorAsync(current));
        var secondary = visual?.SecondaryArgb is int vs && vs != 0 ? FromArgb(vs) : accent;
        var text = isDark ? Color.FromRgb(0xF6, 0xF3, 0xEF) : Color.FromRgb(0x1C, 0x19, 0x17);
        var muted = Color.FromArgb(isDark ? (byte)0xB8 : (byte)0xA8, text.R, text.G, text.B);

        ApplyBrush("NeoBackground", background);
        ApplyBrush("NeoSurface", surface);
        ApplyBrush("NeoSurfaceLow", Mix(background, isDark ? Colors.White : Colors.Black, isDark ? .05 : .025));
        ApplyBrush("NeoSurfaceHigh", Mix(background, isDark ? Colors.White : Colors.Black, isDark ? .105 : .055));
        ApplyBrush("NeoAccent", accent);
        ApplyBrush("NeoSecondary", secondary);
        ApplyBrush("NeoText", text);
        ApplyBrush("NeoMuted", muted);
        ApplyBrush("NeoOutline", Color.FromArgb(48, text.R, text.G, text.B));
        ApplyBrush("NeoAccentContainer", Mix(background, accent, isDark ? .24 : .16));
        Application.Current.Resources["NeoIsDark"] = isDark;

        var backgroundMode = (visual?.BackgroundMode ?? "overlay").ToLowerInvariant();
        WallpaperPath = backgroundMode == "none" ? null : visual?.BackgroundImagePath is { Length: > 0 } p && File.Exists(p) ? p : null;
        WallpaperOpacity = backgroundMode == "replace" ? Math.Max(.55, visual?.BackgroundOpacity ?? .85) : visual?.BackgroundOpacity ?? .28;
        WallpaperBlur = visual?.BackgroundBlurDp ?? 18;
        CanvasPath = visual?.CanvasPath is { Length: > 0 } c && File.Exists(c) ? c : null;
        CanvasEnabled = visual?.CanvasEnabled == true && CanvasPath is not null;
        CanvasFit = visual?.CanvasFit ?? "crop";
        CanvasSpeed = Math.Clamp(visual?.CanvasPlaybackSpeed ?? 1, .25f, 3f);
        CanvasStartMs = visual?.CanvasStartMs ?? 0;
        CanvasEndMs = visual?.CanvasEndMs ?? 0;
        VisualizerMode = visual?.VisualizerMode ?? "waveform";
        VisualizerSensitivity = Math.Clamp(visual?.VisualizerSensitivity ?? 1, .1f, 4f);
        AnimationIntensity = Math.Clamp(visual?.AnimationIntensity ?? 1, 0, 2);
    }

    public System.Windows.FlowDirection FlowDirection => settings.Current.Language.StartsWith("fa", StringComparison.OrdinalIgnoreCase)
        ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;

    static void ApplyBrush(string key, Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }

    static Color AccentColor(AppSettings s) => s.Accent switch
    {
        Accent.Green => Color.FromRgb(0x45, 0xD4, 0x83),
        Accent.Red => Color.FromRgb(0xFF, 0x53, 0x64),
        Accent.Blue => Color.FromRgb(0x5B, 0x8C, 0xFF),
        Accent.Custard => Color.FromRgb(0xE8, 0xC9, 0x78),
        Accent.Purple => Color.FromRgb(0xB5, 0x86, 0xFF),
        Accent.Cyan => Color.FromRgb(0x42, 0xD9, 0xE8),
        Accent.Pink => Color.FromRgb(0xFF, 0x6F, 0xAE),
        Accent.Indigo => Color.FromRgb(0x7C, 0x83, 0xFF),
        Accent.Teal => Color.FromRgb(0x35, 0xC6, 0xA5),
        Accent.Gold => Color.FromRgb(0xFF, 0xB8, 0x4D),
        Accent.Custom => FromArgb(s.CustomColor),
        _ => Color.FromRgb(0xFF, 0x7A, 0x1A)
    };

    static Color FromArgb(int argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    static double Luminance(Color c)
    {
        static double F(byte b) { var x = b / 255d; return x <= .03928 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
        return .2126 * F(c.R) + .7152 * F(c.G) + .0722 * F(c.B);
    }
    static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(255,
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }
    static bool WindowsUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 0), CultureInfo.InvariantCulture) != 0;
        }
        catch { return false; }
    }
    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
        if (name is not nameof(FlowDirection)) PropertyChanged?.Invoke(this, new(nameof(FlowDirection)));
    }
}
