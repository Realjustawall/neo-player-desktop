using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NeoPlayer.Windows.Services;

public sealed class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly BiQuadFilter[,] _filters;
    private readonly float[] _gains = new float[10];
    private readonly float[] _bands = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
    private float _bassDb;
    private float _width = 1f;
    private float _preamp = 1f;

    public EqualizerSampleProvider(ISampleProvider source)
    {
        _source = source;
        _filters = new BiQuadFilter[source.WaveFormat.Channels, _bands.Length + 1];
        Rebuild();
    }

    public WaveFormat WaveFormat => _source.WaveFormat;
    public IReadOnlyList<float> Gains => _gains;

    public void SetBand(int index, float db) { if ((uint)index >= _gains.Length) return; _gains[index] = Math.Clamp(db, -12, 12); Rebuild(); }
    public void SetBass(float db) { _bassDb = Math.Clamp(db, -12, 12); Rebuild(); }
    public void SetStereoWidth(float width) => _width = Math.Clamp(width, 0, 2);
    public void SetPreampDb(double db)
    {
        db = Math.Clamp(db, -18, 6);
        _preamp = (float)Math.Pow(10, db / 20d);
    }

    private void Rebuild()
    {
        var sr = WaveFormat.SampleRate;
        for (var ch = 0; ch < WaveFormat.Channels; ch++)
        {
            for (var i = 0; i < _bands.Length; i++) _filters[ch, i] = BiQuadFilter.PeakingEQ(sr, Math.Min(_bands[i], sr * 0.45f), 0.9f, _gains[i]);
            _filters[ch, _bands.Length] = BiQuadFilter.LowShelf(sr, 120, 0.8f, _bassDb);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        var channels = WaveFormat.Channels;
        for (var n = 0; n < read; n++)
        {
            var ch = n % channels;
            var sample = buffer[offset + n] * _preamp;
            for (var i = 0; i < _bands.Length + 1; i++) sample = _filters[ch, i].Transform(sample);
            buffer[offset + n] = Math.Clamp(sample, -1f, 1f);
        }
        if (channels == 2 && Math.Abs(_width - 1f) > 0.001f)
        {
            for (var n = 0; n + 1 < read; n += 2)
            {
                var l = buffer[offset + n]; var r = buffer[offset + n + 1];
                var mid = (l + r) * 0.5f; var side = (l - r) * 0.5f * _width;
                buffer[offset + n] = Math.Clamp(mid + side, -1, 1); buffer[offset + n + 1] = Math.Clamp(mid - side, -1, 1);
            }
        }
        return read;
    }
}
