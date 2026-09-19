using System.Windows;
using System.Windows.Media;
using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.Services;

public sealed class ThemeService(SettingsService settings)
{
    private static readonly Dictionary<string, string> Accents = new(StringComparer.OrdinalIgnoreCase)
    { ["Orange"]="#FF8A00", ["Green"]="#3DDC84", ["Red"]="#FF4D5E", ["Blue"]="#4C8DFF", ["Purple"]="#A970FF", ["Cyan"]="#27D7E8", ["Pink"]="#FF6FB5", ["Gold"]="#F5C451" };

    public void Apply()
    {
        var app = Application.Current; if (app is null) return;
        var dark = settings.Value.Theme is ThemeMode.Dark or ThemeMode.Amoled || settings.Value.Theme == ThemeMode.System;
        var bg = settings.Value.Theme == ThemeMode.Amoled ? "#000000" : dark ? "#111216" : "#F4F5F7";
        var panel = settings.Value.Theme == ThemeMode.Amoled ? "#080808" : dark ? "#191B21" : "#FFFFFF";
        var fg = dark ? "#F6F7FB" : "#15171B"; var muted = dark ? "#9CA2B1" : "#626977";
        app.Resources["NeoBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
        app.Resources["NeoPanel"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(panel));
        app.Resources["NeoForeground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg));
        app.Resources["NeoMuted"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(muted));
        var accent = Accents.GetValueOrDefault(settings.Value.Accent, Accents["Orange"]);
        app.Resources["NeoAccent"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent));
    }
}
