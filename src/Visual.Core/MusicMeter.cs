namespace Visual.Core;

/// <summary>
/// Energy (0..100 %, relative to the last minutes) and tempo of the music.
/// Tempo: the analyzer's onset strength (rise of the spectrum) goes into a curve of 100 values a second, the same at any
/// frame rate; every half second the curve of the last 8 s is compared with itself shifted (autocorrelation) and the
/// shift that repeats best is the beat (70..180 BPM). The beat position is where the curve's hits line up.
/// </summary>
public sealed class MusicMeter
{
    // ponytail: calibration knobs, tuned with synthetic music; retune with real music
    const float EnergyTau = 1.5f, RefTau = 120f, RefFloor = 0.05f, SilenceLevel = 0.01f, SilenceSeconds = 3f, PunchTau = 0.15f;
    const int Rate = 100, Window = 8 * Rate, MinFill = 4 * Rate;
    const float MinBpm = 70, MaxBpm = 180, PriorBpm = 120, PriorOctaves = 1, ConfFull = 0.5f, TapReset = 2f;
    /// <summary>Below this confidence there are no clear hits: the BPM shown is the last good one.</summary>
    public const float MinConfidence = 0.2f;

    public float Energy { get; private set; }     // 0..100
    /// <summary>Like Energy but follows within a fraction of a second: a drop shows here first.</summary>
    public float Punch { get; private set; }
    public float Bpm { get; private set; }        // 0 = not known yet
    public float Confidence { get; private set; } // 0..1: how clearly the curve repeats at that beat
    public bool Locked { get; private set; }      // Tap / Fijar: detection doesn't change the BPM
    public bool Silence { get; private set; }
    /// <summary>Position in the 4-beat bar, 0..4 (the "1" is only right after a Tap or a drop).</summary>
    public double BarPhase => (phase % 4 + 4) % 4;
    public int Beat => (int)BarPhase;
    /// <summary>Taps of the current series (0 when the last one is old).</summary>
    public int TapCount => taps.Count > 0 && time - taps[^1] <= TapReset ? tapCount : 0;

    double time, phase, nextEstimate, slotTime;
    float level, fast, reference = RefFloor, quiet, candidate;
    int tapCount, head, filled;
    readonly float[] curve = new float[Window], x = new float[Window];
    readonly float[] r = new float[2 * (int)MathF.Ceiling(60f * Rate / MinBpm) + 2];
    readonly List<double> taps = [];

    /// <param name="onset">how much the spectrum rose since the last frame (AudioAnalyzer)</param>
    public void Update(AudioBands b, float onset, float dt)
    {
        time += dt;
        float e = 0.6f * b.Mix[0] + 0.3f * b.Mix[1] + 0.1f * b.Mix[2];
        level += (e - level) * (1 - MathF.Exp(-dt / EnergyTau));
        fast += (e - fast) * (1 - MathF.Exp(-dt / PunchTau));
        reference = MathF.Max(MathF.Max(level, reference * MathF.Exp(-dt / RefTau)), RefFloor);
        quiet = e < SilenceLevel ? quiet + dt : 0;
        Silence = quiet > SilenceSeconds;
        Energy = Silence ? 0 : MathF.Min(100, level / reference * 100);
        Punch = Silence ? 0 : MathF.Min(100, fast / reference * 100);

        if (Bpm > 0) phase += dt * Bpm / 60;
        (b.Beats, b.Bpm) = (phase, Bpm);
        // the frame's value fills the 1/100 s slots it covers: the curve doesn't depend on the frame rate
        for (; slotTime + 1.0 / Rate <= time; slotTime += 1.0 / Rate)
        {
            curve[head] = onset;
            head = (head + 1) % Window;
            filled = Math.Min(filled + 1, Window);
        }

        if (time < nextEstimate || Locked) return;
        nextEstimate = time + 0.5;
        if (filled < MinFill) { Confidence = 0; return; }
        float est = Estimate(out float conf, out double sinceBeat);
        Confidence = conf;
        if (conf < MinConfidence) return; // keep the last tempo
        if (Bpm > 0 && MathF.Abs(est - Bpm) / Bpm < 0.03f) Bpm += (est - Bpm) * 0.3f;
        else if (candidate > 0 && MathF.Abs(est - candidate) / candidate < 0.03f) Bpm = est; // a new tempo, twice in a row
        else { candidate = est; return; }
        // the beat goes where the hits are: half way each time, so one odd window doesn't jump it
        double off = sinceBeat * Bpm / 60 - (phase - Math.Floor(phase));
        off -= Math.Round(off);
        phase += off * 0.5;
    }

