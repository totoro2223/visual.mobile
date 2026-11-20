using System.Text.Json;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Visual.Core;

namespace Visual.Desktop;

static class Program
{
    [STAThread] // FolderBrowserDialog needs STA
    static int Main(string[] args)
    {
        if (args.Contains("--check"))
        {
            try { SelfCheck.Run(); return 0; }
            catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
        }

        try
        {
            var settings = Settings.Load();
            var options = WindowOptions.Default with
            {
                Size = new Vector2D<int>(1280, 720),
                Title = "Visual",
                VSync = true,
                API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Compatability, ContextFlags.Default, new APIVersion(3, 3)),
            };
            using var window = Window.Create(options);
            App? app = null;
            window.Load += () => app = new App(window, settings);
            window.Render += dt => app!.Frame((float)dt);
            window.Closing += () => app?.Dispose();
            window.Run();
            return 0;
        }
        catch (Exception e)
        {
            System.Windows.Forms.MessageBox.Show(e.ToString(), "Visual: error");
            return 1;
        }
    }
}

sealed class Settings
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Visual");
    static string FilePath => Path.Combine(Dir, "settings.json");
    public static string ProjectsDir => Path.Combine(Dir, "projects");

    public string ShaderFolder { get; set; } = Path.Combine(AppContext.BaseDirectory, "shaders");
    /// <summary>Videos and pictures the scenes can use (and where the remote uploads them).</summary>
    public string MediaFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Visual");
    public string? AudioDeviceId { get; set; }
    public float InputGain { get; set; } = 1;
    public float AudioRelease { get; set; } = 0.15f;
    /// <summary>Sensibilidad of bass, mid, high, melody.</summary>
    public float[] BandSense { get; set; } = [1, 1, 1, 1];
    public float MelodySmooth { get; set; } = 0.8f;
    public bool AllowFastStrobe { get; set; }
    public string? LastProject { get; set; }
    // viewer rectangle inside the window, in pixels from the top-left; per machine because it depends on the screen
    public bool FillWindow { get; set; } = true;
    public int ViewX { get; set; }
    public int ViewY { get; set; }
    public int ViewW { get; set; } = 1280;
    public int ViewH { get; set; } = 720;
    public bool RemoteEnabled { get; set; }
    public int RemotePort { get; set; } = 8765;
    public string? RemoteToken { get; set; }
    public bool NetAudioEnabled { get; set; }
    public string NetAudioUrl { get; set; } = "";
    public int OutputMonitor { get; set; }
    public bool SeparateOutput { get; set; }
    /// <summary>Internal resolution as a fraction of the viewer / output window (0.25..1): lower = lighter on the GPU.</summary>
    public float Scale { get; set; } = 1;

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
