using System.Text.Json;
using NeoPlayer.Windows.Core;
using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.Services;

public sealed class SettingsService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    public AppSettings Value { get; private set; } = new();
    public event EventHandler? Changed;

    public async Task LoadAsync()
    {
        AppPaths.Ensure();
        if (!File.Exists(AppPaths.Settings)) { await SaveAsync(); return; }
        try
        {
            await using var stream = File.OpenRead(AppPaths.Settings);
            Value = await JsonSerializer.DeserializeAsync<AppSettings>(stream, _json) ?? new AppSettings();
            Normalize();
        }
        catch { Value = new AppSettings(); }
    }

    public async Task SaveAsync()
    {
        await _gate.WaitAsync();
        try
        {
            AppPaths.Ensure();
            Normalize();
            var temp = AppPaths.Settings + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(Value, _json));
            File.Move(temp, AppPaths.Settings, true);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { _gate.Release(); }
    }

    public async Task AddRecentSearchAsync(string value)
    {
        value = value.Trim(); if (value.Length == 0) return;
        Value.RecentSearches.RemoveAll(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
        Value.RecentSearches.Insert(0, value);
        if (Value.RecentSearches.Count > 12) Value.RecentSearches.RemoveRange(12, Value.RecentSearches.Count - 12);
        await SaveAsync();
    }

    private void Normalize()
    {
        Value.Volume = Math.Clamp(Value.Volume, 0, 1);
        Value.PlaybackSpeed = Math.Clamp(Value.PlaybackSpeed, 0.5, 2);
        Value.CrossfadeSeconds = Math.Clamp(Value.CrossfadeSeconds, 0, 15);
        Value.MinimumDurationSeconds = Math.Clamp(Value.MinimumDurationSeconds, 0, 600);
        Value.PersistedPositionSeconds = Math.Max(0, Value.PersistedPositionSeconds);
        Value.SourceFolders = Value.SourceFolders.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Value.ExcludedFolders = Value.ExcludedFolders.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Value.PersistedQueuePaths = Value.PersistedQueuePaths.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Take(5000).ToList();
        Value.PersistedQueueIndex = Value.PersistedQueuePaths.Count == 0 ? -1 : Math.Clamp(Value.PersistedQueueIndex, 0, Value.PersistedQueuePaths.Count - 1);
        Value.EqualizerBands ??= new List<float>();
        while (Value.EqualizerBands.Count < 10) Value.EqualizerBands.Add(0f);
        if (Value.EqualizerBands.Count > 10) Value.EqualizerBands = Value.EqualizerBands.Take(10).ToList();
        for (var i = 0; i < Value.EqualizerBands.Count; i++) Value.EqualizerBands[i] = Math.Clamp(Value.EqualizerBands[i], -12f, 12f);
        Value.BassDb = Math.Clamp(Value.BassDb, -12f, 12f);
        Value.StereoWidth = Math.Clamp(Value.StereoWidth, 0f, 2f);
        Value.WindowWidth = Math.Clamp(Value.WindowWidth, 900, 3840);
        Value.WindowHeight = Math.Clamp(Value.WindowHeight, 600, 2160);
    }
}
