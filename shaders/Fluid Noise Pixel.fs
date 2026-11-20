/*{
    "DESCRIPTION": "Dynamic Pixel Grid with Procedural Noise Animation",
    "CREDIT": "By fluidstorm.com (ISF)",
    "NAME": "Fluid Noise Pixel",
    "FILE":"isf-fluid-noise-pixel.fs",
    "VERSION":"v1.0.0-20251008",
    "ISFVSN": "2",
    "CATEGORIES": [
        "Generator",
        "Fluid",
        "Pixel",
        "Noise"
    ],
    "INPUTS": [
        {
            "NAME": "zoom",
            "TYPE": "float",
            "MIN": 0.01,
            "MAX": 0.5,
            "DEFAULT": 0.42
        },
        {
            "NAME": "horizontalScale",
            "TYPE": "float",
            "MIN": 0.1,
            "MAX": 10.0,
            "DEFAULT": 1.8
        },
        {
            "NAME": "verticalScale",
            "TYPE": "float",
            "MIN": 0.1,
            "MAX": 10.0,
            "DEFAULT": 1.4
        },
        {
            "NAME": "gridSize",
            "TYPE": "float",
            "MIN": 1.0,
            "MAX": 20.0,
            "DEFAULT": 12.9
        },
        {
            "NAME": "spacing",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 5.0,
            "DEFAULT": 1
        },
        {
            "NAME": "speed",
            "TYPE": "float",
            "MIN": 0.01,
            "MAX": 0.5,
            "DEFAULT": 0.1
        },
        {
            "NAME": "maxOpacity",
            "TYPE": "float",
            "MIN": 0.1,
            "MAX": 1.0,
            "DEFAULT": 1.0
        },
        {
            "NAME": "octaves",
            "TYPE": "float",
            "MIN": 1.0,
            "MAX": 8.0,
            "DEFAULT": 1.0
        },
        {
            "NAME": "persistence",
            "TYPE": "float",
            "MIN": 0.1,
            "MAX": 1.0,
            "DEFAULT": 0.6
        },
        {
            "NAME": "lacunarity",
            "TYPE": "float",
            "MIN": 1.0,
            "MAX": 4.0,
            "DEFAULT": 2.2
        },
        {
            "NAME": "colorRamp0",
            "TYPE": "color",
            "DEFAULT": [0.0, 0.0, 0.0, 1.0]
        },
        {
            "NAME": "colorRamp0Pos",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.0
        },
        {
            "NAME": "colorRamp0Alpha",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 1.0
        },
        {
            "NAME": "colorRamp1",
            "TYPE": "color",
            "DEFAULT": [0.0, 1.0, 0.6667, 1.0]
        },
        {
            "NAME": "colorRamp1Pos",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.5
        },
        {
            "NAME": "colorRamp1Alpha",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 1.0
        },
        {
            "NAME": "colorRamp2",
            "TYPE": "color",
            "DEFAULT": [0.8588, 0.8588, 0.8588, 1.0]
        },
        {
            "NAME": "colorRamp2Pos",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 1.0
        },
        {
            "NAME": "colorRamp2Alpha",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 1.0
        },
        {
            "NAME": "noiseStyle",
            "TYPE": "long",
            "VALUES": [0, 1, 2, 3],
            "LABELS": ["Basic", "Clouds", "Turbulent", "Marble"],
            "DEFAULT": 3
        },
        {
            "NAME": "backgroundColor",
            "TYPE": "color",
            "DEFAULT": [0.0, 0.0, 0.0, 1.0]
        },
        {
            "NAME": "useBackgroundColor",
            "TYPE": "bool",
            "DEFAULT": true
        }
    ]
}*/

