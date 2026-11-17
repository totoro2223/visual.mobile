using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Web;

namespace Visual.Core;

/// <summary>
/// Tiny HTTP server for the phone remote: GET / (page), GET /state (JSON), POST /cmd (JSON command).
/// Every request needs ?k=token. Commands are queued; the render thread drains them.
/// Raw TcpListener instead of HttpListener: no URL ACL on Windows, and it runs the same on Android.
/// </summary>
public sealed class RemoteServer : IDisposable
{
    static readonly byte[] Page = ReadPage();
    TcpListener? listener;

    public string Token { get; private set; } = "";
    public int Port { get; private set; }
    public string? Error { get; private set; }
    public bool Running => listener != null;
    /// <summary>Latest state JSON, published by the render thread.</summary>
    public volatile string State = "{}";
    public readonly ConcurrentQueue<JsonObject> Commands = new();

    public void Start(int port, string token, bool loopbackOnly = false)
    {
        Stop();
        Token = token;
        Error = null;
        try
        {
            var l = new TcpListener(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, port);
            l.Start();
            Port = ((IPEndPoint)l.LocalEndpoint).Port;
            listener = l;
            _ = AcceptLoop(l);
        }
        catch (Exception e) { Error = e.Message; }
    }

    public void Stop()
    {
        listener?.Stop();
        listener = null;
    }

    public void Dispose() => Stop();

    /// <summary>LAN address of this machine (the UDP connect sends nothing, it only picks the outgoing interface).</summary>
    public static string LocalAddress()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 65530);
            return ((IPEndPoint)s.LocalEndPoint!).Address.ToString();
        }
        catch
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))?.ToString() ?? "127.0.0.1";
        }
    }

    async Task AcceptLoop(TcpListener l)
    {
        while (true)
        {
            TcpClient c;
            try { c = await l.AcceptTcpClientAsync(); }
            catch { return; } // stopped
            _ = Handle(c);
        }
    }

    async Task Handle(TcpClient client)
    {
        using var _ = client;
        try
        {
            using var cts = new CancellationTokenSource(5000);
            var s = client.GetStream();
            var buf = new byte[512 * 1024]; // big enough for a shader upload
            int n = 0, end;
            while ((end = buf.AsSpan(0, n).IndexOf("\r\n\r\n"u8)) < 0)
            {
                if (n == buf.Length) return;
                int r = await s.ReadAsync(buf.AsMemory(n), cts.Token);
                if (r == 0) return;
                n += r;
            }
            var head = Encoding.ASCII.GetString(buf, 0, end).Split("\r\n");
            var first = head[0].Split(' ');
            if (first.Length < 2) return;
            int len = 0;
            foreach (var h in head.Skip(1))
                if (h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(h[15..].Trim(), out len);
            int start = end + 4;
            if (len < 0 || start + len > buf.Length) { await Reply(s, 413, "text/plain", "too large"u8.ToArray()); return; }
            while (n < start + len)
            {
                int r = await s.ReadAsync(buf.AsMemory(n), cts.Token);
                if (r == 0) return;
                n += r;
            }

            var target = first[1];
            int q = target.IndexOf('?');
            var path = q < 0 ? target : target[..q];
            var key = HttpUtility.ParseQueryString(q < 0 ? "" : target[(q + 1)..])["k"] ?? "";
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(Token)))
            {
                await Reply(s, 403, "text/plain", "bad key"u8.ToArray());
                return;
            }
            switch (first[0], path)
            {
                case ("GET", "/"):
                    await Reply(s, 200, "text/html; charset=utf-8", Page);
                    break;
                case ("GET", "/state"):
                    await Reply(s, 200, "application/json", Encoding.UTF8.GetBytes(State));
                    break;
                case ("POST", "/upload"): // body = the .fs text; the host saves it on its own thread
                    var name = HttpUtility.ParseQueryString(target[(q + 1)..])["name"] ?? "";
                    Commands.Enqueue(new JsonObject { ["op"] = "upload", ["name"] = name, ["text"] = Encoding.UTF8.GetString(buf, start, len) });
                    await Reply(s, 200, "text/plain", "ok"u8.ToArray());
                    break;
                case ("POST", "/cmd"):
                    if (JsonNode.Parse(buf.AsSpan(start, len)) is JsonObject cmd && Commands.Count < 256) Commands.Enqueue(cmd);
                    await Reply(s, 200, "text/plain", "ok"u8.ToArray());
                    break;
                default:
                    await Reply(s, 404, "text/plain", "not found"u8.ToArray());
                    break;
            }
        }
        catch { } // bad request / client gone: just drop the connection
    }

    static async Task Reply(Stream s, int code, string type, byte[] body)
    {
        var head = $"HTTP/1.1 {code} {(code == 200 ? "OK" : "Error")}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(head));
        await s.WriteAsync(body);
    }

    static byte[] ReadPage()
    {
        using var st = typeof(RemoteServer).Assembly.GetManifestResourceStream("remote.html")!;
        using var ms = new MemoryStream();
        st.CopyTo(ms);
        return ms.ToArray();
    }
}

