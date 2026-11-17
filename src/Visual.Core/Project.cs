using System.Text.Json;
using System.Text.Json.Serialization;

namespace Visual.Core;

public sealed class Scene
{
    public string Shader { get; set; } = "";   // file name inside the shader folder (portable between PC and TV)
    public float Duration { get; set; } = 20;  // seconds, 0 = manual
    public Dictionary<string, ParamState> Params { get; set; } = [];
    public Dictionary<string, ParamState> Effects { get; set; } = [];

    public void EnsureEffects()
    {
        foreach (var d in EffectDefs.All)
            if (!Effects.TryGetValue(d.Name, out var ps) || ps.Value.Length == 0) Effects[d.Name] = new ParamState { Value = [d.Default] };
    }
}

public sealed class Project
{
    public string Name { get; set; } = "Nuevo";
    public List<Scene> Scenes { get; set; } = [];
    public bool Shuffle { get; set; }
    public bool Crossfade { get; set; }
    public float CrossfadeSeconds { get; set; } = 2;
    /// <summary>Only read from projects saved when effects were global; moved into the scenes by EnsureEffects.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, ParamState>? Effects { get; set; }
    public AutoStrobe AutoStrobe { get; set; } = new();

    public void EnsureEffects()
    {
        foreach (var s in Scenes)
        {
            if (Effects != null && s.Effects.Count == 0)
                foreach (var (k, v) in Effects) s.Effects[k] = v.Clone();
            s.EnsureEffects();
        }
        Effects = null;
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

    int Count => Project.Scenes.Count;

    public void Start(Project p)
    {
        Project = p;
        Current = Count > 0 ? 0 : -1;
        Previous = -1;
        SceneTime = 0;
        Fade = 1;
    }

    public void Update(double dt)
    {
        if (Current >= Count) Current = Count - 1;
        if (Current < 0 && Count > 0) Current = 0;
        SceneTime += dt;
        PrevTime += dt;
        if (Previous >= 0)
        {
            Fade = Project.CrossfadeSeconds > 0 ? Math.Min(1, Fade + (float)(dt / Project.CrossfadeSeconds)) : 1;
            if (Fade >= 1) Previous = -1;
        }
        if (Current >= 0 && Project.Scenes[Current].Duration is > 0 and var d && SceneTime >= d) Next();
    }

    public void Next() => GoTo(PickNext());

    public void GoTo(int i)
    {
        if (i < 0 || i >= Count) return;
        if (Project.Crossfade && Current >= 0 && i != Current) { Previous = Current; PrevTime = SceneTime; Fade = 0; }
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
