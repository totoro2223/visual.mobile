using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using Visual.Core;

namespace Visual.Desktop;

sealed partial class App : IDisposable
{
    readonly IWindow window;
    readonly GL gl;
    readonly IInputContext input;
    readonly ImGuiController imgui;
    ImFontPtr? bold; // null when Segoe UI Bold is missing
    readonly Renderer renderer;
    readonly AudioAnalyzer analyzer = new();
    readonly AudioCapture capture;
    readonly Settings settings;
    readonly ProjectStore store = new(Settings.ProjectsDir);
    readonly Player player = new();
    Project project = new();
    string loadedName = "";
    string[] projectNames = [];
    string[] shaderFiles = [];
    string[] mediaFiles = [];
    bool showUi = true, fullscreen;
    Vector2D<int> savedPos, savedSize;

    public App(IWindow window, Settings settings)
    {
        this.window = window;
        this.settings = settings;
        gl = window.CreateOpenGL();
        input = window.CreateInput();
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string font = Path.Combine(fonts, "segoeui.ttf"), boldFile = Path.Combine(fonts, "segoeuib.ttf");
        imgui = File.Exists(font)
            ? new ImGuiController(gl, window, input, new ImGuiFontConfig(font, 18), () => // default font has no accents
            {
                if (File.Exists(boldFile)) bold = ImGui.GetIO().Fonts.AddFontFromFileTTF(boldFile, 18); // effect names
            })
            : new ImGuiController(gl, window, input);
        IsfShader.ExpandDefines = settings.ExpandDefines;
        renderer = new Renderer(gl, Renderer.DesktopHeader) { ShaderFolder = settings.ShaderFolder };
        renderer.OpenMedia = src => DesktopMedia.Open(gl, settings.MediaFolder, src);
        capture = new AudioCapture(analyzer);

        sources = AudioCapture.List();
        var src = sources.FirstOrDefault(s => s.Id == settings.AudioDeviceId)
                  ?? sources.FirstOrDefault(s => s.Id == AudioCapture.DefaultOutputId())
                  ?? sources.FirstOrDefault();
        if (src != null) capture.Start(src);

        foreach (var kb in input.Keyboards) kb.KeyDown += OnKey;
        ReloadShaders();
        OpenProject(settings.LastProject);
        if (settings.RemoteEnabled) StartRemote();
        if (settings.NetAudioEnabled) SetNetAudio(true);
        if (settings.SeparateOutput) OpenOutput();
    }

    public void Frame(float dt)
    {
        analyzer.InputGain = settings.InputGain;
        analyzer.Release = settings.AudioRelease;
        analyzer.Sensitivity = settings.BandSense ?? [];
        analyzer.MelodySmooth = settings.MelodySmooth;
        analyzer.Update(dt);
        RemoteFrame(dt);
        player.Update(dt, analyzer.Music.Bpm);
        var fb = window.FramebufferSize;
        var (x, y, w, h) = (0, 0, fb.X, fb.Y);
        if (!settings.FillWindow && fb.X > 0 && fb.Y > 0)
        {
            x = Math.Clamp(settings.ViewX, 0, Math.Max(0, fb.X - 1));
            y = Math.Clamp(settings.ViewY, 0, Math.Max(0, fb.Y - 1));
            w = Math.Clamp(settings.ViewW, 1, fb.X - x);
            h = Math.Clamp(settings.ViewH, 1, fb.Y - y);
        }
        var outSize = OutputSize();
        if (outSize is var (ow, oh) && fb.X > 0 && fb.Y > 0)
        {
            // drawn at the output's resolution; this window shows a letterboxed preview
            float s = Math.Min(fb.X / (float)ow, fb.Y / (float)oh);
            (w, h) = (Math.Max(1, (int)(ow * s)), Math.Max(1, (int)(oh * s)));
            (x, y) = ((fb.X - w) / 2, (fb.Y - h) / 2);
        }
        renderer.Scale = settings.Scale;
        renderer.Render(player, analyzer, dt, x, y, w, h, fb.X, fb.Y, settings.AllowFastStrobe, outSize?.W ?? 0, outSize?.H ?? 0);
        DrawOutput();
        if (!showUi) return;
        imgui.Update(dt);
        DrawUi();
        imgui.Render();
    }

    void OnKey(IKeyboard kb, Key key, int scancode)
    {
        if (key == Key.F1) { showUi = !showUi; return; }
        if (key == Key.F11 || (key == Key.Escape && fullscreen)) { ToggleFullscreen(); return; }
        if (showUi && ImGui.GetIO().WantCaptureKeyboard) return;
        if (key is Key.N or Key.Right) player.Next();
    }

