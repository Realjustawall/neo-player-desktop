using NeoPlayer.Windows.Models;
using System.Diagnostics;
using System.Text.Json;

namespace NeoPlayer.Windows.Services;

public sealed class WaveformService
{
    public async Task<float[]> GetAsync(Song song, int points = 512, CancellationToken ct = default)
    {
        points = Math.Clamp(points, 32, 4096);
        AppPaths.Ensure();
        var cache = Path.Combine(AppPaths.AnalysisCache, $"wave-{song.Id}-{song.DateModified}-{points}.json");
        if (File.Exists(cache))
        {
            try { return JsonSerializer.Deserialize<float[]>(await File.ReadAllTextAsync(cache, ct)) ?? []; }
            catch { try { File.Delete(cache); } catch { } }
        }

        var psi = FfmpegProcess("-hide_banner", "-loglevel", "error", "-i", song.Path, "-vn", "-ac", "1", "-ar", "8000", "-f", "f32le", "pipe:1");
        using var p = new Process { StartInfo = psi };
        p.Start();
        using var ms = new MemoryStream();
        await p.StandardOutput.BaseStream.CopyToAsync(ms, ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0 && ms.Length == 0) throw new InvalidOperationException("FFmpeg could not decode this track for waveform analysis.");

        var bytes = ms.ToArray();
        var samples = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 4);
        var output = new float[points];
        if (samples.Length > 0)
        {
            for (var i = 0; i < points; i++)
            {
                var a = (int)((long)i * samples.Length / points);
                var b = (int)((long)(i + 1) * samples.Length / points);
                b = Math.Max(a + 1, Math.Min(samples.Length, b));
                float peak = 0;
                for (var j = a; j < b; j++) peak = Math.Max(peak, Math.Abs(samples[j]));
                output[i] = peak;
            }
        }
        await File.WriteAllTextAsync(cache, JsonSerializer.Serialize(output), ct);
        return output;
    }

    static ProcessStartInfo FfmpegProcess(params string[] args)
    {
        var psi = new ProcessStartInfo(FfmpegLocator.Require()) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        return psi;
    }
}
