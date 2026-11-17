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
    public int PassCount = 1;
    public string Source = "";

    static readonly Regex Header = new(@"^\s*/\*(.*?)\*/", RegexOptions.Singleline);
    static readonly Regex VersionLine = new(@"^\s*#version.*$", RegexOptions.Multiline);
    static readonly Regex Macro = new(@"\b(IMG_PIXEL|IMG_NORM_PIXEL|IMG_THIS_PIXEL|IMG_THIS_NORM_PIXEL|IMG_SIZE)\s*\(");

    public static IsfShader Parse(string source)
    {
        var s = new IsfShader { Source = source };
        var m = Header.Match(source);
        if (!m.Success) return s; // plain GLSL without ISF header still renders
        using var doc = JsonDocument.Parse(m.Groups[1].Value,
            new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        if (root.TryGetProperty("DESCRIPTION", out var d)) s.Description = d.ToString();
        if (root.TryGetProperty("PASSES", out var p) && p.ValueKind == JsonValueKind.Array)
            s.PassCount = Math.Max(1, p.GetArrayLength());
        if (root.TryGetProperty("INPUTS", out var inputs) && inputs.ValueKind == JsonValueKind.Array)
            foreach (var e in inputs.EnumerateArray())
                if (ParseInput(e) is { } i) s.Inputs.Add(i);
        return s;
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
    public string BuildFragment(string header)
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
                _ => $"uniform sampler2D {i.Name};\nuniform vec2 _{i.Name}_imgSize;\nuniform vec4 _{i.Name}_imgRect;\nuniform bool _{i.Name}_flip;\n",
            });
        }
        sb.Append("#line 1\n");
        sb.Append(ExpandMacros(VersionLine.Replace(Source, "")));
        // GLSL is ASCII only: accents in comments/description make drivers fail ("pre-mature EOF")
        for (int k = 0; k < sb.Length; k++) if (sb[k] > 127) sb[k] = ' ';
        return sb.ToString();
    }

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
                "IMG_PIXEL" => $"texture2D({img}, ({a[1]}) / _{img}_imgSize)",
                "IMG_NORM_PIXEL" => $"texture2D({img}, {a[1]})",
                "IMG_SIZE" => $"_{img}_imgSize",
                _ => $"texture2D({img}, isf_FragNormCoord)",
            });
            pos = i;
        }
        sb.Append(src, pos, src.Length - pos);
        return sb.ToString();
    }
}
