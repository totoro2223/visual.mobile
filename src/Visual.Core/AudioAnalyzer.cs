namespace Visual.Core;

public sealed class AudioBands
{
    public const int Count = 5;                       // Band: bass, mid, high, melody (level), note (pitch 0..1)
    public readonly float[] Raw = new float[Count];   // smoothed level, roughly 0..1 for loud music
    public readonly float[] Norm = new float[Count];  // Raw divided by its recent peak (auto-gain); the note is the same in both
    /// <summary>For the music's energy (Auto DJ): bass/mid/high as first measured, without tilt or sensitivity, so its calibration holds.</summary>
    public readonly float[] Mix = new float[3];
    /// <summary>Beats counted by MusicMeter (Bar = 4) and its tempo, 0 = not known yet: modifiers in time with the music.</summary>
    public double Beats;
    public float Bpm;
    public float Get(Band b, bool autoGain) => autoGain ? Norm[(int)b] : Raw[(int)b];
}

/// <summary>
/// Platform-neutral analysis: push mono samples from any thread, call Update once per frame.
/// Produces ISF audio textures (Wave, Spectrum), bass/mid/high bands, the melody's level and its note.
/// </summary>
public sealed class AudioAnalyzer
{
    public const int FftSize = 2048;
    // ponytail: calibration knobs, tuned by ear; the per-band Sensibilidad and per-modifier gain cover the rest
    const float BandScale = 2.4f, DbFloor = -70f, AttackTau = 0.015f, PeakTau = 5f, PeakFloor = 0.05f;
    static readonly float[] BandEdges = [20, 250, 2000, 10000], MixEdges = [20, 250, 4000, 16000];
    const float MixScale = 4f;
    /// <summary>Music has less energy up high: mid, high and melody are lifted so loud music reaches ~1 in each
    /// (bass, mid, high, melody; equal for pink noise without it).</summary>
    public static readonly float[] Tilt = [1, 1.6f, 3.2f, 1f];
    const float FluxTopHz = 4000, FluxBassWeight = 4; // the bass has few bins: weighted so the kick counts like the rest
    // melody: per bin, the part that lasts (median over the last frames) and isn't a broadband hit (median over nearby bins)
    // ponytail: the median spans frames, not seconds (150 ms at 60 fps, 300 ms at 30); fine for notes vs drum hits
    const int Hist = 9, Spread = 8;
    const float TonalLo = 60, TonalHi = 8000, MelodyLo = 200, MelodyHi = 4000, NoteTau = 0.3f, NoteMinLevel = 0.03f;
    public const float NoteLo = 80, NoteHi = 1500; // Hz of the note band's 0 and 1

    public readonly byte[] Wave = new byte[1024];          // 128 = silence; ISF "audio"
    public readonly byte[] Spectrum = new byte[FftSize / 2]; // dB-scaled 0..255; ISF "audioFFT"
    readonly byte[] prevSpectrum = new byte[FftSize / 2];
    public readonly AudioBands Bands = new();
    public readonly MusicMeter Music = new();
    public float InputGain = 1f;
    /// <summary>Seconds (time constant) the bands take to fall after a hit; lower = drops faster between beats.</summary>
    public float Release = 0.15f;
    /// <summary>Per band (bass, mid, high, melody): multiplies the level; missing = 1.</summary>
    public float[] Sensitivity = [];
    /// <summary>Seconds the melody averages over (it falls in this time, rises in 40 % of it): higher = moves smoother.</summary>
    public float MelodySmooth = 0.8f;
    public int SampleRate = 48000;

    readonly float[] ring = new float[FftSize];
    readonly float[] re = new float[FftSize], im = new float[FftSize], window = new float[FftSize];
    readonly float[] mags = new float[FftSize / 2], tonal = new float[FftSize / 2];
    readonly float[][] hist = Enumerable.Range(0, Hist).Select(_ => new float[FftSize / 2]).ToArray();
    readonly float[] peak = Enumerable.Repeat(PeakFloor, AudioBands.Count).ToArray();
    readonly object gate = new();
    int ringPos, histPos;
    long pushed, seen;
    float stale, noteSemis = -1;

    public AudioAnalyzer()
    {
        for (int i = 0; i < FftSize; i++) window[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / (FftSize - 1));
    }

    public void Push(ReadOnlySpan<float> mono)
    {
        lock (gate)
        {
            foreach (var s in mono) { ring[ringPos] = s; ringPos = (ringPos + 1) % FftSize; }
            pushed += mono.Length;
        }
    }

