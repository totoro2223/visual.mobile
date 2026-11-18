using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Visual.Core;

public enum IsfType { Float, Bool, Long, Event, Point2D, Color, Image, Audio, AudioFFT }

public sealed class IsfInput
{
    public string Name = "";
    public string Label = "";
    public IsfType Type;
    public float[] Default = [0f];
    public float[] Min = [0f];
    public float[] Max = [1f];
    public int[] Values = [];      // long
    public string[] Labels = [];   // long
    public bool IsTexture => Type is IsfType.Image or IsfType.Audio or IsfType.AudioFFT;
}

/// <summary>ISF (https://isf.video) fragment shader: JSON header + GLSL body.</summary>
public sealed class IsfShader
{
    public string Description = "";
    public List<IsfInput> Inputs = [];
    /// <summary>Render target of each pass ("" = the scene's output). Always at least one pass.</summary>
    public List<string> Passes = [""];
    /// <summary>Named buffers (pass targets, PERSISTENT_BUFFERS): kept between frames, readable as images.</summary>
    public List<string> Buffers = [];
    public int PassCount => Passes.Count;
    public string Source = "";
    /// <summary>Body of the companion .vs vertex shader, if there is one.</summary>
    public string? VertexSource;

    // the JSON comment may come after other comments (credits): the first /*{ ... }*/ of the file
    static readonly Regex Header = new(@"/\*\s*(\{.*?\})\s*\*/", RegexOptions.Singleline);
    static readonly Regex Identifier = new(@"^[A-Za-z_]\w*$");
    static readonly Regex VersionLine = new(@"^\s*#version.*$", RegexOptions.Multiline);
    static readonly Regex Macro = new(@"\b(IMG_PIXEL|IMG_NORM_PIXEL|IMG_THIS_PIXEL|IMG_THIS_NORM_PIXEL|IMG_SIZE)\s*\(");

    /// <summary>folder/file (.fs) plus its .vs next to it, if any.</summary>
    public static IsfShader Load(string folder, string file)
    {
        var s = Parse(File.ReadAllText(Path.Combine(folder, file)));
        var vs = Path.Combine(folder, Path.ChangeExtension(file, ".vs"));
        if (File.Exists(vs)) s.VertexSource = File.ReadAllText(vs);
        return s;
    }

