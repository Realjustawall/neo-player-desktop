using NAudio.Dsp;

namespace NeoPlayer.Windows.Services;

public sealed class SpectrumAnalyzerService : IDisposable
{
    readonly PlaybackEngine playback;
    readonly object gate = new();
    long lastFrame;
    public event Action<float[]>? Frame;

    public SpectrumAnalyzerService(PlaybackEngine playback)
    {
        this.playback = playback;
        playback.Samples += OnSamples;
    }

    void OnSamples(float[] stereo)
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref lastFrame) < 45) return;
        Interlocked.Exchange(ref lastFrame, now);
        if (stereo.Length < 256) return;
        const int n = 1024;
        var complex = new Complex[n];
        var available = Math.Min(n, stereo.Length / 2);
        for (var i = 0; i < n; i++)
        {
            var sample = i < available ? (stereo[i * 2] + stereo[i * 2 + 1]) * .5f : 0f;
            var window = .5f * (1f - (float)Math.Cos(2 * Math.PI * i / (n - 1)));
            complex[i].X = sample * window;
            complex[i].Y = 0;
        }
        FastFourierTransform.FFT(true, 10, complex);
        const int bars = 48;
        var output = new float[bars];
        var minBin = 1;
        var maxBin = n / 2 - 1;
        for (var b = 0; b < bars; b++)
        {
            var a = (int)Math.Round(minBin * Math.Pow(maxBin / (double)minBin, b / (double)bars));
            var z = (int)Math.Round(minBin * Math.Pow(maxBin / (double)minBin, (b + 1d) / bars));
            z = Math.Max(a + 1, Math.Min(maxBin, z));
            double peak = 0;
            for (var i = a; i <= z; i++)
            {
                var mag = Math.Sqrt(complex[i].X * complex[i].X + complex[i].Y * complex[i].Y);
                peak = Math.Max(peak, mag);
            }
            output[b] = (float)Math.Clamp(Math.Log10(1 + peak * 180) / 2.2, 0, 1);
        }
        Frame?.Invoke(output);
    }

    public void Dispose() => playback.Samples -= OnSamples;
}