/// <summary>The remote's protocol: state snapshot for the phone and the commands it sends back.</summary>
public static class Remote
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string State(Player pl, IsfShader? isf, string loaded, string[] projects, string[] shaders, AudioBands bands, float maxHz, bool strobe, object? device = null)
    {
        var p = pl.Project;
        var scene = pl.Current >= 0 && pl.Current < p.Scenes.Count ? p.Scenes[pl.Current] : null;
        if (scene != null) isf?.EnsureParams(scene.Params);
        p.EnsureEffects();
        return JsonSerializer.Serialize(new
        {
            Project = p.Name,
            Loaded = loaded,
            Projects = projects,
            Shaders = shaders,
            pl.Current,
            pl.SceneTime,
            Scenes = p.Scenes.Select(s => new { s.Shader, s.Duration, Fx = EffectDefs.ActiveLabels(s.Effects) }),
            p.Shuffle,
            p.Crossfade,
            p.CrossfadeSeconds,
            Inputs = (isf?.Inputs ?? []).Where(i => !i.IsTexture)
                .Select(i => new { i.Name, i.Label, Type = i.Type.ToString(), i.Min, i.Max, i.Values, i.Labels }),
            Params = scene?.Params,
            EffectDefs = EffectDefs.All,
            Effects = scene?.Effects,
            p.AutoStrobe,
            Bands = bands.Raw,
            MaxHz = maxHz,
            Strobe = strobe,
            Device = device, // host-only settings (Android: audio source, quality); null on PC
        }, Json);
    }

    /// <summary>Applies a command to the project/player. Returns the op when the host must handle it (strobe, open, save, new, duplicate), else null.</summary>
    public static string? Apply(JsonObject c, Player pl, string[] shaders)
    {
        var p = pl.Project;
        var op = Str(c, "op");
        switch (op)
        {
            case "next": pl.Next(); break;
            case "goto": pl.GoTo(Int(c, "i")); break;
            case "duration":
                if (SceneAt(p, Int(c, "scene")) is { } d) d.Duration = Math.Max(0, Float(c, "value"));
                break;
            case "param":
                if (SceneAt(p, Int(c, "scene")) is { } s) Replace(s.Params, Str(c, "name"), c["state"]);
                break;
            case "effect":
                if (SceneAt(p, Int(c, "scene")) is { } fs) Replace(fs.Effects, Str(c, "name"), c["state"]);
                break;
            case "resetEffects":
                if (SceneAt(p, Int(c, "scene")) is { } rs) { rs.Effects.Clear(); rs.EnsureEffects(); }
                break;
            case "copyEffects":
                if (SceneAt(p, Int(c, "scene")) is { } cs) p.CopyEffects(cs);
                break;
            case "autostrobe":
                if (c["value"].Deserialize<AutoStrobe>(Json) is { } a) p.AutoStrobe = a;
                break;
            case "project":
                p.Shuffle = c["shuffle"]?.GetValue<bool>() ?? p.Shuffle;
                p.Crossfade = c["crossfade"]?.GetValue<bool>() ?? p.Crossfade;
                p.CrossfadeSeconds = Math.Clamp(c["crossfadeSeconds"]?.GetValue<float>() ?? p.CrossfadeSeconds, 0.1f, 10f);
                break;
            case "add":
                if (shaders.Contains(Str(c, "shader"))) p.Scenes.Add(new Scene { Shader = Str(c, "shader") });
                break;
            case "move":
                int from = Int(c, "i"), to = Int(c, "to");
                if (from >= 0 && to >= 0 && from < p.Scenes.Count && to < p.Scenes.Count) pl.Move(from, to);
                break;
            case "remove":
                if (Int(c, "i") is var i && i >= 0 && i < p.Scenes.Count) pl.Remove(i);
                break;
            default: return op;
        }
        return null;
    }

    /// <summary>Saves an uploaded shader into the folder; only plain "name.fs" file names. Returns the file name.</summary>
    public static string SaveShader(string folder, string name, string text)
    {
        if (name != Path.GetFileName(name) || !name.EndsWith(".fs", StringComparison.OrdinalIgnoreCase)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.StartsWith('.'))
            throw new ArgumentException($"Nombre de shader inválido: {name}");
        IsfShader.Parse(text); // rejects a broken JSON header before it lands in the folder
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), text);
        return name;
    }

    static void Replace(Dictionary<string, ParamState> ps, string name, JsonNode? state)
    {
        if (!ps.TryGetValue(name, out var old) || state.Deserialize<ParamState>(Json) is not { } n) return;
        if (n.Value.Length != old.Value.Length || !n.Value.All(float.IsFinite)) return;
        ps[name] = n;
    }

    static Scene? SceneAt(Project p, int i) => i >= 0 && i < p.Scenes.Count ? p.Scenes[i] : null;
    static string Str(JsonObject c, string k) => c[k]?.GetValue<string>() ?? "";
    static int Int(JsonObject c, string k) => c[k]?.GetValue<int>() ?? -1;
    static float Float(JsonObject c, string k) => c[k]?.GetValue<float>() ?? 0;
}
