using System.Text.Json;
using System.Text.Json.Serialization;

namespace Visual.Core;

/// <summary>A shader with its settings: the scene's base picture, or one of its layers.</summary>
public abstract class Picture
{
    public string Shader { get; set; } = "";   // file name inside the shader folder (portable between PC and TV)
    /// <summary>Video / picture for each ISF image input, by input name.</summary>
    public Dictionary<string, SceneMedia> Media { get; set; } = [];
    public Dictionary<string, ParamState> Params { get; set; } = [];
    public Dictionary<string, ParamState> Effects { get; set; } = [];

    public virtual void EnsureEffects()
    {
        foreach (var d in EffectDefs.All)
            if (!Effects.TryGetValue(d.Name, out var ps) || ps.Value.Length == 0) Effects[d.Name] = new ParamState { Value = [d.Default] };
    }

    /// <summary>Settings readable (not null) after loading an outside file.</summary>
    internal bool Valid => Shader != null && Media != null && Params != null && Effects != null
                           && Params.Values.Concat(Effects.Values).All(ps => ps?.Value != null && ps.Mod != null);
}

/// <summary>Shape of the change between scenes; Random picks one of the others each time.</summary>
public enum Transition { Fade, Wipe, Circle, Blinds, Dissolve, Pixels, Zoom, Random }

public enum BlendMode { Normal, Add, Subtract, Multiply, Screen, Overlay, SoftLight, HardLight, Difference, Exclusion, Lighten, Darken, Dodge, Burn, Hue, Color, Luminosity }

/// <summary>Drawn over what is below it in the scene, with its own effects; a filter shader (input "inputImage") gets that picture.</summary>
public sealed class Layer : Picture
{
    public static readonly string[] BlendNames = ["Normal", "Sumar", "Restar", "Multiplicar", "Pantalla", "Superponer", "Luz suave", "Luz fuerte",
        "Diferencia", "Exclusión", "Aclarar", "Oscurecer", "Sobreexponer", "Subexponer", "Tono", "Color", "Luminosidad"];
    /// <summary>The ISF input a filter layer reads what is below from (unless a video is set on it).</summary>
    public const string Below = "inputImage";

    public BlendMode Blend { get; set; }
    /// <summary>Hidden: draws nothing but keeps its settings.</summary>
    public bool Off { get; set; }

    public Layer Clone() => JsonSerializer.Deserialize<Layer>(JsonSerializer.Serialize(this))!;
}

public sealed class Scene : Picture
{
    /// <summary>Optional name to tell scenes apart (the same shader used several times); empty = the shader's.</summary>
    public string Name { get; set; } = "";
    public float Duration { get; set; } = 20;  // seconds, 0 = manual
    /// <summary>For the Auto DJ: 0 = automatic (from Motion), 1 soft, 2 medium, 3 strong.</summary>
    public int Intensity { get; set; }
    /// <summary>How much the picture moves, measured while it plays (0..1); -1 = not measured yet.</summary>
    public float Motion { get; set; } = -1;
    /// <summary>Drawn over the base shader in order (first = right above it), before the scene's effects.</summary>
    public List<Layer> Layers { get; set; } = [];
    /// <summary>Own internal resolution for this scene (0.25..1); 0 = the device's (global) one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float Quality { get; set; }
    /// <summary>Own canvas for this scene; null = the device's (global) one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SceneCanvas? Canvas { get; set; }

    [JsonIgnore] public string Title => Name.Trim() != "" ? Name.Trim() : Shader;

    /// <summary>Small picture of the scene as it last played (Thumbs.Pack), for the lists; null = not played yet.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Thumb { get; set; }
    /// <summary>Changes when Thumb does (0 = none): the web panel's image address.</summary>
    [JsonIgnore] public int ThumbKey => Thumb == null ? 0 : ReferenceEquals(thumbOf, Thumb) ? thumbKey : (thumbKey = (thumbOf = Thumb).GetHashCode() | 1);
    string? thumbOf;
    int thumbKey;
    /// <summary>Params and effects before the last Sorpresa (not saved).</summary>
    [JsonIgnore] public (Dictionary<string, ParamState> Params, Dictionary<string, ParamState> Effects)? Undo { get; set; }

