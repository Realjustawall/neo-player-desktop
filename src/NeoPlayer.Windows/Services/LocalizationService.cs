using System.ComponentModel;
using System.Globalization;
using System.Windows;

namespace NeoPlayer.Windows.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    readonly SettingsService settings;
    ResourceDictionary? activeDictionary;
    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizationService(SettingsService settings)
    {
        this.settings = settings;
        settings.Changed += (_, _) => Apply();
    }

    string Effective => settings.Current.Language.Equals("system", StringComparison.OrdinalIgnoreCase)
        ? CultureInfo.CurrentUICulture.Name : settings.Current.Language;
    public bool IsRtl => Effective.StartsWith("fa", StringComparison.OrdinalIgnoreCase);

    public void Apply()
    {
        if (Application.Current is null) return;
        var lang = IsRtl ? "fa" : "en";
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (activeDictionary is not null) Application.Current.Resources.MergedDictionaries.Remove(activeDictionary);
            activeDictionary = new ResourceDictionary { Source = new Uri($"Resources/Strings.{lang}.xaml", UriKind.Relative) };
            Application.Current.Resources.MergedDictionaries.Insert(0, activeDictionary);
            PropertyChanged?.Invoke(this, new(nameof(IsRtl)));
            PropertyChanged?.Invoke(this, new("Item[]"));
        });
    }

    public string this[string key] => Application.Current?.TryFindResource("Str" + key)?.ToString() ?? key;
}
