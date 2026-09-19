using Microsoft.Win32;

namespace NeoPlayer.Windows.Services;

public sealed class WindowsIntegrationService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NEO Player";

    public bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch { return false; }
    }

    public void SetStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot resolve executable path.");
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static void OpenContainingFolder(string path)
    {
        if (!File.Exists(path)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true
        });
    }
}