    // ponytail: motion thresholds guessed (mean color change per 1/4 s on a 32x18 thumbnail); retune with real shaders
    public const float SoftMotion = 0.03f, StrongMotion = 0.08f;
    public static readonly string[] LevelNames = ["Auto", "Suave", "Media", "Fuerte"];

    /// <summary>1 soft, 2 medium, 3 strong: the chosen one, else from the measured motion (medium until measured).</summary>
    [JsonIgnore] public int Level => Intensity is >= 1 and <= 3 ? Intensity : MeasuredLevel;
    [JsonIgnore] public int MeasuredLevel => Motion < 0 ? 2 : Motion < SoftMotion ? 1 : Motion < StrongMotion ? 2 : 3;

    public override void EnsureEffects()
    {
        base.EnsureEffects();
        Layers ??= [];
        foreach (var l in Layers) l.EnsureEffects();
    }
}

/// <summary>Canvas: Mode "full", an aspect ratio, or "px" with X, Y, W, H in pixels of the output (see Renderer.Fit).</summary>
public sealed class SceneCanvas
{
    public static readonly string[] Modes = ["full", "16:9", "4:3", "1:1", "px"];
    public string Mode { get; set; } = "full";
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; } = 1280;
    public int H { get; set; } = 720;
}

/// <summary>What plays in an image input: a file of the device's media folder or an http(s) URL; looped and muted.</summary>
public sealed class SceneMedia
{
    public const float MinSpeed = 0.1f, MaxSpeed = 4f;
    public string Src { get; set; } = "";
    public float Speed { get; set; } = 1;
}

public sealed class Project
{
    public string Name { get; set; } = "Nuevo";
    public List<Scene> Scenes { get; set; } = [];
    public bool Shuffle { get; set; }
    public bool Crossfade { get; set; }
    public float CrossfadeSeconds { get; set; } = 2;
    public Transition Transition { get; set; }
    /// <summary>Length of the change in bars (4 beats) while the BPM is known; 0 = CrossfadeSeconds.</summary>
    public float CrossfadeBars { get; set; }
    public static readonly string[] TransitionNames = ["Fundido", "Barrido", "Círculo", "Persianas", "Disolver", "Píxeles", "Zoom", "Al azar"];
    public static readonly float[] BarChoices = [0, 0.5f, 1, 2, 4];
    public static readonly string[] BarNames = ["En segundos", "1/2 compás", "1 compás", "2 compases", "4 compases"];
    /// <summary>Only read from projects saved when effects were global; moved into the scenes by EnsureEffects.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, ParamState>? Effects { get; set; }
    public AutoStrobe AutoStrobe { get; set; } = new();
    public AutoDj AutoDj { get; set; } = new();

    public void EnsureEffects()
    {
        foreach (var s in Scenes)
        {
            if (Effects != null && s.Effects.Count == 0)
                foreach (var (k, v) in Effects) s.Effects[k] = v.Clone();
            s.EnsureEffects();
        }
        Effects = null;
        AutoStrobe.Ensure();
    }

    public void CopyEffects(Scene from)
    {
        foreach (var s in Scenes)
            if (s != from) s.Effects = from.Effects.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());
    }
}

/// <summary>Projects as one JSON file each.</summary>
public sealed class ProjectStore(string folder)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public string Folder => folder;

    public string[] List() => Directory.Exists(folder)
        ? Directory.GetFiles(folder, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)).Order().ToArray()
        : [];

    public bool Exists(string name) => File.Exists(PathOf(name));
    public Project Load(string name) => JsonSerializer.Deserialize<Project>(File.ReadAllText(PathOf(name)), Json)!;
    public void Delete(string name) => File.Delete(PathOf(name));
    public Project Clone(Project p) => JsonSerializer.Deserialize<Project>(JsonSerializer.Serialize(p, Json), Json)!;
    public static string ToJson(Project p) => JsonSerializer.Serialize(p, Json);

    /// <summary>A project file from outside (download / upload); throws with a readable message if it isn't one.</summary>
    public static Project Parse(string json)
    {
        Project? p;
        try { p = JsonSerializer.Deserialize<Project>(json, Json); }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException) { throw new InvalidDataException("No es un proyecto válido: " + e.Message); }
        if (p?.Scenes == null || p.AutoStrobe == null || p.Name == null
            || p.Scenes.Any(s => s?.Name == null || !s.Valid || s.Layers?.All(l => l?.Valid == true) == false))
            throw new InvalidDataException("No es un proyecto válido");
        p.AutoDj ??= new();
        p.EnsureEffects();
        return p;
    }

    /// <summary>Saves an outside project under a free name ("Nombre 2" if taken) and returns it.</summary>
    public Project Import(string json)
    {
        var p = Parse(json);
        var name = p.Name.Trim() is { Length: > 0 } n ? (n.Length > 60 ? n[..60] : n) : "Importado";
        p.Name = name;
        for (int i = 2; Exists(p.Name); i++) p.Name = $"{name} {i}";
        Save(p);
        return p;
    }

    public void Save(Project p)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(PathOf(p.Name), JsonSerializer.Serialize(p, Json));
    }

    string PathOf(string name) =>
        Path.Combine(folder, string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".json");
}

