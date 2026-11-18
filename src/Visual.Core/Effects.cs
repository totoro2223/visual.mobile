namespace Visual.Core;

public sealed record EffectDef(string Name, string Label, float Min, float Max, float Default, string[]? Labels = null);

/// <summary>Global post-processing applied to whatever the scene shaders draw.</summary>
public static class EffectDefs
{
    public const float SafeStrobeHz = 3f; // WCAG 2.3.1: no more than 3 flashes per second

    /// <summary>Also the auto strobe's modes. 3 uses the hue, 4 picks a new one each flash. Before All: statics initialize in order.</summary>
    public static readonly string[] StrobeModes = ["Blanco", "Negro", "Invertir", "Color", "Color al azar"];

    public static readonly EffectDef[] All =
    [
        new("brightness", "Brillo", -1, 1, 0),
        new("contrast", "Contraste", 0, 3, 1),
        new("saturation", "Saturación", 0, 3, 1),
        new("hue", "Hue", 0, 1, 0),
        new("zoom", "Zoom", 0.2f, 5, 1),
        new("rotation", "Rotación", 0, 1, 0),
        new("kaleido", "Caleidoscopio", 0, 12, 0),
        new("chroma", "Aberración cromática", 0, 0.05f, 0),
        new("vignette", "Viñeta", 0, 1, 0),
        new("feedback", "Feedback", 0, 0.98f, 0),
        new("tint", "Monocolor", 0, 1, 0),          // 1 = everything in one color, keeping light and dark
        new("tintHue", "Monocolor tono", 0, 1, 0),  // 0 red, 0.33 green, 0.66 blue
        new("opacity", "Opacidad", 0, 1, 1),        // 0 = invisible (black)
        new("strobeRate", "Strobe Hz", 0, 20, 0),
        new("strobeDuty", "Strobe duración", 0.05f, 0.95f, 0.3f),
        new("strobeIntensity", "Strobe intensidad", 0, 1, 1),
        new("strobeMode", "Strobe modo", 0, 4, 0, StrobeModes),
        new("strobeHue", "Strobe tono", 0, 1, 0),    // "Color" mode: 0 red, 0.33 green, 0.66 blue
        // mask: only a rectangle stays visible, the rest is black (also over the strobe)
        new("maskX", "Máscara X", 0, 1, 0.5f),       // center; 0 left, 1 right
        new("maskY", "Máscara Y", 0, 1, 0.5f),       // 0 bottom, 1 top
        new("maskW", "Máscara ancho", 0, 1, 1),      // 1 = whole screen
        new("maskH", "Máscara alto", 0, 1, 1),
        new("maskSoft", "Máscara borde", 0, 0.5f, 0), // 0 hard edge, higher = blurred
    ];

    static readonly Dictionary<string, EffectDef> byName = All.ToDictionary(d => d.Name);
    public static EffectDef Get(string name) => byName[name];

    /// <summary>Changed from its default or moving with a modifier.</summary>
    public static bool IsActive(EffectDef d, ParamState ps) =>
        !ps.Off && (ps.Mod.Kind != ModKind.Constant || MathF.Abs(ps.Mod.Apply(ps.Value[0], d.Min, d.Max, 0, NoSound) - d.Default) > 1e-4f);

    static readonly AudioBands NoSound = new();

    /// <summary>Effects shown and moved together (strobe, mask, monocolor): one in use pulls in its siblings.</summary>
    public static string Group(string name) =>
        name.StartsWith("strobe") ? "strobe" : name.StartsWith("mask") ? "mask" : name.StartsWith("tint") ? "tint" : name;

    /// <summary>Short names of the effects a scene is using; the strobe counts once, by its rate.</summary>
    public static string[] ActiveLabels(Dictionary<string, ParamState> fx) => All
        .Where(d => d.Name is not ("strobeDuty" or "strobeIntensity" or "strobeMode" or "strobeHue" or "tintHue" or "maskX" or "maskY" or "maskSoft")
                    && fx.TryGetValue(d.Name, out var ps) && IsActive(d, ps))
        .Select(d => d.Name == "strobeRate" ? "Strobe" : d.Name.StartsWith("mask") ? "Máscara" : d.Label)
        .Distinct()
        .ToArray();

