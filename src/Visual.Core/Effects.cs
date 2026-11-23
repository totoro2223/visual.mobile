namespace Visual.Core;

public sealed record EffectDef(string Name, string Label, float Min, float Max, float Default, string[]? Labels = null);

/// <summary>Mixes a layer (premultiplied alpha) over what is below it; mode = (int)BlendMode. Formulas as in CSS / Photoshop.</summary>
public static class Blends
{
    public static string Fragment(string header) => header + """
        uniform sampler2D below;
        uniform sampler2D over;
        uniform int mode;
        varying vec2 isf_FragNormCoord;

        float lum(vec3 c) { return dot(c, vec3(0.3, 0.59, 0.11)); }
        vec3 clipColor(vec3 c) {
            float l = lum(c), n = min(min(c.r, c.g), c.b), x = max(max(c.r, c.g), c.b);
            if (n < 0.0) c = l + (c - l) * l / max(l - n, 1e-5);
            if (x > 1.0) c = l + (c - l) * (1.0 - l) / max(x - l, 1e-5);
            return c;
        }
        vec3 setLum(vec3 c, float l) { return clipColor(c + (l - lum(c))); }
        float sat(vec3 c) { return max(max(c.r, c.g), c.b) - min(min(c.r, c.g), c.b); }
        vec3 setSat(vec3 c, float s) {
            float n = min(min(c.r, c.g), c.b), x = max(max(c.r, c.g), c.b);
            return x > n ? (c - n) * s / (x - n) : vec3(0.0);
        }
        vec3 softLight(vec3 b, vec3 s) {
            vec3 d = mix(sqrt(b), ((16.0 * b - 12.0) * b + 4.0) * b, step(b, vec3(0.25)));
            return mix(b + (2.0 * s - 1.0) * (d - b), b - (1.0 - 2.0 * s) * b * (1.0 - b), step(s, vec3(0.5)));
        }
        vec3 hardLight(vec3 b, vec3 s) { return mix(1.0 - 2.0 * (1.0 - b) * (1.0 - s), 2.0 * b * s, step(s, vec3(0.5))); }

        vec3 blend(vec3 b, vec3 s) {
            if (mode == 1) return min(b + s, 1.0);
            if (mode == 2) return max(b - s, 0.0);
            if (mode == 3) return b * s;
            if (mode == 4) return b + s - b * s;
            if (mode == 5) return hardLight(s, b); // overlay = hard light with the two swapped
            if (mode == 6) return softLight(b, s);
            if (mode == 7) return hardLight(b, s);
            if (mode == 8) return abs(b - s);
            if (mode == 9) return b + s - 2.0 * b * s;
            if (mode == 10) return max(b, s);
            if (mode == 11) return min(b, s);
            if (mode == 12) return min(b / max(1.0 - s, 1e-5), 1.0);
            if (mode == 13) return 1.0 - min((1.0 - b) / max(s, 1e-5), 1.0);
            if (mode == 14) return setLum(setSat(s, sat(b)), lum(b));
            if (mode == 15) return setLum(s, lum(b));
            if (mode == 16) return setLum(b, lum(s));
            return s;
        }

        void main() {
            vec3 b = texture2D(below, isf_FragNormCoord).rgb;
            vec4 o = texture2D(over, isf_FragNormCoord);
            vec3 s = o.a > 0.0 ? clamp(o.rgb / o.a, 0.0, 1.0) : vec3(0.0);
            gl_FragColor = vec4(mix(b, clamp(blend(b, s), 0.0, 1.0), o.a), 1.0);
        }
        """;
}

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
        // tilt: the picture as a plane seen in perspective; outside the plane is black
        new("tiltX", "Inclinación X", -80, 80, 0),   // degrees; + lays the top away
        new("tiltY", "Inclinación Y", -80, 80, 0),   // + lays the right side away
        new("tiltPersp", "Perspectiva", 0.2f, 3, 1), // higher = the far side shrinks more
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
        new("maskRepX", "Máscara repetir horizontal", 1, 16, 1), // the screen is cut into this many columns, each with its own mask
        new("maskRepY", "Máscara repetir vertical", 1, 16, 1),
        new("maskSepX", "Máscara separación horizontal", 0, 1, 1), // 1 = spread over the screen, 0 = copies touching in the middle
        new("maskSepY", "Máscara separación vertical", 0, 1, 1),
    ];

    static readonly Dictionary<string, EffectDef> byName = All.ToDictionary(d => d.Name);
    public static EffectDef Get(string name) => byName[name];

    /// <summary>Changed from its default or moving with a modifier.</summary>
    public static bool IsActive(EffectDef d, ParamState ps) =>
        !ps.Off && (ps.Mod.Kind != ModKind.Constant || MathF.Abs(ps.Mod.Apply(ps.Value[0], d.Min, d.Max, 0, NoSound) - d.Default) > 1e-4f);

    static readonly AudioBands NoSound = new();

    /// <summary>Effects shown and moved together (strobe, mask, monocolor): one in use pulls in its siblings.</summary>
    public static string Group(string name) =>
        name.StartsWith("strobe") ? "strobe" : name.StartsWith("mask") ? "mask" : name.StartsWith("tint") ? "tint" : name.StartsWith("tilt") ? "tilt" : name;

    /// <summary>Short names of the effects a scene is using; the strobe counts once, by its rate.</summary>
    public static string[] ActiveLabels(Dictionary<string, ParamState> fx) => All
        .Where(d => d.Name is not ("strobeDuty" or "strobeIntensity" or "strobeMode" or "strobeHue" or "tintHue" or "maskX" or "maskY" or "maskSoft" or "maskRepX" or "maskRepY" or "maskSepX" or "maskSepY" or "tiltY" or "tiltPersp")
                    && fx.TryGetValue(d.Name, out var ps) && IsActive(d, ps))
        .Select(d => d.Name == "strobeRate" ? "Strobe" : d.Name.StartsWith("mask") ? "Máscara" : d.Name.StartsWith("tilt") ? "Inclinación" : d.Label)
        .Distinct()
        .ToArray();

    /// <summary>Post shader; works as GLSL 1.20 and GLSL ES 1.00 after the header.</summary>
    public static string PostFragment(string header) => header + """
        uniform sampler2D sceneA;
        uniform sampler2D sceneB;
        uniform sampler2D prevOut;
        uniform float mixB;
        uniform float layer; // 1 = a layer: opacity, mask and its shader's alpha go to the alpha (premultiplied), for the blend
        uniform vec2 RENDERSIZE;
        uniform float brightness, contrast, saturation, hue, zoom, rotation, kaleido, chroma, vignette, feedback, tint, tintHue, opacity, strobe, strobeMode, strobeHue, maskX, maskY, maskW, maskH, maskSoft, maskRepX, maskRepY, maskSepX, maskSepY, tiltX, tiltY, tiltPersp;
        varying vec2 isf_FragNormCoord;
        const float TAU = 6.2831853;

        vec3 scene(vec2 uv) { return mix(texture2D(sceneA, uv).rgb, texture2D(sceneB, uv).rgb, mixB); }

        float onPlane = 1.0;

        // the screen point seen on the tilted plane: ray from the eye (at focal distance) to the plane through the center
        vec2 tilt(vec2 p, vec2 asp) {
            if (tiltX == 0.0 && tiltY == 0.0) return p;
            float ax = radians(tiltX), ay = radians(tiltY), sx = sin(ax), cx = cos(ax), sy = sin(ay), cy = cos(ay);
            vec3 u = vec3(cy, 0.0, -sy), v = vec3(sy * sx, cx, cy * sx), n = vec3(sy * cx, -sx, cy * cx);
            float f = 2.0 / max(tiltPersp, 0.01);
            vec3 o = vec3(0.0, 0.0, -f), d = vec3(p, f);
            float dn = dot(d, n);
            if (dn <= 1e-5) { onPlane = 0.0; return p; } // past the horizon
            vec3 h = o + d * (f * n.z / dn);
            vec2 q = vec2(dot(h, u), dot(h, v));
            if (any(greaterThan(abs(q), asp * 0.5))) onPlane = 0.0;
            return q;
        }

        vec2 geometry(vec2 uv) {
            vec2 asp = vec2(RENDERSIZE.x / RENDERSIZE.y, 1.0);
            vec2 p = tilt((uv - 0.5) * asp, asp) / zoom;
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
            if (layer < 0.5) c *= opacity;
            vec3 s = strobeMode < 0.5 ? vec3(1.0) : (strobeMode < 1.5 ? vec3(0.0) : (strobeMode < 2.5 ? 1.0 - c : pure(strobeHue)));
            // mask: d < 0 inside the rectangle; the soft edge fades inward over maskSoft
            // repeat: n copies per axis, pitch p from touching (sep 0, grouped around maskX/Y) to spread over the screen (sep 1)
            vec2 n = floor(vec2(maskRepX, maskRepY) + 0.5), sep = vec2(maskSepX, maskSepY), mc = vec2(maskX, maskY), mw = vec2(maskW, maskH) / n;
            vec2 pitch = max(mix(mw, 1.0 / n, sep), 0.0001);
            vec2 u = (isf_FragNormCoord - mix(mc, (mc + (n - 1.0) * 0.5) / n, sep)) / pitch + (n - 1.0) * 0.5;
            vec2 d = abs((u - clamp(floor(u + 0.5), 0.0, n - 1.0)) * pitch) - 0.5 * mw;
            vec2 m = clamp(-d / max(maskSoft, 0.0001), 0.0, 1.0);
            m = m * m * (3.0 - 2.0 * m);
            c = clamp(mix(c, s, strobe), 0.0, 1.0) * onPlane;
            float a = layer < 0.5 ? 1.0 : clamp(texture2D(sceneA, uv).a, 0.0, 1.0) * opacity * m.x * m.y * onPlane;
            gl_FragColor = layer < 0.5 ? vec4(c * m.x * m.y, 1.0) : vec4(c * a, a);
        }
        """;
}