    public void Update(float dt)
    {
        lock (gate)
        {
            if (pushed == seen) stale += dt; else { stale = 0; seen = pushed; }
            if (stale > 0.25f) Array.Clear(ring); // WASAPI loopback sends nothing while silent: don't freeze on the last buffer
            for (int i = 0; i < FftSize; i++) re[i] = ring[(ringPos + i) % FftSize];
        }

        int w0 = FftSize - Wave.Length;
        for (int i = 0; i < Wave.Length; i++)
            Wave[i] = (byte)Math.Clamp(128 + re[w0 + i] * InputGain * 127, 0, 255);

        for (int i = 0; i < FftSize; i++) { re[i] *= window[i] * InputGain; im[i] = 0; }
        Fft(re, im);

        Span<float> sum = stackalloc float[AudioBands.Count], mix = stackalloc float[3];
        Span<int> mixCount = stackalloc int[3];
        float binHz = SampleRate / (float)FftSize, flux = 0;
        for (int k = 0; k < Spectrum.Length; k++)
        {
            float mag = MathF.Sqrt(re[k] * re[k] + im[k] * im[k]) * 4f / FftSize; // 1.0 = full-scale sine (Hann)
            mags[k] = mag;
            float db = 20 * MathF.Log10(mag + 1e-9f);
            Spectrum[k] = (byte)Math.Clamp((db - DbFloor) / -DbFloor * 255, 0, 255);
            float hz = k * binHz;
            for (int b = 0; b < 3; b++)
            {
                if (hz >= BandEdges[b] && hz < BandEdges[b + 1]) sum[b] += mag * mag;
                if (hz >= MixEdges[b] && hz < MixEdges[b + 1]) { mix[b] += mag * mag; mixCount[b]++; }
            }
            // onset strength for the tempo: how much the spectrum rose since the last frame, up to 4 kHz (kick, snare, chords)
            if (hz < FluxTopHz && Spectrum[k] > prevSpectrum[k]) flux += (Spectrum[k] - prevSpectrum[k]) * (hz < BandEdges[1] ? FluxBassWeight : 1);
        }
        Spectrum.CopyTo(prevSpectrum, 0);

        // tonal part: lasts over the last frames (median in time) and stands out from its neighbors (median across bins)
        mags.CopyTo(hist[histPos], 0);
        histPos = (histPos + 1) % Hist;
        Span<float> t = stackalloc float[Hist], f = stackalloc float[2 * Spread + 1];
        int k0 = Math.Max(Spread, (int)(TonalLo / binHz)), k1 = Math.Min(mags.Length - Spread, (int)(TonalHi / binHz) + 1);
        Array.Clear(tonal);
        for (int k = k0; k < k1; k++)
        {
            for (int i = 0; i < Hist; i++) t[i] = hist[i][k];
            t.Sort();
            mags.AsSpan(k - Spread, f.Length).CopyTo(f);
            f.Sort();
            float h = t[Hist / 2];
            if (h > f[Spread]) tonal[k] = h;
            float hz = k * binHz;
            if (hz >= MelodyLo && hz < MelodyHi) sum[3] += tonal[k] * tonal[k];
        }

        // per octave, so the width of a band doesn't change its level (and the bin size doesn't either)
        for (int b = 0; b < 4; b++)
        {
            float lo = b < 3 ? BandEdges[b] : MelodyLo, hi = b < 3 ? BandEdges[b + 1] : MelodyHi;
            float sens = b < Sensitivity.Length && float.IsFinite(Sensitivity[b]) ? Math.Clamp(Sensitivity[b], 0.1f, 10f) : 1;
            float v = MathF.Sqrt(sum[b] / MathF.Log2(hi / lo)) * BandScale * Tilt[b] * sens;
            float tau = b == 3 ? MelodyTau(v > Bands.Raw[b]) : v > Bands.Raw[b] ? AttackTau : Math.Max(Release, AttackTau);
            Bands.Raw[b] += (v - Bands.Raw[b]) * (1 - MathF.Exp(-dt / tau));
            peak[b] = Math.Max(Math.Max(Bands.Raw[b], peak[b] * MathF.Exp(-dt / PeakTau)), PeakFloor);
            Bands.Norm[b] = Bands.Raw[b] / peak[b];
        }

        // note: the pitch whose harmonics are strongest in the tonal part; holds while there is no melody
        if (Bands.Raw[3] > NoteMinLevel)
        {
            double best = 0;
            float bestHz = 0;
            for (float c = NoteLo; c <= NoteHi; c *= 1.0293022f) // half semitones
            {
                double s = 0, w = 1;
                for (int n = 1; n <= 5; n++, w *= 0.8) s += w * At(c * n / binHz);
                if (s > best) { best = s; bestHz = c; }
            }
            if (bestHz > 0)
            {
                float semis = 12 * MathF.Log2(bestHz / NoteLo);
                noteSemis = noteSemis < 0 ? semis : noteSemis + (semis - noteSemis) * (1 - MathF.Exp(-dt / NoteTau));
            }
        }
        for (int b = 0; b < 3; b++)
        {
            float v = mixCount[b] > 0 ? MathF.Sqrt(mix[b] / mixCount[b]) * MixScale : 0;
            Bands.Mix[b] += (v - Bands.Mix[b]) * (1 - MathF.Exp(-dt / (v > Bands.Mix[b] ? AttackTau : Math.Max(Release, AttackTau))));
        }
        Bands.Raw[4] = Bands.Norm[4] = noteSemis < 0 ? 0 : Math.Clamp(noteSemis / (12 * MathF.Log2(NoteHi / NoteLo)), 0, 1);
        Music.Update(Bands, flux / 255, dt);
    }

    float MelodyTau(bool rising)
    {
        float s = float.IsFinite(MelodySmooth) ? Math.Clamp(MelodySmooth, 0.1f, 3f) : 0.8f;
        return rising ? s * 0.4f : s;
    }

    float At(float bin)
    {
        int i = (int)bin;
        if (i < 0 || i + 1 >= tonal.Length) return 0;
        float x = bin - i;
        return tonal[i] * (1 - x) + tonal[i + 1] * x;
    }
    static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    float xr = (float)(re[b] * cr - im[b] * ci), xi = (float)(re[b] * ci + im[b] * cr);
                    re[b] = re[a] - xr; im[b] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                    (cr, ci) = (cr * wr - ci * wi, cr * wi + ci * wr);
                }
            }
        }
    }
}