    /// <summary>Post shader; works as GLSL 1.20 and GLSL ES 1.00 after the header.</summary>
    public static string PostFragment(string header) => header + """
        uniform sampler2D sceneA;
        uniform sampler2D sceneB;
        uniform sampler2D prevOut;
        uniform float mixB;
        uniform vec2 RENDERSIZE;
        uniform float brightness, contrast, saturation, hue, zoom, rotation, kaleido, chroma, vignette, feedback, tint, tintHue, opacity, strobe, strobeMode, strobeHue, maskX, maskY, maskW, maskH, maskSoft;
        varying vec2 isf_FragNormCoord;
        const float TAU = 6.2831853;

        vec3 scene(vec2 uv) { return mix(texture2D(sceneA, uv).rgb, texture2D(sceneB, uv).rgb, mixB); }

        vec2 geometry(vec2 uv) {
            vec2 asp = vec2(RENDERSIZE.x / RENDERSIZE.y, 1.0);
            vec2 p = (uv - 0.5) * asp / zoom;
            float a = rotation * TAU;
            p = mat2(cos(a), sin(a), -sin(a), cos(a)) * p;
            if (kaleido >= 1.0) {
                float seg = TAU / floor(kaleido);
                float ang = abs(mod(atan(p.y, p.x), seg) - seg * 0.5);
                p = length(p) * vec2(cos(ang), sin(ang));
            }
            return p / asp + 0.5;
        }

        vec3 hueShift(vec3 c, float h) {
            const mat3 toYIQ = mat3(0.299, 0.596, 0.211, 0.587, -0.274, -0.523, 0.114, -0.322, 0.312);
            const mat3 toRGB = mat3(1.0, 1.0, 1.0, 0.956, -0.272, -1.106, 0.621, -0.647, 1.703);
            vec3 y = toYIQ * c;
            float a = h * TAU;
            y.yz = vec2(y.y * cos(a) - y.z * sin(a), y.y * sin(a) + y.z * cos(a));
            return toRGB * y;
        }

        vec3 pure(float h) { return clamp(abs(mod(h * 6.0 + vec3(0.0, 4.0, 2.0), 6.0) - 3.0) - 1.0, 0.0, 1.0); } // full color of that hue

        void main() {
            vec2 uv = geometry(isf_FragNormCoord);
            vec2 off = (uv - 0.5) * chroma;
            vec3 c = vec3(scene(uv + off).r, scene(uv).g, scene(uv - off).b);
            c = (c + brightness - 0.5) * contrast + 0.5;
            c = mix(vec3(dot(c, vec3(0.299, 0.587, 0.114))), c, saturation);
            c = hueShift(c, hue);
            c = max(c, texture2D(prevOut, (isf_FragNormCoord - 0.5) * 0.99 + 0.5).rgb * feedback);
            c *= 1.0 - vignette * smoothstep(0.2, 0.8, length(isf_FragNormCoord - 0.5));
            c = mix(c, dot(c, vec3(0.299, 0.587, 0.114)) * pure(tintHue), tint);
            c *= opacity;
            vec3 s = strobeMode < 0.5 ? vec3(1.0) : (strobeMode < 1.5 ? vec3(0.0) : (strobeMode < 2.5 ? 1.0 - c : pure(strobeHue)));
            // mask: d < 0 inside the rectangle; the soft edge fades inward over maskSoft
            vec2 d = abs(isf_FragNormCoord - vec2(maskX, maskY)) - 0.5 * vec2(maskW, maskH);
            vec2 m = clamp(-d / max(maskSoft, 0.0001), 0.0, 1.0);
            m = m * m * (3.0 - 2.0 * m);
            gl_FragColor = vec4(clamp(mix(c, s, strobe), 0.0, 1.0) * m.x * m.y, 1.0);
        }
        """;
}