/// <summary>Runs a project's scene list: durations, order/shuffle, optional crossfade.</summary>
public sealed class Player
{
    readonly Random rng = new();
    public Project Project { get; private set; } = new();
    public int Current { get; private set; } = -1;
    public int Previous { get; private set; } = -1; // scene fading out, -1 if none
    public double SceneTime { get; private set; }
    public double PrevTime { get; private set; }
    public float Fade { get; private set; } = 1;   // 0 = only Previous visible, 1 = only Current
    /// <summary>Shape of the change running now (Random already picked).</summary>
    public Transition Shape { get; private set; }
    /// <summary>The Auto DJ is changing scenes with the music: the scenes' durations wait.</summary>
    public bool AutoDriving { get; set; }

    int Count => Project.Scenes.Count;

    public void Start(Project p)
    {
        Project = p;
        Current = Count > 0 ? 0 : -1;
        Previous = -1;
        SceneTime = 0;
        Fade = 1;
    }

    /// <param name="bpm">the music's tempo (0 = not known): changes measured in bars follow it</param>
    public void Update(double dt, float bpm = 0)
    {
        if (Current >= Count) Current = Count - 1;
        if (Current < 0 && Count > 0) Current = 0;
        SceneTime += dt;
        PrevTime += dt;
        if (Previous >= 0)
        {
            double secs = Project.CrossfadeBars > 0 && bpm > 0 ? Math.Min(Project.CrossfadeBars, 8) * 240 / bpm : Project.CrossfadeSeconds;
            Fade = secs > 0 ? Math.Min(1, Fade + (float)(dt / secs)) : 1;
            if (Fade >= 1) Previous = -1;
        }
        if (!AutoDriving && Current >= 0 && Project.Scenes[Current].Duration is > 0 and var d && SceneTime >= d) Next();
    }

    public void Next() => GoTo(PickNext());

    public void GoTo(int i)
    {
        if (i < 0 || i >= Count) return;
        if (Project.Crossfade && Current >= 0 && i != Current)
        {
            (Previous, PrevTime, Fade) = (Current, SceneTime, 0);
            Shape = Project.Transition is >= Transition.Random or < 0 ? (Transition)rng.Next((int)Transition.Random) : Project.Transition;
        }
        else Previous = -1;
        Current = i;
        SceneTime = 0;
    }

    int PickNext()
    {
        if (Count == 0) return -1;
        if (!Project.Shuffle || Count == 1) return (Current + 1) % Count;
        int r;
        do r = rng.Next(Count); while (r == Current);
        return r;
    }

    public void Move(int from, int to)
    {
        var l = Project.Scenes;
        var s = l[from];
        l.RemoveAt(from);
        l.Insert(to, s);
        Current = Remap(Current);
        Previous = Remap(Previous);

        int Remap(int i) =>
            i == from ? to
            : from < to && i > from && i <= to ? i - 1
            : from > to && i >= to && i < from ? i + 1
            : i;
    }

    public void Remove(int i)
    {
        Project.Scenes.RemoveAt(i);
        Previous = -1;
        if (Current == i) { Current = Math.Min(i, Count - 1); SceneTime = 0; }
        else if (Current > i) Current--;
    }
}
