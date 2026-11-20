namespace Visual.Core;

/// <summary>LinearAudio = a Linear sweep whose speed follows an audio band (still below the threshold).</summary>
public enum ModKind { Constant, Linear, Audio, LinearAudio }
/// <summary>At the edge: start over, come back playing the curve backwards, or come back with the curve flipped (same shape going down).</summary>
public enum LinearMode { Loop, PingPong, Mirror }
/// <summary>Shape of a Linear sweep inside its range (see Curves).</summary>
public enum Ease { Linear, Smooth, In, Out, InStrong, OutStrong, Expo, Circ, Wave, Steps, RandomSteps, RandomSmooth, Bounce, Back, Elastic }
/// <summary>Melody = level of the sustained notes (not the drums); Note = how high the main note is (0..1).</summary>
public enum Band { Bass, Mid, High, Melody, Note }

/// <summary>How a float parameter moves: fixed, sweeping over time, or following an audio band.</summary>
public sealed class Modifier
{
    public ModKind Kind { get; set; }

    // Linear
    public float Speed { get; set; } = 0.1f; // full sweeps of the range per second
    public LinearMode Mode { get; set; }
    public Ease Ease { get; set; }
    public int Steps { get; set; } = 4; // Steps / random curves: values per sweep, 2..16

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

    // LinearAudio: where the sweep is, advanced each frame (not saved; starts again from the slider when the scene does)
    double phase, last = double.NaN;

    public Modifier Clone() => (Modifier)MemberwiseClone();

    /// <summary>The working range in the parameter's units, low to high.</summary>
    public (float Lo, float Hi) Range(float min, float max)
    {
        float f = float.IsFinite(From) ? Math.Clamp(From, 0, 1) : 0, t = float.IsFinite(To) ? Math.Clamp(To, 0, 1) : 1;
        float a = min + Math.Min(f, t) * (max - min), b = min + Math.Max(f, t) * (max - min);
        return (Math.Min(a, b), Math.Max(a, b));
    }

    /// <summary>Band level past the threshold, 0..1.</summary>
    float Level(AudioBands bands) =>
        Math.Clamp((bands.Get(Band, AutoGain) * Gain - Threshold) / (1 - Math.Min(Threshold, 0.99f)), 0, 1);

    public float Apply(float value, float min, float max, double time, AudioBands bands)
    {
        var (lo, hi) = Range(min, max);
        float range = hi - lo;
        switch (Kind)
        {
            case ModKind.Linear or ModKind.LinearAudio:
            {
                double start = range != 0 ? (Math.Clamp(value, lo, hi) - lo) / range : 0;
                int steps = Math.Clamp(Steps, 2, 16);
                if (Invert) start = 1 - start;
                double ph = Curves.Find(Ease, steps, start); // starts at the slider value either way
                if (Kind == ModKind.Linear) ph += time * Speed;
                else
                {
                    if (!(time >= last)) phase = ph; // first frame, or the scene started again
                    else phase += Math.Min(time - last, 0.1) * Speed * Level(bands); // same time twice (UI + render) = no step
                    last = time;
                    ph = phase;
                }
                double x = ph - Math.Floor(ph);
                double f = Ease is Ease.RandomSteps or Ease.RandomSmooth ? Curves.Random(Ease, ph * steps) // random values don't loop or bounce
                    : Mode == LinearMode.PingPong ? Curves.Of(Ease, steps, 1 - Math.Abs(1 - 2 * (ph * 0.5 - Math.Floor(ph * 0.5))))
                    : Mode == LinearMode.Mirror && ((long)Math.Floor(ph) & 1) == 1 ? 1 - Curves.Of(Ease, steps, x)
                    : Curves.Of(Ease, steps, x);
                if (Invert) f = 1 - f;
                return lo + (float)f * range;
            }

            case ModKind.Audio:
                float a = Level(bands) * Math.Abs(Amount);
                // From is 0 % and To is 100 %, the slider value isn't used; inverted (or negative Intensity) rests at To and goes down
                return (Amount < 0) != Invert ? hi - a * range : lo + a * range;

            default:
                return Math.Clamp(value, lo, hi);
        }
    }
}

/// <summary>Sweep curves: position 0..1 in, value 0..1 out (always inside the range, also Back and Elastic).</summary>
public static class Curves
{
    public static readonly string[] Names = ["Lineal", "Suave", "Acelera", "Frena", "Acelera fuerte", "Frena fuerte", "Exponencial",
        "Circular", "Ola", "Escalones", "Al azar por pasos", "Al azar suave", "Rebote", "Pasarse", "Elástico"];

