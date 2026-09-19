using NeoPlayer.Windows.Models;
using System.ComponentModel;
using System.Text.Json;

namespace NeoPlayer.Windows.Services;

public sealed class SettingsService : IAsyncDisposable
{
    public AppSettings Current { get; private set; } = new();
    readonly JsonSerializerOptions json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    readonly SemaphoreSlim saveGate = new(1, 1);
    public event EventHandler? Changed;

    public async Task LoadAsync()
    {
        AppPaths.Ensure();
        if (File.Exists(AppPaths.Settings))
        {
            try { Current = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(AppPaths.Settings), json) ?? new(); }
            catch { Current = new(); }
        }
        Normalize();
        Hook();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void Normalize()
    {
        Current.RecentSearches ??= [];
        Current.IncludedFolders ??= [];
        Current.ExcludedFolders ??= [];
        Current.RecentSearches = Current.RecentSearches.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        Current.IncludedFolders = NormalizePaths(Current.IncludedFolders);
        Current.ExcludedFolders = NormalizePaths(Current.ExcludedFolders);
    }

    static List<string> NormalizePaths(IEnumerable<string> values) => values
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => { try { return Path.GetFullPath(x); } catch { return x.Trim(); } })
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    void Hook()
    {
        Current.PropertyChanged -= OnChanged;
        Current.PropertyChanged += OnChanged;
    }

    async void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        try { await SaveAsync(); Changed?.Invoke(this, EventArgs.Empty); }
        catch { /* settings write failure must not crash playback/UI */ }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await saveGate.WaitAsync(ct);
        try
        {
            AppPaths.Ensure();
            var tmp = AppPaths.Settings + ".tmp";
            var jsonText = JsonSerializer.Serialize(Current, json);
            await File.WriteAllTextAsync(tmp, jsonText, ct);
            File.Move(tmp, AppPaths.Settings, true);
        }
        finally { saveGate.Release(); }
    }

    public async Task AddRecentSearchAsync(string q)
    {
        q = q.Trim(); if (q.Length == 0) return;
        Current.RecentSearches.RemoveAll(x => x.Equals(q, StringComparison.OrdinalIgnoreCase));
        Current.RecentSearches.Insert(0, q);
        if (Current.RecentSearches.Count > 12) Current.RecentSearches.RemoveRange(12, Current.RecentSearches.Count - 12);
        await SaveAsync(); Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task ClearRecentSearchesAsync() { Current.RecentSearches.Clear(); await SaveAsync(); Changed?.Invoke(this, EventArgs.Empty); }
    public async Task AddIncludedFolderAsync(string p) { p = Path.GetFullPath(p); if (!Current.IncludedFolders.Contains(p, StringComparer.OrdinalIgnoreCase)) Current.IncludedFolders.Add(p); await SaveAsync(); Changed?.Invoke(this, EventArgs.Empty); }
    public async Task AddExcludedFolderAsync(string p) { p = Path.GetFullPath(p); if (!Current.ExcludedFolders.Contains(p, StringComparer.OrdinalIgnoreCase)) Current.ExcludedFolders.Add(p); await SaveAsync(); Changed?.Invoke(this, EventArgs.Empty); }
    public async Task RemoveIncludedFolderAsync(string p) { Current.IncludedFolders.RemoveAll(x => x.Equals(p, StringComparison.OrdinalIgnoreCase)); await SaveAsync(); Changed?.Invoke(this, EventArgs.Empty); }
    public async Task RemoveExcludedFolderAsync(string p) { Current.ExcludedFolders.RemoveAll(x => x.Equals(p, StringComparison.OrdinalIgnoreCase)); await SaveAsync(); Changed?.Invoke(this, EventArgs.Empty); }

    public ValueTask DisposeAsync() { saveGate.Dispose(); return ValueTask.CompletedTask; }
}