    void ToggleFullscreen(IMonitor? target = null)
    {
        // borderless window on the current monitor: works for a projector / second screen without a mode switch
        if (!fullscreen && (target ?? window.Monitor) is { } m)
        {
            savedPos = window.Position;
            savedSize = window.Size;
            window.WindowBorder = WindowBorder.Hidden;
            window.Position = m.Bounds.Origin;
            window.Size = m.Bounds.Size;
            fullscreen = true;
        }
        else if (fullscreen)
        {
            window.WindowBorder = WindowBorder.Resizable;
            window.Position = savedPos;
            window.Size = savedSize;
            fullscreen = false;
        }
    }

    string[] ShaderFiles() => Directory.Exists(settings.ShaderFolder)
        ? Directory.GetFiles(settings.ShaderFolder, "*.fs").Select(f => Path.GetFileName(f)).Order().ToArray()
        : [];

    void OpenProject(string? name)
    {
        projectNames = store.List();
        name ??= projectNames.FirstOrDefault();
        project = new Project();
        if (name != null && store.Exists(name))
        {
            try { project = store.Load(name); }
            catch (Exception e) { status = $"No se pudo abrir {name}: {e.Message}"; }
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
        player.Start(project);
        selected = project.Scenes.Count > 0 ? 0 : -1;
        settings.LastProject = project.Name;
    }

    void SaveProject()
    {
        if (project.Name.Trim() == "") project.Name = UniqueName("Proyecto");
        if (project.Name != loadedName && store.Exists(project.Name))
        {
            status = $"Ya existe un proyecto llamado \"{project.Name}\"";
            return;
        }
        if (loadedName != "" && loadedName != project.Name) store.Delete(loadedName); // rename
        store.Save(project);
        loadedName = project.Name;
        projectNames = store.List();
        settings.LastProject = project.Name;
        settings.Save();
        status = "Guardado";
    }

    void NewProject()
    {
        SaveProject();
        StartNew(new Project { Name = UniqueName("Proyecto") });
    }

    void DuplicateProject()
    {
        SaveProject();
        var copy = store.Clone(project);
        copy.Name = UniqueName(project.Name + " copia");
        StartNew(copy);
    }

    /// <summary>Saves the current project, then stores the outside one under a free name and opens it.</summary>
    void ImportProject(string json)
    {
        SaveProject(); // first: its name can't be taken by the import
        OpenProject(store.Import(json).Name); // a bad file throws here and nothing else changes
        status = $"Proyecto importado: {project.Name}";
    }

    string UniqueName(string baseName)
    {
        var name = baseName;
        for (int n = 2; store.Exists(name); n++) name = $"{baseName} {n}";
        return name;
    }

    void SetShaderFolder(string folder)
    {
        settings.ShaderFolder = folder;
        renderer.ShaderFolder = folder;
        ReloadShaders();
    }

    void ReloadShaders()
    {
        CopyBundledShaders();
        renderer.Reload();
        renderer.ReleaseMedia();
        shaderFiles = ShaderFiles();
        mediaFiles = Media.List(settings.MediaFolder);
        remote.MediaFolder = settings.MediaFolder;
        remote.ShaderFolder = settings.ShaderFolder;
    }

    /// <summary>The shaders built next to the exe go to the chosen shader folder: missing ones, or ones the build has newer.
    /// A copy the user edited later is newer, so it is kept.</summary>
    void CopyBundledShaders()
    {
        string from = Path.Combine(AppContext.BaseDirectory, "shaders"), to = settings.ShaderFolder;
        if (!Directory.Exists(from) || Path.GetFullPath(from).TrimEnd('\\').Equals(Path.GetFullPath(to).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
        foreach (var f in Directory.GetFiles(from, "*.fs").Concat(Directory.GetFiles(from, "*.vs")))
        {
            var dst = Path.Combine(to, Path.GetFileName(f));
            try
            {
                if (File.Exists(dst) && File.GetLastWriteTimeUtc(dst) >= File.GetLastWriteTimeUtc(f)) continue;
                Directory.CreateDirectory(to);
                File.Copy(f, dst, overwrite: true); // keeps the date, so it isn't copied again
            }
            catch (IOException) { } // folder missing / file in use: the old copy stays
            catch (UnauthorizedAccessException) { }
        }
    }

    void SetMediaFolder(string folder)
    {
        settings.MediaFolder = folder;
        ReloadShaders();
    }

    public void Dispose()
    {
        SaveProject();
        settings.Save();
        remote.Dispose();
        capture.Dispose();
        capture.Net?.Dispose();
        CloseOutput();
        renderer.Dispose();
        imgui.Dispose();
        input.Dispose();
        gl.Dispose();
    }
}
