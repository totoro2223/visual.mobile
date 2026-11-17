using Android.Content;
using Android.Media;
using Android.Media.Audiofx;
using Visual.Core;

namespace VisualApp;

/// <summary>
/// Where the music comes from: what the device itself plays (output mix), its microphone,
/// or an input device such as a USB audio interface or a wired headset.
/// </summary>
sealed class AudioInput(Context ctx, AudioAnalyzer analyzer)
{
    public const string SystemId = "system", MicId = "mic";
    const int Rate = 44100;
    readonly object gate = new();
    Visualizer? viz;
    volatile AudioRecord? rec;
    Thread? thread;
    public volatile string Active = "";
    public volatile string? Error;

#pragma warning disable CA1416 // UsbHeadset is a plain constant; on Android < 8 it simply never matches
    AudioDeviceInfo[] Inputs() =>
        ((AudioManager)ctx.GetSystemService(Context.AudioService)!).GetDevices(GetDevicesTargets.Inputs)?
            .Where(d => d.Type is AudioDeviceType.UsbDevice or AudioDeviceType.UsbHeadset or AudioDeviceType.UsbAccessory
                or AudioDeviceType.WiredHeadset or AudioDeviceType.LineAnalog or AudioDeviceType.LineDigital).ToArray() ?? [];
#pragma warning restore CA1416

    // device ids change when replugged; name + type stays the same
    static string Id(AudioDeviceInfo d) => $"dev:{d.ProductName}|{d.Type}";

    public (string Id, string Label)[] Sources() =>
    [
        (SystemId, "Salida del sistema (lo que suena)"),
        (MicId, "Micrófono"),
        .. Inputs().Select(d => (Id(d), $"{d.ProductName} ({(d.Type.ToString().StartsWith("Usb") ? "USB" : d.Type.ToString())})")),
    ];

    /// <summary>Switches source; if it can't be opened, falls back to the system output and says why in <see cref="Error"/>.</summary>
    public void Start(string id)
    {
        lock (gate)
        {
            StopLocked();
            Error = null;
            try { Open(id); Active = id; return; }
            catch (Exception e) { StopLocked(); Error = "No se pudo usar esa fuente de audio: " + e.Message; }
            if (id == SystemId) return;
            try { Open(SystemId); Active = SystemId; Error += ". Se usa la salida del sistema."; }
            catch (Exception e) { StopLocked(); Error = "Audio no disponible: " + e.Message; }
        }
    }

    void Open(string id)
    {
        if (id == SystemId)
        {
            // Visualizer on the global output mix (8-bit, ~20 captures/s): silent on some devices for
            // passthrough / DRM audio; the mic or a USB input is the way around that
            viz = new Visualizer(0);
            viz.SetCaptureSize(Visualizer.GetCaptureSizeRange()![1]);
            viz.SetDataCaptureListener(new Capture(analyzer), Visualizer.MaxCaptureRate, true, false);
            viz.SetEnabled(true);
            return;
        }
        AudioDeviceInfo? dev = null;
        if (id != MicId) dev = Inputs().FirstOrDefault(d => Id(d) == id) ?? throw new InvalidOperationException("no está conectado");
        int min = AudioRecord.GetMinBufferSize(Rate, ChannelIn.Mono, Encoding.PcmFloat);
        if (min <= 0) throw new InvalidOperationException("formato no soportado");
        var r = new AudioRecord(AudioSource.Mic, Rate, ChannelIn.Mono, Encoding.PcmFloat, Math.Max(min, 16384));
        if (r.State != State.Initialized) { r.Release(); throw new InvalidOperationException("no hay micrófono o falta el permiso"); }
        if (dev != null) r.SetPreferredDevice(dev);
        r.StartRecording();
        rec = r;
        analyzer.SampleRate = Rate;
        thread = new Thread(() =>
        {
            var buf = new float[1024];
            while (rec == r)
            {
                int n = r.Read(buf, 0, buf.Length, 0); // 0 = READ_BLOCKING
                if (n < 0) break;
                analyzer.Push(buf.AsSpan(0, n));
            }
        }) { IsBackground = true };
        thread.Start();
    }

    void StopLocked()
    {
        if (viz != null) { try { viz.SetEnabled(false); } catch { } viz.Release(); viz = null; }
        var r = rec;
        rec = null;
        if (r == null) return;
        try { r.Stop(); } catch { } // unblocks Read
        thread?.Join(500);
        r.Release();
    }

    public void Stop() { lock (gate) StopLocked(); }

    sealed class Capture(AudioAnalyzer analyzer) : Java.Lang.Object, Visualizer.IOnDataCaptureListener
    {
        float[] buf = [];

        public void OnWaveFormDataCapture(Visualizer? visualizer, byte[]? waveform, int samplingRate)
        {
            if (waveform == null) return;
            if (buf.Length != waveform.Length) buf = new float[waveform.Length];
            for (int i = 0; i < buf.Length; i++) buf[i] = (waveform[i] - 128) / 128f; // unsigned 8-bit, 128 = silence
            analyzer.SampleRate = Math.Max(8000, samplingRate / 1000); // reported in mHz
            analyzer.Push(buf);
        }

        public void OnFftDataCapture(Visualizer? visualizer, byte[]? fft, int samplingRate) { }
    }
}
