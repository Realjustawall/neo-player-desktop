using System.Diagnostics;

namespace NeoPlayer.Windows;

public static class FfmpegLocator
{
    static string? cached;
    public static string Require()
    {
        if (cached is not null) return cached;
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("NEO_FFMPEG"),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")
        };
        foreach (var candidate in candidates)
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return cached = candidate;
        try
        {
            using var p = Process.Start(new ProcessStartInfo("where.exe", "ffmpeg.exe") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            var line = p?.StandardOutput.ReadLine(); p?.WaitForExit(1000);
            if (!string.IsNullOrWhiteSpace(line) && File.Exists(line)) return cached = line;
        }
        catch { }
        // Let Windows resolve PATH as a final fallback. Process.Start will provide the actual failure if absent.
        return cached = "ffmpeg.exe";
    }
}
