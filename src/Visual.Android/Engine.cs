using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Android.Opengl;
using EGLConfig = Javax.Microedition.Khronos.Egl.EGLConfig;
using Javax.Microedition.Khronos.Opengles;
using Silk.NET.OpenGL;
using Visual.Core;

namespace VisualApp;

sealed class AppSettings
{
    public string? RemoteToken { get; set; }
    public int RemotePort { get; set; } = 8765;
    public string? LastProject { get; set; }
    public string AudioSource { get; set; } = AudioInput.SystemId;
    /// <summary>Internal resolution (1 = full); lower for slow GPUs.</summary>
    public float Scale { get; set; } = 1;
    public int Fps { get; set; } = 60;
    /// <summary>"full", an aspect ratio ("16:9", "4:3", "1:1") or "px" (ViewX..ViewH).</summary>
    public string Canvas { get; set; } = "full";
    public int ViewX { get; set; }
    public int ViewY { get; set; }
    public int ViewW { get; set; } = 1280;
    public int ViewH { get; set; } = 720;
    /// <summary>Lets strobes go past the safe 3 Hz, up to 20 Hz.</summary>
    public bool AllowFastStrobe { get; set; }
    /// <summary>Seconds the audio bands take to fall after a hit.</summary>
    public float AudioRelease { get; set; } = 0.15f;
    /// <summary>Sensibilidad of bass, mid, high, melody.</summary>
    public float[] BandSense { get; set; } = [1, 1, 1, 1];
    public float MelodySmooth { get; set; } = 0.8f;
    public float MaxHz => AllowFastStrobe ? 20f : EffectDefs.SafeStrobeHz;
}

/// <summary>
/// Everything that runs on the GL thread: projects, player, renderer and the remote's commands.
/// Same flow as the desktop App, minus the ImGui panel (the remote page is the editor here).
/// </summary>
sealed class Engine : Java.Lang.Object, GLSurfaceView.IRenderer
{
    public readonly AudioAnalyzer Analyzer = new();
    public readonly Player Player = new();
    /// <summary>Work from other threads (keys) that must run on the GL thread.</summary>
    public readonly ConcurrentQueue<Action> Pending = new();
    public volatile string Status = "";
    public volatile string? AudioError;
    readonly AudioInput audio;
    (string Id, string Label)[] sources = [];
    double sourcesDue;
    public string Url { get; }
    public string? RemoteError => remote.Error;

    readonly string shaderFolder, mediaFolder, settingsPath;
    string[] mediaFiles = [];
    readonly ProjectStore store;
    readonly RemoteServer remote = new();
    readonly AppSettings settings;
    Renderer? renderer;
    int width, height;
    long last = Stopwatch.GetTimestamp();
    double clock, strobeUntil, stateDue;
    Project project = new();
    string loadedName = "", notice = "";
    string[] projectNames = [], shaderFiles = [];

