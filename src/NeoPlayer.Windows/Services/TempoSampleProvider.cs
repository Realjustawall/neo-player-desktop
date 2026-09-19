using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NeoPlayer.Windows.Services;

public sealed class TempoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _inner;
    private readonly WaveFormat _reportedFormat;

    public TempoSampleProvider(ISampleProvider source, double speed)
    {
        speed = Math.Clamp(speed, 0.5, 2.0);
        if (source.WaveFormat.SampleRate != 44100) throw new ArgumentException("TempoSampleProvider expects 44.1 kHz input.", nameof(source));
        var intermediateRate = Math.Clamp((int)Math.Round(44100d / speed), 22050, 88200);
        var resampler = new WdlResamplingSampleProvider(source, intermediateRate);
        _reportedFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, source.WaveFormat.Channels);
        _inner = new FormatOverrideSampleProvider(resampler, _reportedFormat);
    }

    public WaveFormat WaveFormat => _reportedFormat;
    public int Read(float[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    private sealed class FormatOverrideSampleProvider(ISampleProvider source, WaveFormat format) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = format;
        public int Read(float[] buffer, int offset, int count) => source.Read(buffer, offset, count);
    }
}
