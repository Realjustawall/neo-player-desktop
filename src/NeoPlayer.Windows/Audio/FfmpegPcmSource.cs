using NAudio.Wave;
using System.Diagnostics;

namespace NeoPlayer.Windows.Audio;

public sealed class FfmpegPcmSource : ISampleProvider, IDisposable
{
    readonly string path;
    readonly int rate;
    readonly int channels;
    Process? process;
    Stream? stream;
    double startSeconds;
    float speed = 1;
    long samplesRead;
    bool disposed;

    public WaveFormat WaveFormat { get; }
    public bool Ended { get; private set; }
    public double PositionSeconds => startSeconds + samplesRead / (double)(rate * channels) * speed;

    public FfmpegPcmSource(string path, int rate = 48000, int channels = 2, double startSeconds = 0, float playbackSpeed = 1)
    {
        this.path = path; this.rate = rate; this.channels = channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
        Restart(startSeconds, playbackSpeed);
    }

    public void Restart(double seconds, float playbackSpeed)
    {
        Stop();
        startSeconds = Math.Max(0, seconds); speed = Math.Clamp(playbackSpeed, .25f, 3f); samplesRead = 0; Ended = false;
        var psi = new ProcessStartInfo(FfmpegLocator.Require()) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[]
        {
            "-hide_banner","-loglevel","error","-ss",startSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),"-i",path,
            "-vn","-sn","-dn","-af",TempoFilter(speed),"-f","f32le","-ac",channels.ToString(),"-ar",rate.ToString(),"pipe:1"
        }) psi.ArgumentList.Add(arg);
        process = new Process { StartInfo = psi }; process.Start(); stream = process.StandardOutput.BaseStream;
    }

    static string TempoFilter(float value)
    {
        // FFmpeg atempo supports 0.5..100. Compose filters for values below 0.5.
        if (value >= .5f) return $"atempo={value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var remainder = value;
        var filters = new List<string>();
        while (remainder < .5f) { filters.Add("atempo=0.5"); remainder /= .5f; }
        filters.Add($"atempo={remainder.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        return string.Join(',', filters);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (disposed || stream is null || Ended) return 0;
        var bytesNeeded = count * sizeof(float); var bytes = new byte[bytesNeeded]; var got = 0;
        try
        {
            while (got < bytesNeeded)
            {
                var n = stream.Read(bytes, got, bytesNeeded - got); if (n <= 0) break; got += n;
            }
        }
        catch { got = 0; }
        var samples = got / sizeof(float);
        if (samples > 0) Buffer.BlockCopy(bytes, 0, buffer, offset * sizeof(float), samples * sizeof(float));
        samplesRead += samples;
        if (samples == 0) Ended = true;
        return samples;
    }

    void Stop()
    {
        try { stream?.Dispose(); } catch { }
        stream = null;
        try { if (process is { HasExited: false }) process.Kill(true); } catch { }
        try { process?.Dispose(); } catch { }
        process = null;
    }

    public void Dispose() { if (disposed) return; disposed = true; Stop(); }
}
