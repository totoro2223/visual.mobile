namespace Visual.Core;

/// <summary>Project setting: scene changes that follow the music.</summary>
public sealed class AutoDj
{
    public bool Enabled { get; set; } = true;
    public int Bars { get; set; } = 16;      // a change every this many bars
    public bool Drops { get; set; } = true;  // cut at once on a drop; change on the next "1" when the energy jumps
}

/// <summary>
/// Runtime of the Auto DJ. While the music has a clear beat it changes scene on the "1" of the bar every Bars bars,
/// to one whose intensity (Scene.Level) matches the energy; a drop cuts at once to a strong one.
/// Without a beat (silence, no music) it does nothing and the scenes' durations work as usual.
/// </summary>
public sealed class AutoDjRunner(Random? rng = null)
{
    // ponytail: calibration knobs (energy points 0..100, seconds); tuned with synthetic music, retune with real sets
    const float SoftBelow = 35, StrongFrom = 70, DropRise = 45, DropHold = 0.15f, JumpBy = 35, LowRise = 5, TrustSeconds = 20;
    const int DropMinBars = 2, JumpMinBars = 4;

    readonly Random rng = rng ?? new();
    int scene = -1, last = -1, jump; // jump: -1 energy fell, +1 rose (change on the next "1")
    double prevPhase;
    float sinceSure = float.MaxValue, low = 100, sceneEnergy, dropFor;
    bool armed;

    /// <summary>Driving the changes now (enabled, music with a clear beat, two scenes or more).</summary>
    public bool Active { get; private set; }
    /// <summary>Whole bars played in the current scene.</summary>
    public int Bar { get; private set; }
    /// <summary>Why it changes on the next "1" before the bar count is up; "" = no reason.</summary>
    public string Pending { get; private set; } = "";
    public string LastChange { get; private set; } = "";

    public string Status(AutoDj c, Project p) =>
        !c.Enabled ? "Apagado"
        : p.Scenes.Count < 2 ? "Hace falta más de una escena"
        : !Active ? "Esperando música con ritmo claro; mientras, cada escena usa su duración"
        : $"Compás {Bar + 1}/{c.Bars}" + (Pending != "" ? $" · cambia en el próximo 1 ({Pending})" : "");

    public void Update(AutoDj c, Player pl, MusicMeter m, float dt)
    {
        var sc = pl.Project.Scenes;
        if (pl.Current != scene) Reset(pl, m.Energy); // changed by hand, by duration or by the auto strobe
        sinceSure = m.Locked || m.Confidence >= 0.5f ? 0 : sinceSure + dt;
        Active = c.Enabled && m.Bpm > 0 && !m.Silence && sinceSure < TrustSeconds && sc.Count > 1;
        pl.AutoDriving = Active;
        double ph = m.BarPhase;
        bool downbeat = ph < prevPhase - 2; // 3.9 -> 0.1
        prevPhase = ph;

        // drop: the fast energy jumps well above the recent low (a break or build-up) and stays there a moment
        // (only measured while active: music starting after silence isn't a drop)
        low = Active ? MathF.Min(m.Energy, low + LowRise * dt) : 100;
        if (m.Punch - low < DropRise / 2) armed = true;
        dropFor = m.Punch - low > DropRise && m.Punch >= StrongFrom ? dropFor + dt : 0;
        if (!Active) return;

        if (c.Drops && armed && dropFor >= DropHold && Bar >= DropMinBars)
        {
            armed = false;
            m.MarkDownbeat(dropFor); // the drop started when the jump did, on a "1"
            prevPhase = m.BarPhase;
            Change(pl, 3, "drop", m.Punch);
            return;
        }
        if (c.Drops && jump == 0 && Bar >= JumpMinBars)
            jump = m.Energy < sceneEnergy - JumpBy ? -1 : m.Energy > sceneEnergy + JumpBy ? 1 : 0;
        Pending = jump < 0 ? "bajó la energía" : jump > 0 ? "subió la energía" : "";
        if (!downbeat) return;
        Bar++;
        if (jump == 0 && Bar < c.Bars) return;
        // the slow energy is still on its way after a jump: the fast one says where it goes
        float e = jump < 0 ? MathF.Min(m.Energy, m.Punch) : jump > 0 ? MathF.Max(m.Energy, m.Punch) : m.Energy;
        Change(pl, e < SoftBelow ? 1 : e < StrongFrom ? 2 : 3, Pending != "" ? Pending : $"{Bar} compases", e);
    }

    /// <summary>To a scene of the level (the nearest one there is), at random, not the current or the one before.</summary>
    void Change(Player pl, int level, string why, float energy)
    {
        var sc = pl.Project.Scenes;
        var others = Enumerable.Range(0, sc.Count).Where(i => i != pl.Current).ToList();
        int gap = others.Min(i => Math.Abs(sc[i].Level - level));
        var pick = others.Where(i => Math.Abs(sc[i].Level - level) == gap).ToList();
        if (pick.Count > 1) pick.Remove(last);
        pl.GoTo(pick[rng.Next(pick.Count)]);
        LastChange = $"{why} → {Scene.LevelNames[sc[pl.Current].Level].ToLowerInvariant()}: {sc[pl.Current].Title}";
        Reset(pl, energy);
    }

    void Reset(Player pl, float energy)
    {
        last = scene;
        scene = pl.Current;
        Bar = 0;
        jump = 0;
        Pending = "";
        sceneEnergy = energy;
    }
}
