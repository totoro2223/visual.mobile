namespace Visual.Core;

public sealed class AudioBands
{
    public readonly float[] Raw = new float[3];   // smoothed level, roughly 0..1 for loud music
    public readonly float[] Norm = new float[3];  // Raw divided by its recent peak (auto-gain)
    public float Get(Band b, bool autoGain) => autoGain ? Norm[(int)b] : Raw[(int)b];
}

/// <summary>
/// Platform-neutral analysis: push mono samples from any thread, call Update once per frame.
/// Produces ISF audio textures (Wave, Spectrum) and bass/mid/high bands.
/// </summary>
public sealed class AudioAnalyzer
{
    public const int FftSize = 2048;
    // ponytail: calibration knobs, tuned by ear; the per-modifier gain covers the rest
    const float BandScale = 4f, DbFloor = -70f, AttackTau = 0.015f, ReleaseTau = 0.15f, PeakTau = 5f, PeakFloor = 0.05f;
    static readonly float[] BandEdges = [20, 250, 4000, 16000];

    public readonly byte[] Wave = new byte[1024];          // 128 = silence; ISF "audio"
    public readonly byte[] Spectrum = new byte[FftSize / 2]; // dB-scaled 0..255; ISF "audioFFT"
    public readonly AudioBands Bands = new();
    public float InputGain = 1f;
    public int SampleRate = 48000;

    readonly float[] ring = new float[FftSize];
    readonly float[] re = new float[FftSize], im = new float[FftSize], window = new float[FftSize];
    readonly float[] peak = [PeakFloor, PeakFloor, PeakFloor];
    readonly object gate = new();
    int ringPos;
    long pushed, seen;
    float stale;

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

        Span<float> sum = stackalloc float[3];
        Span<int> count = stackalloc int[3];
        float binHz = SampleRate / (float)FftSize;
        for (int k = 0; k < Spectrum.Length; k++)
        {
            float mag = MathF.Sqrt(re[k] * re[k] + im[k] * im[k]) * 4f / FftSize; // 1.0 = full-scale sine (Hann)
            float db = 20 * MathF.Log10(mag + 1e-9f);
            Spectrum[k] = (byte)Math.Clamp((db - DbFloor) / -DbFloor * 255, 0, 255);
            float hz = k * binHz;
            for (int b = 0; b < 3; b++)
                if (hz >= BandEdges[b] && hz < BandEdges[b + 1]) { sum[b] += mag * mag; count[b]++; }
        }

        for (int b = 0; b < 3; b++)
        {
            float v = count[b] > 0 ? MathF.Sqrt(sum[b] / count[b]) * BandScale : 0;
            float tau = v > Bands.Raw[b] ? AttackTau : ReleaseTau;
            Bands.Raw[b] += (v - Bands.Raw[b]) * (1 - MathF.Exp(-dt / tau));
            peak[b] = Math.Max(Math.Max(Bands.Raw[b], peak[b] * MathF.Exp(-dt / PeakTau)), PeakFloor);
            Bands.Norm[b] = Bands.Raw[b] / peak[b];
        }
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
