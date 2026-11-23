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
    /// <summary>GET /project waits on these; the render thread answers with (name, ProjectStore.ToJson) via AnswerExports.</summary>
    public readonly ConcurrentQueue<TaskCompletionSource<(string Name, string Json)>> ExportRequests = new();

    public void AnswerExports(Project p)
    {
        while (ExportRequests.TryDequeue(out var r)) r.TrySetResult((p.Name, ProjectStore.ToJson(p)));
    }

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
            long len = 0;
            foreach (var h in head.Skip(1))
                if (h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) long.TryParse(h[15..].Trim(), out len);
            int start = end + 4;
            var target = first[1];
            int q = target.IndexOf('?');
            var path = q < 0 ? target : target[..q];
            var query = HttpUtility.ParseQueryString(q < 0 ? "" : target[(q + 1)..]);
            var key = query["k"] ?? "";
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(Token)))
            {
                await Reply(s, 403, "text/plain", "bad key"u8.ToArray());
                return;
            }
            if ((first[0], path) == ("POST", "/media")) // big: straight to disk, not into buf
            {
                var (code, msg) = await ReceiveMedia(s, query["name"] ?? "", len, buf.AsMemory(start, n - start));
                await Reply(s, code, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(msg));
                return;
            }
            if (len < 0 || start + len > buf.Length) { await Reply(s, 413, "text/plain", "too large"u8.ToArray()); return; }
            while (n < start + len)
            {
                int r = await s.ReadAsync(buf.AsMemory(n), cts.Token);
                if (r == 0) return;
                n += r;
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
                    Commands.Enqueue(new JsonObject { ["op"] = "upload", ["name"] = name, ["text"] = Encoding.UTF8.GetString(buf, start, (int)len) });
                    await Reply(s, 200, "text/plain", "ok"u8.ToArray());
                    break;
                case ("GET", "/project"): // the host serializes it on its own thread (ExportRequests)
                    var req = new TaskCompletionSource<(string Name, string Json)>(TaskCreationOptions.RunContinuationsAsynchronously);
                    ExportRequests.Enqueue(req);
                    var (pname, json) = await req.Task.WaitAsync(cts.Token);
                    await Reply(s, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json),
                        "Content-Disposition: attachment; filename*=UTF-8''" + Uri.EscapeDataString(pname + ".json"));
                    break;
                case ("POST", "/importProject"): // checked here so the phone gets the error; the host saves and opens it
                    var text = Encoding.UTF8.GetString(buf, start, (int)len);
                    try { ProjectStore.Parse(text); }
                    catch (InvalidDataException e) { await Reply(s, 400, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(e.Message)); break; }
                    Commands.Enqueue(new JsonObject { ["op"] = "import", ["text"] = text });
                    await Reply(s, 200, "text/plain", "ok"u8.ToArray());
                    break;
                case ("GET", "/shaderSource"):
                    try
                    {
                        var src = Remote.ShaderPreview(ShaderFolder ?? throw new ArgumentException("Este equipo no muestra shaders"), query["name"] ?? "");
                        await Reply(s, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(src));
                    }
                    catch (Exception e) // bad name, missing file, broken header / macro
                    {
                        await Reply(s, 400, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(e.Message));
                    }
                    break;
                case ("POST", "/cmd"):
                    if (JsonNode.Parse(buf.AsSpan(start, (int)len)) is JsonObject cmd && Commands.Count < 256) Commands.Enqueue(cmd);
                    await Reply(s, 200, "text/plain", "ok"u8.ToArray());
                    break;
                default:
                    await Reply(s, 404, "text/plain", "not found"u8.ToArray());
                    break;
            }
        }
        catch { } // bad request / client gone: just drop the connection
    }

    /// <summary>Folder that POST /media writes videos / pictures into; null = uploads refused.</summary>
    public volatile string? MediaFolder;
    /// <summary>Folder GET /shaderSource reads from; null = no previews.</summary>
    public volatile string? ShaderFolder;
    public const long MaxMediaBytes = 4L << 30;

    /// <summary>Copies the body into MediaFolder/name (via a .part file), then queues {"op":"mediaChanged"} for the host.</summary>
    async Task<(int, string)> ReceiveMedia(Stream s, string name, long len, ReadOnlyMemory<byte> already)
    {
        var folder = MediaFolder;
        if (folder == null) return (403, "Este equipo no recibe videos");
        if (!Media.ValidName(name)) return (400, "Nombre o tipo de archivo no admitido: " + name);
        if (len <= 0 || len > MaxMediaBytes) return (413, "El archivo tiene que pesar menos de 4 GB");
        Directory.CreateDirectory(folder);
        try { if (new DriveInfo(Path.GetFullPath(folder)).AvailableFreeSpace < len + (64 << 20)) return (507, "No hay espacio libre en el equipo"); }
        catch { } // unknown drive: just try
        var dest = Path.Combine(folder, name);
        var part = dest + ".part";
        try
        {
            await using (var f = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
            {
                var take = already[..(int)Math.Min(already.Length, len)];
                await f.WriteAsync(take);
                long left = len - take.Length;
                var chunk = new byte[1 << 16];
                while (left > 0)
                {
                    using var idle = new CancellationTokenSource(30000); // slow Wi-Fi is fine, a dead one is not
                    int r = await s.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, left)), idle.Token);
                    if (r == 0) throw new IOException("Se cortó la conexión");
                    await f.WriteAsync(chunk.AsMemory(0, r));
                    left -= r;
                }
            }
            File.Move(part, dest, true);
        }
        catch (Exception e)
        {
            try { File.Delete(part); } catch { }
            return (500, e is IOException && File.Exists(dest) ? "No se pudo reemplazar (¿se está reproduciendo?): " + e.Message : e.Message);
        }
        Commands.Enqueue(new JsonObject { ["op"] = "mediaChanged", ["name"] = name });
        return (200, "ok");
    }

    static async Task Reply(Stream s, int code, string type, byte[] body, string? extraHeader = null)
    {
        var extra = extraHeader == null ? "" : extraHeader + "\r\n";
        var head = $"HTTP/1.1 {code} {(code == 200 ? "OK" : "Error")}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\n{extra}Connection: close\r\n\r\n";
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

    public static string State(Player pl, IsfShader? isf, string loaded, string[] projects, string[] shaders, string[] media, AudioAnalyzer audio, float maxHz, bool strobe, object? device = null, AutoDjRunner? dj = null,
        Func<string, IsfShader?>? shaderOf = null, AutoStrobeRunner? auto = null)
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
            Scenes = p.Scenes.Select(s => new { s.Shader, s.Title, s.Duration, s.Intensity, s.Level, Fx = EffectDefs.ActiveLabels(s.Effects), Layers = s.Layers.Count }),
            Name = scene?.Name,
            Media = scene?.Media,
            MediaFiles = media,
            p.Shuffle,
            p.Crossfade,
            p.CrossfadeSeconds,
            Inputs = Inputs(isf),
            Params = scene?.Params,
            EffectDefs = EffectDefs.All,
            EffectDefs.StrobeModes,
            Effects = scene?.Effects,
            Layers = scene?.Layers.Select(l =>
            {
                var li = l.Shader == "" ? null : shaderOf?.Invoke(l.Shader);
                li?.EnsureParams(l.Params);
                return new
                {
                    l.Shader, l.Off, l.Blend, l.Params, l.Effects, l.Media,
                    Fx = EffectDefs.ActiveLabels(l.Effects),
                    Filter = li?.Inputs.Any(i => i.Name == Layer.Below && i.IsTexture) == true,
                    Inputs = Inputs(li),
                };
            }),
            Layer.BlendNames,
            Canvas = scene?.Canvas,
            Quality = scene?.Quality ?? 0,
            p.AutoStrobe,
            AutoPlaying = auto?.Active == true ? auto.Current : -1,
            Bands = audio.Bands.Raw,
            Music = new { audio.Music.Energy, audio.Music.Bpm, audio.Music.Confidence, audio.Music.Locked, audio.Music.Silence, audio.Music.BarPhase },
            p.AutoDj,
            Dj = dj == null ? null : new { dj.Active, dj.Bar, Target = dj.Target(p.AutoDj), Status = dj.Status(p.AutoDj, p), dj.LastChange },
            MaxHz = maxHz,
            Strobe = strobe,
            Device = device, // host-only settings (Android: audio source, quality); null on PC
        }, Json);
    }

    static object Inputs(IsfShader? isf) => (isf?.Inputs ?? []).Where(i => i.Type is not (IsfType.Audio or IsfType.AudioFFT))
        .Select(i => new { i.Name, i.Label, Type = i.Type.ToString(), i.Min, i.Max, i.Values, i.Labels });

    /// <summary>Applies a command to the project/player. Returns the op when the host must handle it (strobe, open, save, new, duplicate), else null.</summary>
    public static string? Apply(JsonObject c, Player pl, string[] shaders, MusicMeter? music = null)
    {
        var p = pl.Project;
        var op = Str(c, "op");
        switch (op)
        {
            case "next": pl.Next(); break;
            case "goto": pl.GoTo(Int(c, "i")); break;
            case "quality": // 0 = the global one
                if (SceneAt(p, Int(c, "scene")) is { } qs) qs.Quality = Float(c, "value") is var q && float.IsFinite(q) && q > 0 ? Math.Clamp(q, 0.25f, 1f) : 0;
                break;
            case "duration":
                if (SceneAt(p, Int(c, "scene")) is { } d) d.Duration = Math.Max(0, Float(c, "value"));
                break;
            case "param":
                if (PictureAt(p, c) is { } s) Replace(s.Params, Str(c, "name"), c["state"]);
                break;
            case "effect":
                if (PictureAt(p, c) is { } fs) Replace(fs.Effects, Str(c, "name"), c["state"]);
                break;
            case "resetEffects":
                if (PictureAt(p, c) is { } rs) { rs.Effects.Clear(); rs.EnsureEffects(); }
                break;
            case "copyEffects":
                if (SceneAt(p, Int(c, "scene")) is { } cs) p.CopyEffects(cs);
                break;
            case "canvas": // value null = back to the global canvas
                if (SceneAt(p, Int(c, "scene")) is { } vs)
                    vs.Canvas = c["value"].Deserialize<SceneCanvas>(Json) is { } v && SceneCanvas.Modes.Contains(v.Mode)
                        ? new SceneCanvas { Mode = v.Mode, X = Math.Max(0, v.X), Y = Math.Max(0, v.Y), W = Math.Max(1, v.W), H = Math.Max(1, v.H) }
                        : null;
                break;
            case "name":
                if (SceneAt(p, Int(c, "scene")) is { } ns) ns.Name = Str(c, "value").Trim() is var t && t.Length > 60 ? t[..60] : t;
                break;
            case "media": // value null = nothing in that input
                if (PictureAt(p, c) is { } ms && Str(c, "name") is { Length: > 0 and <= 64 } input)
                {
                    if (c["value"].Deserialize<SceneMedia>(Json) is { } sm && (Media.IsUrl(sm.Src) ? sm.Src.Length <= 2000 : Media.ValidName(sm.Src)))
                        ms.Media[input] = new SceneMedia
                        {
                            Src = sm.Src.Trim(),
                            Speed = float.IsFinite(sm.Speed) ? Math.Clamp(sm.Speed, SceneMedia.MinSpeed, SceneMedia.MaxSpeed) : 1,
                        };
                    else if (c["value"] == null) ms.Media.Remove(input);
                }
                break;
            case "layerAdd":
                if (SceneAt(p, Int(c, "scene")) is { } las && shaders.Contains(Str(c, "shader")) && las.Layers.Count < MaxLayers)
                {
                    var nl = new Layer { Shader = Str(c, "shader") };
                    nl.EnsureEffects();
                    las.Layers.Add(nl);
                }
                break;
            case "layerRemove":
                if (SceneAt(p, Int(c, "scene")) is { } lrs && LayerAt(lrs, Int(c, "layer")) is { } rl) lrs.Layers.Remove(rl);
                break;
            case "layerMove":
                if (SceneAt(p, Int(c, "scene")) is { } lms && LayerAt(lms, Int(c, "layer")) is { } ml && Int(c, "to") is var lt && lt >= 0 && lt < lms.Layers.Count)
                {
                    lms.Layers.Remove(ml);
                    lms.Layers.Insert(lt, ml);
                }
                break;
            case "layerDup":
                if (SceneAt(p, Int(c, "scene")) is { } lds && LayerAt(lds, Int(c, "layer")) is { } dl && lds.Layers.Count < MaxLayers)
                    lds.Layers.Insert(lds.Layers.IndexOf(dl) + 1, dl.Clone());
                break;
            case "layerSet": // only the fields sent
                if (SceneAt(p, Int(c, "scene")) is { } lss && LayerAt(lss, Int(c, "layer")) is { } sl)
                {
                    if (c["shader"] != null && shaders.Contains(Str(c, "shader"))) sl.Shader = Str(c, "shader");
                    if (c["blend"] != null && Enum.IsDefined((BlendMode)Int(c, "blend"))) sl.Blend = (BlendMode)Int(c, "blend");
                    if (c["off"] != null) sl.Off = c["off"]!.GetValue<bool>();
                }
                break;
            case "intensity":
                if (SceneAt(p, Int(c, "scene")) is { } ins) ins.Intensity = Math.Clamp(Int(c, "value"), 0, 3);
                break;
            case "autodj":
                if (c["value"].Deserialize<AutoDj>(Json) is { } dj) p.AutoDj = new AutoDj
                    {
                        Enabled = dj.Enabled, Bars = Math.Clamp(dj.Bars, 1, 64), Drops = dj.Drops, RandomBars = dj.RandomBars,
                        BarsMax = Math.Clamp(dj.BarsMax, Math.Clamp(dj.Bars, 1, 64), 64),
                        Sensitivity = float.IsFinite(dj.Sensitivity) ? Math.Clamp(dj.Sensitivity, 0, 1) : 0.5f,
                    };
                break;
            case "autostrobe":
                if (c["value"].Deserialize<AutoStrobe>(Json) is { } a)
                {
                    a.Old = null;
                    if (a.Configs?.Count > AutoStrobe.MaxConfigs) a.Configs = a.Configs.Take(AutoStrobe.MaxConfigs).ToList();
                    a.Ensure();
                    p.AutoStrobe = a;
                }
                break;
            case "project":
                p.Shuffle = c["shuffle"]?.GetValue<bool>() ?? p.Shuffle;
                p.Crossfade = c["crossfade"]?.GetValue<bool>() ?? p.Crossfade;
                p.CrossfadeSeconds = Math.Clamp(c["crossfadeSeconds"]?.GetValue<float>() ?? p.CrossfadeSeconds, 0.1f, 10f);
                break;
            case "music": // the phone's Tap sends the tempo it measured (Wi-Fi delays would spoil single taps)
                if (c["bpm"] != null) music?.SetTempo(Float(c, "bpm"), Int(c, "beat"));
                else if (c["lock"]?.GetValue<bool>() == true) music?.Lock();
                else music?.Unlock();
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

    /// <summary>Only plain "name.fs" (or "name.vs" when <paramref name="vertex"/>) file names.</summary>
    static void CheckShaderName(string name, bool vertex = false)
    {
        if (name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.StartsWith('.')
            || !(name.EndsWith(".fs", StringComparison.OrdinalIgnoreCase) || vertex && name.EndsWith(".vs", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Nombre de shader inválido: {name}");
    }

    /// <summary>GET /shaderSource: the GLSL ES sources the TV compiles (the browser draws them with WebGL), passes and inputs.</summary>
    public static string ShaderPreview(string folder, string name)
    {
        CheckShaderName(name);
        if (!File.Exists(Path.Combine(folder, name))) throw new ArgumentException("No existe: " + name);
        var isf = IsfShader.Load(folder, name);
        return JsonSerializer.Serialize(new
        {
            Frag = isf.BuildFragment(Renderer.EsHeader),
            Vert = isf.BuildVertex(Renderer.EsHeader),
            isf.Passes,
            isf.Buffers,
            Inputs = isf.Inputs.Select(i => new { i.Name, i.Label, Type = i.Type.ToString(), i.Default, i.Min, i.Max, i.Values, i.Labels }),
        });
    }

    /// <summary>Saves an uploaded .fs (or its companion .vs) into the folder. Returns the file name.</summary>
    public static string SaveShader(string folder, string name, string text)
    {
        CheckShaderName(name, vertex: true);
        if (name.EndsWith(".fs", StringComparison.OrdinalIgnoreCase)) IsfShader.Parse(text); // rejects a broken JSON header before it lands in the folder
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
    static Layer? LayerAt(Scene s, int i) => i >= 0 && i < s.Layers.Count ? s.Layers[i] : null;
    /// <summary>The scene, or one of its layers when the command has "layer".</summary>
    static Picture? PictureAt(Project p, JsonObject c) =>
        SceneAt(p, Int(c, "scene")) is not { } s ? null : c["layer"] == null ? s : LayerAt(s, Int(c, "layer"));
    public const int MaxLayers = 16;
    static string Str(JsonObject c, string k) => c[k]?.GetValue<string>() ?? "";
    static int Int(JsonObject c, string k) => c[k]?.GetValue<int>() ?? -1;
    static float Float(JsonObject c, string k) => c[k]?.GetValue<float>() ?? 0;
}