    /// <summary>Beat of the last 8 s: BPM, confidence 0..1, and seconds since the last beat.</summary>
    float Estimate(out float conf, out double sinceBeat)
    {
        int n = filled;
        float mean = 0;
        for (int i = 0; i < n; i++) mean += x[i] = curve[(head - n + i + Window) % Window];
        mean /= n;
        for (int i = 0; i < n; i++) x[i] -= mean;

        int lo = (int)(60f * Rate / MaxBpm), hi = (int)MathF.Ceiling(60f * Rate / MinBpm);
        Array.Clear(r);
        for (int lag = 0; lag <= 2 * hi + 1; lag = lag == 0 ? lo - 1 : lag + 1) // 0, then the beats and their doubles
        {
            float s = 0;
            for (int i = lag; i < n; i++) s += x[i] * x[i - lag];
            r[lag] = s / (n - lag);
        }
        sinceBeat = 0;
        conf = 0;
        if (r[0] <= 1e-9f) return Bpm;

        // each beat also counts what repeats at twice its length, and tempos near 120 a little more: 90 doesn't read as 180
        float Score(int lag) => (r[lag] + 0.5f * r[2 * lag]) * MathF.Exp(-0.5f * MathF.Pow(MathF.Log2(60f * Rate / lag / PriorBpm) / PriorOctaves, 2));
        int best = lo;
        for (int lag = lo; lag <= hi; lag++) if (Score(lag) > Score(best)) best = lag;
        conf = Math.Clamp(r[best] / r[0] / ConfFull, 0, 1);
        // between whole slots: the top of the parabola through the three scores
        double period = best;
        if (best > lo && best < hi)
        {
            float a = Score(best - 1), b = Score(best), c = Score(best + 1), d = a - 2 * b + c;
            if (d < 0) period += Math.Clamp(0.5 * (a - c) / d, -0.5, 0.5);
        }

        // where the hits line up: the offset from now whose beats, every period back, add up the most
        double bestSum = double.MinValue;
        for (int off = 0; off < period; off++)
        {
            double s = 0;
            for (double k = n - 1 - off; k >= 0; k -= period) s += x[(int)k];
            if (s > bestSum) { bestSum = s; sinceBeat = off / (double)Rate; }
        }
        return (float)(60 * Rate / period);
    }

    /// <summary>A tap on the beat; from the 4th tap of a series it sets (and fixes) the tempo, the first tap being the "1".</summary>
    public void Tap()
    {
        if (TapCount == 0) { taps.Clear(); tapCount = 0; }
        taps.Add(time);
        tapCount++;
        if (taps.Count > 8) taps.RemoveAt(0);
        if (tapCount >= 4) SetTempo((float)(60 * (taps.Count - 1) / (taps[^1] - taps[0])), (tapCount - 1) % 4);
    }

    /// <summary>Fixed tempo with the current moment on <paramref name="beat"/> (0 = the "1"); the phone's Tap ends here.</summary>
    public void SetTempo(float bpm, int beat)
    {
        Bpm = Math.Clamp(bpm, 30, 300);
        phase = Math.Clamp(beat, 0, 3);
        Locked = true;
        Confidence = 1;
    }

    public void Lock() { if (Bpm > 0) Locked = true; }

    /// <summary>Back to detecting the tempo from the music.</summary>
    public void Unlock() { Locked = false; candidate = 0; }

    /// <summary>A "1" was <paramref name="secondsAgo"/> (a drop lands on one): the bar count moves so the nearest beat then is it. A Tap's "1" is kept.</summary>
    public void MarkDownbeat(double secondsAgo)
    {
        if (Locked || Bpm == 0) return;
        double then = phase - secondsAgo * Bpm / 60, one = Math.Round(then);
        phase += one - (one % 4 + 4) % 4 - then;
    }
}