    public Engine(string files, Android.Content.Context ctx)
    {
        shaderFolder = Path.Combine(files, "shaders");
        mediaFolder = Path.Combine(files, "videos");
        mediaFiles = Media.List(mediaFolder);
        settingsPath = Path.Combine(files, "settings.json");
        store = new ProjectStore(Path.Combine(files, "projects"));
        try { settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath)) ?? new(); }
        catch { settings = new(); }
        settings.RemoteToken ??= RandomNumberGenerator.GetHexString(10, true);
        remote.Start(settings.RemotePort, settings.RemoteToken);
        remote.MediaFolder = mediaFolder;
        remote.ShaderFolder = shaderFolder;
        audio = new AudioInput(ctx, Analyzer, remote.Port, remote.Token);
        Url = $"http://{RemoteServer.LocalAddress()}:{remote.Port}/?k={remote.Token}";
        shaderFiles = ShaderFiles();
        OpenProject(settings.LastProject);
        SaveSettings();
    }

    public void OnSurfaceCreated(IGL10? unused, EGLConfig? config)
    {
        // a new context (first start or after it was lost): old GL objects are gone, build everything again
        renderer?.ReleaseMedia(); // stops the old players; the new context has no objects yet to clash with
        var gl = GL.GetApi(GlLoader.Get);
        renderer = new Renderer(gl, Renderer.EsHeader) { ShaderFolder = shaderFolder };
        renderer.OpenMedia = src => AndroidMedia.Open(gl, mediaFolder, src);
    }

    public void OnSurfaceChanged(IGL10? unused, int w, int h) => (width, height) = (w, h);

    public void OnDrawFrame(IGL10? unused)
    {
        long now = Stopwatch.GetTimestamp();
        float dt = (float)Math.Min(0.1, (now - last) / (double)Stopwatch.Frequency);
        last = now;
        while (Pending.TryDequeue(out var a)) a();
        Analyzer.Release = settings.AudioRelease;
        Analyzer.Sensitivity = settings.BandSense ?? [];
        Analyzer.MelodySmooth = settings.MelodySmooth;
        Analyzer.Update(dt);
        RemoteFrame(dt);
        Player.Update(dt);
        if (renderer == null) return;
        renderer.Scale = Math.Clamp(settings.Scale, 0.25f, 1f);
        // whole screen as the area; the device canvas is the default for scenes without their own
        renderer.Render(Player, Analyzer, dt, 0, 0, width, height, width, height, allowFastStrobe: settings.AllowFastStrobe,
            global: Renderer.Fit(settings.Canvas, settings.ViewX, settings.ViewY, settings.ViewW, settings.ViewH, width, height));

        var sc = project.Scenes;
        var cur = Player.Current >= 0 && Player.Current < sc.Count ? sc[Player.Current] : null;
        var prog = cur == null ? null : renderer.Get(cur.Shader);
        var mediaErr = cur == null ? "" : string.Join("\n", cur.Media.Keys.Select(k => renderer.MediaError(cur, k)).Where(e => e != null));
        if (settings.Fps < 60) // GLSurfaceView has no frame cap: wait out the rest of the frame on the GL thread
        {
            double left = 1.0 / settings.Fps - (Stopwatch.GetTimestamp() - now) / (double)Stopwatch.Frequency - 0.002;
            if (left > 0) Thread.Sleep(TimeSpan.FromSeconds(left));
        }
        Status = string.Join("\n", new[] { prog?.Error, mediaErr, AudioError, audio.Error, audio.Waiting, notice }.Where(s => !string.IsNullOrEmpty(s)));
    }

    void RemoteFrame(float dt)
    {
        clock += dt;
        while (remote.Commands.TryDequeue(out var c))
        {
            try
            {
                switch (Remote.Apply(c, Player, shaderFiles, Analyzer.Music))
                {
                    case "strobe":
                        // re-sent every 0.4 s while held; stops by itself if the phone goes quiet
                        strobeUntil = c["on"]?.GetValue<bool>() == true ? clock + 1 : 0;
                        break;
                    case "save": SaveProject(); break;
                    case "open":
                        if (c["name"]?.GetValue<string>() is { } n && projectNames.Contains(n) && n != loadedName)
                        {
                            SaveProject();
                            OpenProject(n);
                        }
                        break;
                    case "new":
                        SaveProject();
                        StartNew(new Project { Name = UniqueName("Proyecto") });
                        break;
                    case "duplicate":
                        SaveProject();
                        var copy = store.Clone(project);
                        copy.Name = UniqueName(project.Name + " copia");
                        StartNew(copy);
                        break;
                    case "import":
                        SaveProject(); // first: its name can't be taken by the import
                        OpenProject(store.Import(c["text"]!.GetValue<string>()).Name);
                        break;
                    case "upload":
                        Remote.SaveShader(shaderFolder, c["name"]!.GetValue<string>(), c["text"]!.GetValue<string>());
                        renderer?.Reload();
                        shaderFiles = ShaderFiles();
                        notice = "";
                        break;
                    case "mediaChanged":
                        renderer?.ReleaseMedia(); // a replaced video reopens
                        mediaFiles = Media.List(mediaFolder);
                        break;
                    case "deleteMedia":
                        if (c["name"]?.GetValue<string>() is { } dm && Media.ValidName(dm))
                        {
                            renderer?.ReleaseMedia();
                            File.Delete(Path.Combine(mediaFolder, dm));
                            mediaFiles = Media.List(mediaFolder);
                        }
                        break;
                    case "device":
                        if (c["source"]?.GetValue<string>() is { } src && src != audio.Active)
                        {
                            settings.AudioSource = src;
                            StartAudio();
                        }
                        if (c["scale"]?.GetValue<float>() is { } s && float.IsFinite(s)) settings.Scale = Math.Clamp(s, 0.25f, 1f);
                        if (c["fps"]?.GetValue<int>() is { } f) settings.Fps = f >= 60 ? 60 : 30;
                        if (c["canvas"]?.GetValue<string>() is { } cv && SceneCanvas.Modes.Contains(cv)) settings.Canvas = cv;
                        if (c["x"]?.GetValue<int>() is { } vx) settings.ViewX = Math.Max(0, vx);
                        if (c["y"]?.GetValue<int>() is { } vy) settings.ViewY = Math.Max(0, vy);
                        if (c["w"]?.GetValue<int>() is { } vw) settings.ViewW = Math.Max(1, vw);
                        if (c["h"]?.GetValue<int>() is { } vh) settings.ViewH = Math.Max(1, vh);
                        if (c["fastStrobe"]?.GetValue<bool>() is { } fs) settings.AllowFastStrobe = fs;
                        if (c["release"]?.GetValue<float>() is { } rl && float.IsFinite(rl)) settings.AudioRelease = Math.Clamp(rl, 0.02f, 0.4f);
                        if (c["sense"] is System.Text.Json.Nodes.JsonArray sa && sa.Count == 4)
                            settings.BandSense = sa.Select(x => x?.GetValue<float>() is float v && float.IsFinite(v) ? Math.Clamp(v, 0.1f, 10f) : 1).ToArray();
                        if (c["melody"]?.GetValue<float>() is { } ms && float.IsFinite(ms)) settings.MelodySmooth = Math.Clamp(ms, 0.1f, 3f);
                        SaveSettings();
                        break;
                }
            }
            catch (Exception e) { notice = "Control remoto: " + e.Message; }
        }
        remote.AnswerExports(project);
        if (renderer != null) renderer.StrobeHeld = clock < strobeUntil;
        if (clock < stateDue || renderer == null) return;
        stateDue = clock + 0.2;
        var sc = project.Scenes;
        var isf = Player.Current >= 0 && Player.Current < sc.Count ? renderer.Get(sc[Player.Current].Shader).Isf : null;
        remote.State = Remote.State(Player, isf, loadedName, projectNames, shaderFiles, mediaFiles, Analyzer,
            settings.MaxHz, renderer.StrobeActive, Device(), renderer.AutoDj, f => renderer.Get(f).Isf, renderer.AutoStrobe);
    }

    object Device()
    {
        if (clock >= sourcesDue) { sources = audio.Sources(); sourcesDue = clock + 2; } // picks up USB plug / unplug
        return new
        {
            Sources = sources.Select(s => new { s.Id, s.Label }),
            Source = audio.Active,
            settings.Scale,
            settings.Fps,
            settings.Canvas,
            settings.ViewX,
            settings.ViewY,
            settings.ViewW,
            settings.ViewH,
            settings.AllowFastStrobe,
            settings.AudioRelease,
            settings.BandSense,
            settings.MelodySmooth,
            ScreenW = width,
            ScreenH = height,
        };
    }

    /// <summary>Opens the saved audio source (needs the RECORD_AUDIO permission).</summary>
    public void StartAudio() => audio.Start(settings.AudioSource);

    string[] ShaderFiles() => Directory.Exists(shaderFolder)
        ? Directory.GetFiles(shaderFolder, "*.fs").Select(f => Path.GetFileName(f)).Order().ToArray()
        : [];

    void OpenProject(string? name)
    {
        projectNames = store.List();
        name ??= projectNames.FirstOrDefault();
        project = new Project();
        if (name != null && store.Exists(name))
        {
            try { project = store.Load(name); }
            catch (Exception e) { notice = $"No se pudo abrir {name}: {e.Message}"; }
        }
        else
        {
            project = new Project
            {
                Name = UniqueName("Demo"),
                Scenes = shaderFiles.Select(f => new Scene { Shader = f, Duration = 20 }).ToList(),
            };
        }
        project.EnsureEffects();
        loadedName = store.Exists(project.Name) ? project.Name : "";
        Player.Start(project);
        settings.LastProject = project.Name;
    }

    void StartNew(Project p)
    {
        project = p;
        project.EnsureEffects();
        loadedName = "";
        Player.Start(project);
        SaveProject();
    }

    void SaveProject()
    {
        if (project.Name.Trim() == "") project.Name = UniqueName("Proyecto");
        if (project.Name != loadedName && store.Exists(project.Name))
        {
            notice = $"Ya existe un proyecto llamado \"{project.Name}\"";
            return;
        }
        if (loadedName != "" && loadedName != project.Name) store.Delete(loadedName); // rename
        store.Save(project);
        loadedName = project.Name;
        projectNames = store.List();
        settings.LastProject = project.Name;
        SaveSettings();
    }

    string UniqueName(string baseName)
    {
        var name = baseName;
        for (int n = 2; store.Exists(name); n++) name = $"{baseName} {n}";
        return name;
    }

    void SaveSettings()
    {
        try { File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings)); }
        catch (Exception e) { notice = "No se pudieron guardar los ajustes: " + e.Message; }
    }

    /// <summary>Call only while the GL thread is paused.</summary>
    public void Save()
    {
        try { SaveProject(); }
        catch (Exception e) { notice = "No se pudo guardar: " + e.Message; }
    }

    public void Stop()
    {
        audio.Stop();
        remote.Stop();
    }
}