    /// <summary>Always going up: the start position for a slider value can be looked up on them.</summary>
    public static bool Rising(Ease e) => e is not (Ease.Wave or Ease.RandomSteps or Ease.RandomSmooth or Ease.Bounce or Ease.Back or Ease.Elastic);

    public static double Of(Ease e, int steps, double x) => e switch
    {
        Ease.Smooth => 0.5 - 0.5 * Math.Cos(Math.PI * x),
        Ease.In => x * x * x,
        Ease.Out => 1 - Math.Pow(1 - x, 3),
        Ease.InStrong => Math.Pow(x, 5),
        Ease.OutStrong => 1 - Math.Pow(1 - x, 5),
        Ease.Expo => (Math.Pow(2, 10 * x) - 1) / 1023,
        Ease.Circ => x < 0.5 ? (1 - Math.Sqrt(1 - 4 * x * x)) / 2 : (1 + Math.Sqrt(Math.Max(0, 1 - Math.Pow(2 - 2 * x, 2)))) / 2,
        Ease.Wave => 0.5 - 0.5 * Math.Cos(2 * Math.PI * x),
        Ease.Steps => Math.Min(Math.Floor(x * steps), steps - 1) / (steps - 1),
        Ease.RandomSteps or Ease.RandomSmooth => Random(e, x * steps),
        Ease.Bounce => Bounce(x),
        Ease.Back => (BackRaw(x) - backLo) / (backHi - backLo),
        Ease.Elastic => (ElasticRaw(x) - elasticLo) / (elasticHi - elasticLo),
        _ => x,
    };

    /// <summary>Where on the curve the value <paramref name="y"/> is (rising curves); else y itself.</summary>
    public static double Find(Ease e, int steps, double y)
    {
        if (!Rising(e) || e == Ease.Linear) return Math.Min(y, 1 - 1e-9); // 1 would wrap to 0 when looping
        double a = 0, b = 1;
        for (int i = 0; i < 30; i++) { double m = (a + b) / 2; if (Of(e, steps, m) < y) a = m; else b = m; }
        return Math.Min(b, 1 - 1e-9);
    }

    /// <summary>Random value for each whole <paramref name="u"/>; RandomSmooth glides between them.</summary>
    public static double Random(Ease e, double u)
    {
        double k = Math.Floor(u), t = u - k;
        if (e == Ease.RandomSteps) return Hash(k);
        t = t * t * (3 - 2 * t);
        return Hash(k) + (Hash(k + 1) - Hash(k)) * t;
    }

    static double Hash(double k) { double s = Math.Sin(k * 12.9898 + 78.233) * 43758.5453; return s - Math.Floor(s); }

    static double Bounce(double x)
    {
        const double n = 7.5625, d = 2.75;
        return x < 1 / d ? n * x * x
            : x < 2 / d ? n * (x -= 1.5 / d) * x + 0.75
            : x < 2.5 / d ? n * (x -= 2.25 / d) * x + 0.9375
            : n * (x -= 2.625 / d) * x + 0.984375;
    }

    static double BackRaw(double x)
    {
        const double c = 1.70158 * 1.525;
        return x < 0.5 ? Math.Pow(2 * x, 2) * ((c + 1) * 2 * x - c) / 2 : (Math.Pow(2 * x - 2, 2) * ((c + 1) * (2 * x - 2) + c) + 2) / 2;
    }

    static double ElasticRaw(double x) => x <= 0 ? 0 : x >= 1 ? 1 : Math.Pow(2, -10 * x) * Math.Sin((10 * x - 0.75) * 2 * Math.PI / 3) + 1;

    // the overshoot is squeezed in: these go out of 0..1, so they are scaled to their own low / high
    static readonly (double, double) backBounds = Bounds(BackRaw), elasticBounds = Bounds(ElasticRaw);
    static readonly double backLo = backBounds.Item1, backHi = backBounds.Item2, elasticLo = elasticBounds.Item1, elasticHi = elasticBounds.Item2;
    static (double Lo, double Hi) Bounds(Func<double, double> f)
    {
        double lo = 0, hi = 1;
        for (int i = 0; i <= 1000; i++) { double v = f(i / 1000.0); lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
        return (lo, hi);
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
