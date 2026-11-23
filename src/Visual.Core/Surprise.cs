namespace Visual.Core;

/// <summary>"Sorpresa": random values for a scene's shader inputs and a few effects, in ranges that tend to look good; Undo puts them back.</summary>
public static class Surprise
{
    // ponytail: hand-picked effects and ranges, tuned by eye; never strobe (flashes), mask, opacity or tilt (they hide the picture)
    static readonly (string Name, float Lo, float Hi)[] Looks =
    [
        ("hue", 0, 1), ("saturation", 1.1f, 1.8f), ("contrast", 1.05f, 1.5f), ("kaleido", 3, 8), ("zoom", 0.7f, 1.6f),
        ("chroma", 0.004f, 0.02f), ("vignette", 0.2f, 0.6f), ("feedback", 0.5f, 0.85f), ("tint", 0.6f, 1), ("rotation", 0, 1),
    ];
    static readonly float[] Bars = [4, 8, 16];

    public static void Apply(Scene s, IsfShader isf, Random rng)
    {
        isf.EnsureParams(s.Params);
        s.EnsureEffects();
        s.Undo = (Copy(s.Params), Copy(s.Effects));
        float R(float lo, float hi) => lo + rng.NextSingle() * (hi - lo);
        foreach (var i in isf.Inputs)
        {
            if (!s.Params.TryGetValue(i.Name, out var ps) || rng.NextDouble() < 0.3) continue; // some stay as they are
            switch (i.Type)
            {
                case IsfType.Float:
                    float lo = i.Min[0], hi = i.Max[0], v = R(lo, hi);
                    ps.Mod = new Modifier();
                    ps.Value[0] = v;
                    if (rng.NextDouble() < 0.25 && hi > lo) // now and then it also breathes with the music, around its value
                    {
                        float f = (v - lo) / (hi - lo);
                        ps.Mod = new Modifier { Kind = ModKind.Linear, Ease = Ease.Smooth, Mode = LinearMode.PingPong, Sync = true,
                            SyncBeats = Bars[rng.Next(Bars.Length)], From = Math.Max(0, f - 0.15f), To = Math.Min(1, f + 0.15f) };
                    }
                    break;
                case IsfType.Bool: ps.Value[0] = rng.Next(2); break;
                case IsfType.Long:
                    if (i.Values.Length > 0) ps.Value[0] = i.Values[rng.Next(i.Values.Length)];
                    else ps.Value[0] = MathF.Round(R(i.Min[0], i.Max[0]));
                    break;
                case IsfType.Color when ps.Value.Length >= 3:
                    var c = Hue(rng.NextSingle());
                    (ps.Value[0], ps.Value[1], ps.Value[2]) = (c.R, c.G, c.B);
                    break;
            }
        }
        foreach (var d in EffectDefs.All) s.Effects[d.Name] = new ParamState { Value = [d.Default] };
        foreach (var (name, lo, hi) in Looks.OrderBy(_ => rng.Next()).Take(rng.Next(1, 4)))
        {
            var ps = s.Effects[name];
            ps.Value[0] = name == "kaleido" ? MathF.Round(R(lo, hi)) : R(lo, hi);
            switch (name)
            {
                case "tint": s.Effects["tintHue"].Value[0] = rng.NextSingle(); break;
                case "feedback":
                    s.Effects["feedbackZoom"].Value[0] = R(0.98f, 1.03f);
                    s.Effects["feedbackRot"].Value[0] = R(-0.005f, 0.005f);
                    break;
                case "rotation": // a slow turn in time with the music
                    ps.Mod = new Modifier { Kind = ModKind.Linear, Sync = true, SyncBeats = 16 * Bars[rng.Next(Bars.Length)] / 4 };
                    break;
            }
        }
    }

    /// <summary>Back to before the last Sorpresa; false if there is none.</summary>
    public static bool Undo(Scene s)
    {
        if (s.Undo is not { } u) return false;
        (s.Params, s.Effects, s.Undo) = (u.Params, u.Effects, null);
        return true;
    }

    static Dictionary<string, ParamState> Copy(Dictionary<string, ParamState> d) => d.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());

    static (float R, float G, float B) Hue(float h)
    {
        float C(float o) => Math.Clamp(Math.Abs((h * 6 + o) % 6 - 3) - 1, 0, 1);
        return (C(0), C(4), C(2));
    }
}
