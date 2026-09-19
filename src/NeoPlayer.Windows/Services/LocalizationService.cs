using System.Globalization;
using System.Windows;
using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.Services;

public sealed class LocalizationService(SettingsService settings)
{
    public void Apply(string? language = null)
    {
        language ??= settings.Value.Language;
        if (language is not ("fa" or "en")) language = "en";
        settings.Value.Language = language;
        var app = Application.Current; if (app is null) return;
        var existing = app.Resources.MergedDictionaries.FirstOrDefault(x => x.Source?.OriginalString.Contains("Strings.") == true);
        if (existing is not null) app.Resources.MergedDictionaries.Remove(existing);
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"Resources/Strings.{language}.xaml", UriKind.Relative) });
        foreach (Window w in app.Windows) w.FlowDirection = language == "fa" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }
}
