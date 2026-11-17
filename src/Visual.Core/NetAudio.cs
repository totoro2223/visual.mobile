using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Visual.Core;

/// <summary>
/// Audio over Wi-Fi: the PC sends what it hears to the TV app, which analyzes it like any other source.
/// One UDP packet = "VA" + token length + token + sample rate (int32) + mono int16 samples.
/// Sent to the TV's remote port with the remote's token, so only someone who saw the QR / URL can feed it.
/// </summary>
public static class NetAudio
{
    public const int MaxSamples = 600; // keeps a packet under a typical 1500-byte MTU

    public static int Encode(Span<byte> dst, string token, int rate, ReadOnlySpan<float> s)
    {
        var tk = Encoding.ASCII.GetBytes(token);
        dst[0] = (byte)'V';
        dst[1] = (byte)'A';
        dst[2] = (byte)tk.Length;
        tk.CopyTo(dst[3..]);
        int o = 3 + tk.Length;
        BinaryPrimitives.WriteInt32LittleEndian(dst[o..], rate);
        o += 4;
        foreach (var x in s)
        {
            BinaryPrimitives.WriteInt16LittleEndian(dst[o..], (short)(Math.Clamp(x, -1f, 1f) * 32767));
            o += 2;
        }
        return o;
    }

    /// <summary>Returns the number of samples written to dst, or -1 if the packet is malformed or has the wrong token.</summary>
    public static int Decode(ReadOnlySpan<byte> p, string token, Span<float> dst, out int rate)
    {
        rate = 0;
        if (p.Length < 7 || p[0] != 'V' || p[1] != 'A') return -1;
        int tl = p[2], o = 3 + tl;
        if (p.Length < o + 4 || tl != token.Length) return -1;
        Span<byte> tk = stackalloc byte[tl];
        Encoding.ASCII.GetBytes(token, tk);
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(p.Slice(3, tl), tk)) return -1;
        rate = BinaryPrimitives.ReadInt32LittleEndian(p[o..]);
        if (rate < 4000 || rate > 192000) return -1;
        o += 4;
        int n = Math.Min((p.Length - o) / 2, dst.Length);
        for (int i = 0; i < n; i++) dst[i] = BinaryPrimitives.ReadInt16LittleEndian(p[(o + 2 * i)..]) / 32768f;
        return n;
    }

    /// <summary>Reads host, port and token out of the URL the TV shows (http://192.168.1.5:8765/?k=abc).</summary>
    public static (string Host, int Port, string Token)? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || u.Scheme != "http") return null;
        var k = System.Web.HttpUtility.ParseQueryString(u.Query)["k"];
        return string.IsNullOrEmpty(k) || k.Length > 64 ? null : (u.Host, u.Port, k);
    }
}

/// <summary>Sends mono audio to the TV app. Push is called from the capture thread.</summary>
public sealed class NetAudioSender : IDisposable
{
    readonly UdpClient udp = new();
    readonly IPEndPoint to;
    readonly string token;
    readonly float[] pend = new float[NetAudio.MaxSamples];
    readonly byte[] packet = new byte[2048];
    int n, step = 1, acc;
    float sum;
    public volatile string? Error;
    public string Target => to.ToString();

    /// <exception cref="ArgumentException">the URL isn't the one the TV shows</exception>
    public NetAudioSender(string url)
    {
        var p = NetAudio.ParseUrl(url) ?? throw new ArgumentException("Pegá la dirección completa que muestra el TV (http://…/?k=…)");
        var ip = IPAddress.TryParse(p.Host, out var a) ? a
            : Dns.GetHostAddresses(p.Host).FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork)
              ?? throw new ArgumentException("No se encontró " + p.Host);
        to = new IPEndPoint(ip, p.Port);
        token = p.Token;
    }

    public void Push(ReadOnlySpan<float> mono, int rate)
    {
        // ponytail: averaging down to <= 48 kHz is a crude low-pass; plenty for bass / mid / high bands
        step = Math.Max(1, rate / 48000);
        try
        {
            foreach (var x in mono)
            {
                sum += x;
                if (++acc < step) continue;
                pend[n++] = sum / step;
                sum = 0;
                acc = 0;
                if (n < pend.Length) continue;
                udp.Send(packet, NetAudio.Encode(packet, token, rate / step, pend), to);
                n = 0;
            }
            Error = null;
        }
        catch (Exception e) { Error = e.Message; n = 0; }
    }

    public void Dispose() => udp.Dispose();
}

/// <summary>Receives what a <see cref="NetAudioSender"/> sends and feeds it to the analyzer (TV side).</summary>
public sealed class NetAudioReceiver : IDisposable
{
    readonly UdpClient udp;
    readonly Thread thread;
    public volatile int Packets;

    public NetAudioReceiver(int port, string token, AudioAnalyzer analyzer)
    {
        udp = new UdpClient(port);
        thread = new Thread(() =>
        {
            var buf = new float[NetAudio.MaxSamples];
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                byte[] p;
                try { p = udp.Receive(ref from); }
                catch { return; } // closed
                int n = NetAudio.Decode(p, token, buf, out int rate);
                if (n <= 0) continue;
                analyzer.SampleRate = rate;
                analyzer.Push(buf.AsSpan(0, n));
                Packets++;
            }
        }) { IsBackground = true, Name = "net audio" };
        thread.Start();
    }

    public void Dispose()
    {
        udp.Dispose(); // unblocks Receive
        thread.Join(500);
    }
}
