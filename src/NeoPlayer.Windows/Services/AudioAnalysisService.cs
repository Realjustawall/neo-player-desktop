using NAudio.Wave;
using NeoPlayer.Windows.Data;
using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.Services;

public sealed class AudioAnalysisService(NeoDatabase db)
{
    public async Task<AudioAnalysis> AnalyzeAsync(Song song, CancellationToken ct = default)
    {
        using var reader = new MediaFoundationReader(song.Path);
        var provider = reader.ToSampleProvider();
        var buffer = new float[Math.Max(4096, provider.WaveFormat.SampleRate * provider.WaveFormat.Channels / 10)];
        double sumSq = 0, peak = 0; long count = 0; var envelopes = new List<double>(); int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested(); double local = 0;
            for (var i = 0; i < read; i++) { var a = Math.Abs(buffer[i]); peak = Math.Max(peak, a); sumSq += buffer[i] * buffer[i]; local += a; count++; }
            envelopes.Add(local / read); if (envelopes.Count > 6000) break; await Task.Yield();
        }
        var rms = count == 0 ? 0 : Math.Sqrt(sumSq / count); var lufs = rms <= 1e-9 ? -70 : 20 * Math.Log10(rms) - 0.691; var energy = Math.Clamp(rms * 3, 0, 1); var bpm = EstimateBpm(envelopes);
        var result = new AudioAnalysis(rms, peak, lufs, bpm, energy, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); await db.SaveAnalysisAsync(song.Id, result); return result;
    }

    private static double EstimateBpm(IReadOnlyList<double> env)
    {
        if (env.Count < 40) return 0; double best = double.MinValue; int bestLag = 0;
        for (var lag = 20; lag <= Math.Min(120, env.Count / 2); lag++) { double s = 0; for (var i = lag; i < env.Count; i++) s += env[i] * env[i - lag]; if (s > best) { best = s; bestLag = lag; } }
        return bestLag == 0 ? 0 : Math.Clamp(600.0 / bestLag, 60, 200);
    }
}
