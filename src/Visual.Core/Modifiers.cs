namespace Visual.Core;

public enum ModKind { Constant, Linear, Audio }
public enum LinearMode { Loop, PingPong }
public enum Band { Bass, Mid, High }

/// <summary>How a float parameter moves: fixed, sweeping over time, or following an audio band.</summary>
public sealed class Modifier
{
    public ModKind Kind { get; set; }

    // Linear
    public float Speed { get; set; } = 0.1f; // full sweeps of the range per second
    public LinearMode Mode { get; set; }

    // Audio
    public Band Band { get; set; }
    public float Amount { get; set; } = 0.5f;   // -1..1, fraction of the range added at full band level
    public float Gain { get; set; } = 1f;       // 0.1..10
    public float Threshold { get; set; }        // 0..0.95, band level below this is ignored
    public bool AutoGain { get; set; }

    // Range: the part of the parameter's range it works in, as fractions (0 = its min, 1 = its max); also limits the slider
    public float From { get; set; }
    public float To { get; set; } = 1;
    /// <summary>Linear runs backwards; Audio pushes the other way (more sound = lower value).</summary>
    public bool Invert { get; set; }

    public Modifier Clone() => (Modifier)MemberwiseClone();

    /// <summary>The working range in the parameter's units, low to high.</summary>
    public (float Lo, float Hi) Range(float min, float max)
    {
        float f = float.IsFinite(From) ? Math.Clamp(From, 0, 1) : 0, t = float.IsFinite(To) ? Math.Clamp(To, 0, 1) : 1;
        float a = min + Math.Min(f, t) * (max - min), b = min + Math.Max(f, t) * (max - min);
        return (Math.Min(a, b), Math.Max(a, b));
    }

    public float Apply(float value, float min, float max, double time, AudioBands bands)
    {
        var (lo, hi) = Range(min, max);
        float range = hi - lo;
        switch (Kind)
        {
            case ModKind.Linear:
                double start = range != 0 ? (Math.Clamp(value, lo, hi) - lo) / range : 0;
                double ph = (Invert ? 1 - start : start) + time * Speed; // starts at the slider value either way
                double f = Mode == LinearMode.Loop
                    ? ph - Math.Floor(ph)
                    : 1 - Math.Abs(1 - 2 * (ph * 0.5 - Math.Floor(ph * 0.5)));
                if (Invert) f = 1 - f;
                return lo + (float)f * range;

            case ModKind.Audio:
                float a = bands.Get(Band, AutoGain) * Gain;
                a = Math.Clamp((a - Threshold) / (1 - Math.Min(Threshold, 0.99f)), 0, 1);
                return Math.Clamp(value + a * Amount * (Invert ? -range : range), lo, hi);

            default:
                return Math.Clamp(value, lo, hi);
        }
    }
}

public sealed class ParamState
{
    public float[] Value { get; set; } = [0f];
    public Modifier Mod { get; set; } = new();
    /// <summary>Effects only: switched off, acts as its default but keeps the settings.</summary>
    public bool Off { get; set; }

    public ParamState Clone() => new() { Value = (float[])Value.Clone(), Mod = Mod.Clone(), Off = Off };
}