/* Simplex Noise implementation */
vec3 mod289(vec3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec4 mod289(vec4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec4 permute(vec4 x) { return mod289(((x * 34.0) + 1.0) * x); }
vec4 taylorInvSqrt(vec4 r) { return 1.79284291400159 - 0.85373472095314 * r; }

float snoise(vec3 v) {
    const vec2 C = vec2(1.0 / 6.0, 1.0 / 3.0);
    const vec4 D = vec4(0.0, 0.5, 1.0, 2.0);

    vec3 i = floor(v + dot(v, C.yyy));
    vec3 x0 = v - i + dot(i, C.xxx);

    vec3 g = step(x0.yzx, x0.xyz);
    vec3 l = 1.0 - g;
    vec3 i1 = min(g.xyz, l.zxy);
    vec3 i2 = max(g.xyz, l.zxy);

    vec3 x1 = x0 - i1 + C.xxx;
    vec3 x2 = x0 - i2 + C.yyy;
    vec3 x3 = x0 - D.yyy;

    i = mod289(i);
    vec4 p = permute(permute(permute(
                i.z + vec4(0.0, i1.z, i2.z, 1.0))
                + i.y + vec4(0.0, i1.y, i2.y, 1.0))
                + i.x + vec4(0.0, i1.x, i2.x, 1.0));

    float n_ = 0.142857142857;
    vec3 ns = n_ * D.wyz - D.xzx;

    vec4 j = p - 49.0 * floor(p * ns.z * ns.z);

    vec4 x_ = floor(j * ns.z);
    vec4 y_ = floor(j - 7.0 * x_);

    vec4 x = x_ * ns.x + ns.yyyy;
    vec4 y = y_ * ns.x + ns.yyyy;
    vec4 h = 1.0 - abs(x) - abs(y);

    vec4 b0 = vec4(x.xy, y.xy);
    vec4 b1 = vec4(x.zw, y.zw);

    vec4 s0 = floor(b0) * 2.0 + 1.0;
    vec4 s1 = floor(b1) * 2.0 + 1.0;
    vec4 sh = -step(h, vec4(0.0));

    vec4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
    vec4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

    vec3 p0 = vec3(a0.xy, h.x);
    vec3 p1 = vec3(a0.zw, h.y);
    vec3 p2 = vec3(a1.xy, h.z);
    vec3 p3 = vec3(a1.zw, h.w);

    vec4 norm = taylorInvSqrt(vec4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
    p0 *= norm.x;
    p1 *= norm.y;
    p2 *= norm.z;
    p3 *= norm.w;

    vec4 m = max(0.6 - vec4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
    m = m * m;
    return 42.0 * dot(m * m, vec4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
}

// fBm
float fbm(vec3 p) {
    float total = 0.0;
    float amplitude = 1.0;
    float frequency = 1.0;
    float maxValue = 0.0;

    for (int i = 0; i < 8; ++i) {
        if (float(i) >= octaves) break;
        total += snoise(p * frequency) * amplitude;
        maxValue += amplitude;
        amplitude *= persistence;
        frequency *= lacunarity;
    }
    return total / maxValue;
}

// Color ramp with custom alpha
vec4 getColorFromRamp(float t) {
    t = clamp(t, 0.0, 1.0);
    vec4 color0 = vec4(colorRamp0.rgb, colorRamp0Alpha);
    vec4 color1 = vec4(colorRamp1.rgb, colorRamp1Alpha);
    vec4 color2 = vec4(colorRamp2.rgb, colorRamp2Alpha);

    if (t <= colorRamp0Pos) return color0;
    if (t >= colorRamp2Pos) return color2;
    if (t < colorRamp1Pos) {
        float mixT = (t - colorRamp0Pos) / (colorRamp1Pos - colorRamp0Pos);
        return mix(color0, color1, mixT);
    } else {
        float mixT = (t - colorRamp1Pos) / (colorRamp2Pos - colorRamp1Pos);
        return mix(color1, color2, mixT);
    }
}

void main() {
    vec2 pix = gl_FragCoord.xy;
    float gs = max(gridSize, 1.0);
    vec2 cell = floor(pix / gs);
    vec2 within = mod(pix, gs);
    float gap = spacing;

    // Set background as the base layer
    if (useBackgroundColor) {
        gl_FragColor = backgroundColor;
    } else {
        gl_FragColor = vec4(0.0, 0.0, 0.0, 0.0);
    }

    // Only apply noise to pixels within the grid cells (excluding spacing)
    if (within.x >= gap && within.x <= gs - gap && within.y >= gap && within.y <= gs - gap) {
        vec2 cellUV = (cell * gs + gs * 0.5) / RENDERSIZE.xy;
        vec3 p = vec3(cellUV.x / zoom * horizontalScale, cellUV.y / zoom * verticalScale, TIME * speed);
        float noiseValue = fbm(p);
        if (noiseStyle == 3) {
            noiseValue = sin(noiseValue * 10.0 + TIME * 0.05 * speed);
        } else if (noiseStyle == 2) {
            noiseValue = abs(noiseValue);
        } else if (noiseStyle == 1) {
            noiseValue = smoothstep(-0.5, 0.5, noiseValue);
        }
        // noiseStyle == 0 uses raw fbm
        float intensity = (noiseValue + 1.0) * 0.5;
        vec4 color = getColorFromRamp(intensity);
        color.a *= maxOpacity; // Apply maxOpacity as an overall multiplier
        gl_FragColor = color;
    }
}