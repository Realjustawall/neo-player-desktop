using NeoPlayer.Windows.Core;

namespace NeoPlayer.Windows.ViewModels;

public sealed class EqBandItem : ObservableObject
{
    private float _gain;
    private readonly Action<int, float> _changed;

    public int Index { get; }
    public string Label { get; }
    public float Gain
    {
        get => _gain;
        set
        {
            var clamped = Math.Clamp(value, -12f, 12f);
            if (!Set(ref _gain, clamped)) return;
            _changed(Index, clamped);
        }
    }

    public EqBandItem(int index, string label, float gain, Action<int, float> changed)
    {
        Index = index;
        Label = label;
        _gain = gain;
        _changed = changed;
    }

    public void SetWithoutCallback(float value)
    {
        _gain = Math.Clamp(value, -12f, 12f);
        Raise(nameof(Gain));
    }
}
