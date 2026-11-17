namespace Visual.Core;

public enum StrobeRhythm { Linear, Beat }

/// <summary>Project setting: random strobe bursts triggered when a band crosses a threshold.</summary>
public sealed class AutoStrobe
{
    public bool Enabled { get; set; }
    public Band Band { get; set; } = Band.Bass;
    public float Gain { get; set; } = 1;
    public float Threshold { get; set; } = 0.6f;
    public float Probability { get; set; } = 25;  // % per upward crossing
    public bool RandomDuration { get; set; }
    public float Duration { get; set; } = 2;      // s, when fixed
    public float MinDuration { get; set; } = 1;
    public float MaxDuration { get; set; } = 4;
    public float Cooldown { get; set; } = 10;     // s after a burst before another can start
    public StrobeRhythm Rhythm { get; set; }
    public bool NextScene { get; set; }           // at the end of the burst: false = keep the scene
    // the flash itself (own settings, not the scene's strobe)
    public float Rate { get; set; } = EffectDefs.SafeStrobeHz; // Hz, Linear rhythm; capped like any strobe
    public float Duty { get; set; } = 0.3f;       // fraction of each cycle that is lit
    public float Intensity { get; set; } = 1;
    public int Mode { get; set; }                 // 0 white, 1 black, 2 invert
}

/// <summary>Runtime state of the bursts. Update once per frame.</summary>
public sealed class AutoStrobeRunner(Random? rng = null)
{
    readonly Random rng = rng ?? new();
    bool above;
    double left, cool, sinceHit = double.MaxValue;

    public bool Active => left > 0;
    public bool Blocked { get; private set; }
    public double CooldownLeft => Math.Max(0, cool);
    /// <summary>Seconds since the last accepted band hit inside the current burst (for Beat rhythm).</summary>
    public double SinceHit => sinceHit;

    /// <summary>Returns true on the frame a burst ends. <paramref name="blocked"/> (the scene has its own strobe on)
    /// prevents bursts and cuts a running one without counting it as finished.</summary>
    public bool Update(AutoStrobe c, AudioBands bands, float dt, float maxHz, bool blocked = false)
    {
        float level = bands.Raw[(int)c.Band] * c.Gain;
        bool cross = level >= c.Threshold && !above;
        above = level >= c.Threshold;
        sinceHit += dt;
        cool -= dt;
        Blocked = blocked;
        if (blocked) { left = 0; return false; }

        if (Active)
        {
            if (cross && sinceHit >= 1 / maxHz) sinceHit = 0; // hits closer than the safe rate are ignored
            left -= dt;
            if (left > 0) return false;
            cool = c.Cooldown;
            return true;
        }
        if (!c.Enabled || !cross || cool > 0 || rng.NextDouble() * 100 >= c.Probability) return false;
        left = c.RandomDuration
            ? c.MinDuration + rng.NextDouble() * Math.Max(0, c.MaxDuration - c.MinDuration)
            : c.Duration;
        sinceHit = 0;
        return false;
    }
}
