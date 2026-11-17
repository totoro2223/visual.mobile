using NAudio.CoreAudioApi;
using NAudio.Wave;
using Visual.Core;

namespace Visual.Desktop;

public sealed record AudioSource(string Id, string Name, bool Loopback);

/// <summary>WASAPI capture: loopback of an output device (what's playing) or a regular input.</summary>
sealed class AudioCapture(AudioAnalyzer analyzer) : IDisposable
{
    WasapiCapture? cap;
    float[] mono = [];
    public AudioSource? Current { get; private set; }
    public string? Error { get; private set; }

    public static List<AudioSource> List()
    {
        var en = new MMDeviceEnumerator();
        var l = new List<AudioSource>();
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            l.Add(new(d.ID, "Salida: " + d.FriendlyName, true));
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            l.Add(new(d.ID, "Entrada: " + d.FriendlyName, false));
        return l;
    }

    public static string? DefaultOutputId()
    {
        try { return new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; }
        catch { return null; }
    }

    public void Start(AudioSource src)
    {
        Stop();
        Error = null;
        Current = src;
        try
        {
            var dev = new MMDeviceEnumerator().GetDevice(src.Id);
            cap = src.Loopback ? new WasapiLoopbackCapture(dev) : new WasapiCapture(dev);
            analyzer.SampleRate = cap.WaveFormat.SampleRate;
            cap.DataAvailable += OnData;
            cap.RecordingStopped += (_, e) => { if (e.Exception != null) Error = e.Exception.Message; };
            cap.StartRecording();
        }
        catch (Exception e)
        {
            Error = e.Message;
            cap = null;
        }
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        var f = ((WasapiCapture)sender!).WaveFormat;
        int ch = f.Channels, bytes = f.BitsPerSample / 8;
        // ponytail: 32-bit extensible assumed float (WASAPI shared-mode mix format always is in practice)
        bool isFloat = f.Encoding == WaveFormatEncoding.IeeeFloat || (f.Encoding == WaveFormatEncoding.Extensible && f.BitsPerSample == 32);
        int frames = e.BytesRecorded / (bytes * ch);
        if (mono.Length < frames) mono = new float[frames];
        var buf = e.Buffer;
        for (int i = 0; i < frames; i++)
        {
            float sum = 0;
            for (int c = 0; c < ch; c++)
            {
                int o = (i * ch + c) * bytes;
                sum += bytes switch
                {
                    4 when isFloat => BitConverter.ToSingle(buf, o),
                    4 => BitConverter.ToInt32(buf, o) / 2147483648f,
                    3 => (buf[o] | buf[o + 1] << 8 | (sbyte)buf[o + 2] << 16) / 8388608f,
                    2 => BitConverter.ToInt16(buf, o) / 32768f,
                    _ => 0,
                };
            }
            mono[i] = sum / ch;
        }
        analyzer.Push(mono.AsSpan(0, frames));
        Net?.Push(mono.AsSpan(0, frames), f.SampleRate);
    }

    /// <summary>When set, everything captured is also sent to the TV app over Wi-Fi.</summary>
    public volatile NetAudioSender? Net;

    void Stop()
    {
        if (cap == null) return;
        cap.DataAvailable -= OnData;
        try { cap.StopRecording(); } catch { }
        cap.Dispose();
        cap = null;
    }

    public void Dispose() => Stop();
}