    public static IsfShader Parse(string source)
    {
        var s = new IsfShader { Source = source };
        var m = Header.Match(source);
        if (!m.Success) return s; // plain GLSL without ISF header still renders
        using var doc = JsonDocument.Parse(m.Groups[1].Value,
            new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        if (root.TryGetProperty("DESCRIPTION", out var d)) s.Description = d.ToString();
        if (root.TryGetProperty("INPUTS", out var inputs) && inputs.ValueKind == JsonValueKind.Array)
            foreach (var e in inputs.EnumerateArray())
                if (ParseInput(e) is { } i) s.Inputs.Add(i);
        // ponytail: buffers are always the render size and 8-bit; WIDTH/HEIGHT expressions and FLOAT are ignored
        if (root.TryGetProperty("PASSES", out var p) && p.ValueKind == JsonValueKind.Array && p.GetArrayLength() > 0)
            s.Passes = p.EnumerateArray().Select(x => s.Buffer(x.ValueKind == JsonValueKind.Object ? Str(x, "TARGET") : "")).ToList();
        if (root.TryGetProperty("PERSISTENT_BUFFERS", out var pb)) // ISF v1: a list of names or { name: {...} }
            foreach (var n in pb.ValueKind == JsonValueKind.Array ? pb.EnumerateArray().Select(x => x.ToString())
                     : pb.ValueKind == JsonValueKind.Object ? pb.EnumerateObject().Select(x => x.Name) : Enumerable.Empty<string>())
                s.Buffer(n);
        return s;
    }

    /// <summary>Registers a buffer name (once) and returns it; "" for no / unusable names (they draw to the output).</summary>
    string Buffer(string name)
    {
        if (!Identifier.IsMatch(name) || Inputs.Any(i => i.Name == name)) return "";
        if (!Buffers.Contains(name)) Buffers.Add(name);
        return name;
    }

    static IsfInput? ParseInput(JsonElement e)
    {
        IsfType? type = Str(e, "TYPE").ToLowerInvariant() switch
        {
            "float" => IsfType.Float, "bool" => IsfType.Bool, "long" => IsfType.Long, "event" => IsfType.Event,
            "point2d" => IsfType.Point2D, "color" => IsfType.Color, "image" => IsfType.Image,
            "audio" => IsfType.Audio, "audiofft" => IsfType.AudioFFT, _ => null,
        };
        var name = Str(e, "NAME");
        if (type is not { } t || name == "") return null;

        int n = t switch { IsfType.Point2D => 2, IsfType.Color => 4, _ => 1 };
        var i = new IsfInput { Name = name, Type = t, Label = Str(e, "LABEL") is { Length: > 0 } l ? l : name };
        i.Default = Floats(e, "DEFAULT", n) ?? (t == IsfType.Color ? [1, 1, 1, 1] : new float[n]);
        i.Min = Floats(e, "MIN", n) ?? new float[n];
        // ponytail: point2D without MAX is guessed as normalized or 1080p pixels; real fix needs the render size at edit time
        i.Max = Floats(e, "MAX", n) ?? (t == IsfType.Point2D
            ? (i.Default.All(v => v <= 1) ? [1, 1] : [1920, 1080])
            : Enumerable.Repeat(1f, n).ToArray());

        if (t == IsfType.Long)
        {
            var labels = e.TryGetProperty("LABELS", out var la) && la.ValueKind == JsonValueKind.Array
                ? la.EnumerateArray().Select(x => x.ToString()).ToArray() : null;
            i.Values = e.TryGetProperty("VALUES", out var va) && va.ValueKind == JsonValueKind.Array
                ? va.EnumerateArray().Select(x => (int)Num(x)).ToArray()
                : labels != null ? Enumerable.Range(0, labels.Length).ToArray()
                : Enumerable.Range((int)i.Min[0], Math.Clamp((int)i.Max[0] - (int)i.Min[0] + 1, 1, 100)).ToArray();
            i.Labels = labels is { } lb && lb.Length == i.Values.Length ? lb : i.Values.Select(v => v.ToString()).ToArray();
            if (!e.TryGetProperty("DEFAULT", out _) && i.Values.Length > 0) i.Default = [i.Values[0]];
        }
        return i;
    }

    static string Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static float Num(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => v.GetSingle(),
        JsonValueKind.True => 1,
        _ => 0,
    };

    static float[]? Floats(JsonElement e, string key, int n)
    {
        if (!e.TryGetProperty(key, out var v)) return null;
        var r = new float[n];
        if (v.ValueKind == JsonValueKind.Array)
        {
            int k = 0;
            foreach (var x in v.EnumerateArray()) if (k < n) r[k++] = Num(x);
            if (n == 4 && v.GetArrayLength() == 3) r[3] = 1; // rgb without alpha
        }
        else Array.Fill(r, Num(v));
        return r;
    }

    /// <summary>Adds missing parameter values (defaults) for this shader's inputs, keeps existing ones.</summary>
    public void EnsureParams(Dictionary<string, ParamState> ps)
    {
        foreach (var i in Inputs)
        {
            if (i.IsTexture) continue;
            if (!ps.TryGetValue(i.Name, out var s) || s.Value.Length != i.Default.Length)
                ps[i.Name] = new ParamState { Value = (float[])i.Default.Clone() };
        }
    }

    /// <summary>Full fragment source: <paramref name="header"/> (#version or precision) + ISF uniforms + expanded body.</summary>
    public string BuildFragment(string header) => Build(header, "", Source);

    /// <summary>Vertex source from the .vs body (it calls isf_vertShaderInit), or null when the shader has none.</summary>
    public string? BuildVertex(string header) => VertexSource == null ? null : Build(
        // GLSL ES: a uniform in both stages needs the same precision. The fragment side gets highp float when the GPU has it
        // (GL_FRAGMENT_PRECISION_HIGH only exists there) and mediump int by default, so say both before any uniform.
        // ponytail: a GPU without highp in fragments would mismatch; none of the TVs we target lack it
        header.Contains("precision") ? "precision highp float;\nprecision mediump int;\n" : header, """
        attribute vec2 isf_Position;
        void isf_vertShaderInit() { isf_FragNormCoord = isf_Position * 0.5 + 0.5; gl_Position = vec4(isf_Position, 0.0, 1.0); }
        #define vv_vertShaderInit isf_vertShaderInit

        """, VertexSource);

    string Build(string header, string extra, string body)
    {
        var sb = new StringBuilder(header);
        sb.Append("""
            uniform float TIME;
            uniform float TIMEDELTA;
            uniform vec2 RENDERSIZE;
            uniform int FRAMEINDEX;
            uniform int PASSINDEX;
            uniform vec4 DATE;
            varying vec2 isf_FragNormCoord;
            #define vv_FragNormCoord isf_FragNormCoord

            """);
        foreach (var i in Inputs)
        {
            sb.Append(i.Type switch
            {
                IsfType.Float => $"uniform float {i.Name};\n",
                IsfType.Bool or IsfType.Event => $"uniform bool {i.Name};\n",
                IsfType.Long => $"uniform int {i.Name};\n",
                IsfType.Point2D => $"uniform vec2 {i.Name};\n",
                IsfType.Color => $"uniform vec4 {i.Name};\n",
                _ => Image(i.Name),
            });
        }
        foreach (var b in Buffers) sb.Append(Image(b));
        sb.Append(extra);
        sb.Append("#line 1\n");
        sb.Append(ExpandMacros(VersionLine.Replace(body, "")));
        // GLSL is ASCII only: accents in comments/description make drivers fail ("pre-mature EOF")
        for (int k = 0; k < sb.Length; k++) if (sb[k] > 127) sb[k] = ' ';
        return sb.ToString();
    }

    // pictures uploaded top row first set _flip; every IMG_* macro reads through _x_px
    static string Image(string n) =>
        $"uniform sampler2D {n};\nuniform vec2 _{n}_imgSize;\nuniform vec4 _{n}_imgRect;\nuniform bool _{n}_flip;\n"
        + $"vec4 _{n}_px(vec2 p) {{ return texture2D({n}, _{n}_flip ? vec2(p.x, 1.0 - p.y) : p); }}\n";

    public static string ExpandMacros(string src)
    {
        var sb = new StringBuilder();
        int pos = 0;
        while (Macro.Match(src, pos) is { Success: true } m)
        {
            sb.Append(src, pos, m.Index - pos);
            var args = new List<string>();
            int i = m.Index + m.Length, start = i, depth = 1;
            for (; i < src.Length && depth > 0; i++)
            {
                char c = src[i];
                if (c == '(') depth++;
                else if (c == ')' && --depth == 0) args.Add(src[start..i]);
                else if (c == ',' && depth == 1) { args.Add(src[start..i]); start = i + 1; }
            }
            var name = m.Groups[1].Value;
            if (depth != 0) throw new FormatException($"{name}: falta ')'");
            var a = args.Select(x => ExpandMacros(x).Trim()).ToList();
            bool twoArgs = name is "IMG_PIXEL" or "IMG_NORM_PIXEL";
            if (a.Count < (twoArgs ? 2 : 1)) throw new FormatException($"{name}: faltan argumentos");
            var img = a[0];
            sb.Append(name switch
            {
                "IMG_PIXEL" => $"_{img}_px(({a[1]}) / _{img}_imgSize)",
                "IMG_NORM_PIXEL" => $"_{img}_px({a[1]})",
                "IMG_SIZE" => $"_{img}_imgSize",
                _ => $"_{img}_px(isf_FragNormCoord)",
            });
            pos = i;
        }
        sb.Append(src, pos, src.Length - pos);
        return sb.ToString();
    }
}
