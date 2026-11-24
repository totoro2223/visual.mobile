using System.Text.Json;
using System.Text.Json.Serialization;

namespace Visual.Core;

public enum StrobeRhythm { Linear, Beat }

/// <summary>Project setting: strobe bursts triggered when a band crosses a threshold; each burst plays one of the configurations.</summary>
public sealed class AutoStrobe
{
    public const int MaxConfigs = 16;

    public bool Enabled { get; set; }
    public Band Band { get; set; } = Band.Bass;
    public float Gain { get; set; } = 1;
    /// <summary>The band divided by its recent peak: reacts the same to quiet and loud songs.</summary>
    public bool AutoGain { get; set; }
    public float Threshold { get; set; } = 0.6f;
    public float Probability { get; set; } = 25;  // % per upward crossing
    public float Cooldown { get; set; } = 10;     // s after a burst before another can start
    /// <summary>Which configuration plays next: false = in order, true = at random (not the same twice in a row).</summary>
    public bool Shuffle { get; set; }
    public List<StrobeConfig> Configs { get; set; } = [];

    /// <summary>Projects saved with a single configuration had its fields here.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Old { get; set; }

    static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>At least one configuration; an old project's settings become the first.</summary>
    public void Ensure()
    {
        Configs ??= [];
        Configs.RemoveAll(c => c == null);
        if (Configs.Count == 0)
        {
            StrobeConfig? old = null;
            try { old = Old == null ? null : JsonSerializer.SerializeToElement(Old).Deserialize<StrobeConfig>(Json); }
            catch (Exception e) when (e is JsonException or InvalidOperationException) { }
            Configs.Add(old ?? new());
        }
        Old = null;
    }
}

/// <summary>One kind of burst: how long it lasts, how it flashes, what happens after.</summary>
public sealed class StrobeConfig
{
    public string Name { get; set; } = "";
    /// <summary>In the rotation; off = kept but never picked.</summary>
    public bool On { get; set; } = true;
    public bool RandomDuration { get; set; }
    public float Duration { get; set; } = 2;      // s, when fixed
    public float MinDuration { get; set; } = 1;
    public float MaxDuration { get; set; } = 4;
    public StrobeRhythm Rhythm { get; set; }
    public bool NextScene { get; set; }           // at the end of the burst: false = keep the scene
    // the flash itself (own settings, not the scene's strobe)
    public float Rate { get; set; } = EffectDefs.SafeStrobeHz; // Hz, Linear rhythm; capped like any strobe
    public float Duty { get; set; } = 0.3f;       // fraction of each cycle that is lit
    public float Intensity { get; set; } = 1;
    public int Mode { get; set; }                 // EffectDefs.StrobeModes: 0 white, 1 black, 2 invert, 3 color, 4 random color
    public float Hue { get; set; }                // mode 3: 0 red, 0.33 green, 0.66 blue

    public StrobeConfig Clone() => (StrobeConfig)MemberwiseClone();
}

/// <summary>Runtime state of the bursts. Update once per frame.</summary>
public sealed class AutoStrobeRunner(Random? rng = null)
{
    static readonly StrobeConfig Fallback = new();
    readonly Random rng = rng ?? new();
    bool above;
    double left, cool, sinceHit = double.MaxValue;
    int next = -1;

    public bool Active => left > 0;
    public bool Blocked { get; private set; }
    public double CooldownLeft => Math.Max(0, cool);
    /// <summary>Seconds since the last accepted band hit inside the current burst (for Beat rhythm).</summary>
    public double SinceHit => sinceHit;
    /// <summary>The configuration playing, or the last one played; -1 = none yet.</summary>
    public int Current { get; private set; } = -1;

    /// <summary>The flash to use now: the burst's configuration, else the one coming next (for "hold to strobe").</summary>
    public StrobeConfig Flash(AutoStrobe c) =>
        c.Configs.ElementAtOrDefault(Active ? Current : Upcoming(c)) ?? c.Configs.FirstOrDefault() ?? Fallback;

    /// <summary>The configuration that ended, on the frame Update returns true.</summary>
    public StrobeConfig? Ended(AutoStrobe c) => c.Configs.ElementAtOrDefault(Current);

    /// <summary>Picked ahead so it stays the same until it plays; picked again if it was removed or switched off.</summary>
    public int Upcoming(AutoStrobe c)
    {
        if (next >= 0 && next < c.Configs.Count && c.Configs[next].On) return next;
        var on = Enumerable.Range(0, c.Configs.Count).Where(i => c.Configs[i].On).ToList();
        if (on.Count == 0) return next = -1;
        if (!c.Shuffle) return next = on.Where(i => i > Current).DefaultIfEmpty(on[0]).First();
        if (on.Count > 1) on.Remove(Current);
        return next = on[rng.Next(on.Count)];
    }

    /// <summary>Returns true on the frame a burst ends. <paramref name="blocked"/> (the scene has its own strobe on)
    /// prevents bursts and cuts a running one without counting it as finished.</summary>
    public bool Update(AutoStrobe c, AudioBands bands, float dt, float maxHz, bool blocked = false)
    {
        float level = bands.Get(c.Band, c.AutoGain) * c.Gain;
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
        if (c.Enabled && cross && cool <= 0 && rng.NextDouble() * 100 < c.Probability) Start(c);
        return false;
    }

    /// <summary>Starts a burst with the configuration coming next (also when the auto strobe is off: a drop's burst).</summary>
    public void Start(AutoStrobe c)
    {
        int k = Upcoming(c);
        if (k < 0) return;
        Current = k;
        next = -1;
        var f = c.Configs[k];
        left = f.RandomDuration
            ? f.MinDuration + rng.NextDouble() * Math.Max(0, f.MaxDuration - f.MinDuration)
            : f.Duration;
        sinceHit = 0;
    }
}
