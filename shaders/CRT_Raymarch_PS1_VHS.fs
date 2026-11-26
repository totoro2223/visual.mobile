/*{
  "ISFVSN": "2",
  "TYPE": "GLSL",
  "NAME": "CRT_Raymarch_PS1_VHS",
  "DESCRIPTION": "Raymarched neon scene with CRT curvature, PS1-style quantization, and VHS degradation.",
  "CATEGORIES": ["Generator", "Stylize", "Retro"],
  "INPUTS": [
    { "NAME": "retroEnabled", "TYPE": "bool", "DEFAULT": true },
    { "NAME": "sceneSpeed",      "TYPE": "float", "DEFAULT": 1.0, "MIN": 0.0, "MAX": 3.0 },
    { "NAME": "cameraOrbit",     "TYPE": "float", "DEFAULT": 0.35, "MIN": 0.0, "MAX": 2.0 },
    { "NAME": "cameraElevation", "TYPE": "float", "DEFAULT": 0.28, "MIN": -1.0, "MAX": 1.0 },

    { "NAME": "pixelSize",       "TYPE": "float", "DEFAULT": 3.0, "MIN": 1.0, "MAX": 8.0 },
    { "NAME": "warpAmount",      "TYPE": "float", "DEFAULT": 0.010, "MIN": 0.0, "MAX": 0.05 },
    { "NAME": "tearAmount",      "TYPE": "float", "DEFAULT": 0.020, "MIN": 0.0, "MAX": 0.08 },
    { "NAME": "chromaSplit",     "TYPE": "float", "DEFAULT": 0.010, "MIN": 0.0, "MAX": 0.05 },

    { "NAME": "scanlineStrength","TYPE": "float", "DEFAULT": 0.15, "MIN": 0.0, "MAX": 0.5 },
    { "NAME": "noiseAmount",     "TYPE": "float", "DEFAULT": 0.08, "MIN": 0.0, "MAX": 0.25 },
    { "NAME": "paletteLevels",   "TYPE": "float", "DEFAULT": 6.0,  "MIN": 2.0, "MAX": 16.0 },

    { "NAME": "vignetteAmount",  "TYPE": "float", "DEFAULT": 0.12, "MIN": 0.0, "MAX": 0.5 },
    { "NAME": "maskStrength",    "TYPE": "float", "DEFAULT": 0.10, "MIN": 0.0, "MAX": 0.5 },
    { "NAME": "dropoutAmount",    "TYPE": "float", "DEFAULT": 0.35, "MIN": 0.0, "MAX": 1.0 }
  ]
}*/

#define sat(x) clamp(x, 0.0, 1.0)

float hash12(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

mat2 rot(float a) {
    float s = sin(a), c = cos(a);
    return mat2(c, -s, s, c);
}

float sdSphere(vec3 p, float r) {
    return length(p) - r;
}

float sdBox(vec3 p, vec3 b) {
    vec3 q = abs(p) - b;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

float sdTorus(vec3 p, vec2 t) {
    vec2 q = vec2(length(p.xz) - t.x, p.y);
    return length(q) - t.y;
}

vec2 mapScene(vec3 p, float t) {
    float d = p.y + 1.1;
    float id = 1.0;

    vec3 q = p;
    q.xz *= rot(t * 0.35);
    q.yz *= rot(sin(t * 0.6) * 0.15);
    float tor = sdTorus(q - vec3(0.0, 0.05, 0.0), vec2(1.15, 0.23));
    if (tor < d) { d = tor; id = 2.0; }

    vec3 s = p - vec3(0.0, 0.02, 0.0);
    float sph = sdSphere(s, 0.58);
    if (sph < d) { d = sph; id = 3.0; }

    vec3 b = p - vec3(1.55, -0.18, 0.25);
    b.xz *= rot(t * 0.9);
    b.xy *= rot(t * 0.45);
    float bx = sdBox(b, vec3(0.26));
    if (bx < d) { d = bx; id = 4.0; }

    vec3 o = p - vec3(1.55 * cos(t * 0.8), -0.02 + 0.35 * sin(t * 1.3), 1.55 * sin(t * 0.8));
    float orb = sdSphere(o, 0.16);
    if (orb < d) { d = orb; id = 5.0; }

    return vec2(d, id);
}

vec3 calcNormal(vec3 p, float t) {
    vec2 e = vec2(0.0015, 0.0);
    return normalize(vec3(
        mapScene(p + e.xyy, t).x - mapScene(p - e.xyy, t).x,
        mapScene(p + e.yxy, t).x - mapScene(p - e.yxy, t).x,
        mapScene(p + e.yyx, t).x - mapScene(p - e.yyx, t).x
    ));
}

float softShadow(vec3 ro, vec3 rd, float mint, float maxt, float k, float t) {
    float res = 1.0;
    float h = mint;
    for (int i = 0; i < 20; i++) {
        float d = mapScene(ro + rd * h, t).x;
        res = min(res, k * d / h);
        h += clamp(d, 0.03, 0.15);
        if (res < 0.001 || h > maxt) break;
    }
    return sat(res);
}

float ao(vec3 p, vec3 n, float t) {
    float occ = 0.0;
    float sca = 1.0;
    for (int i = 0; i < 5; i++) {
        float h = 0.04 + 0.09 * float(i);
        float d = mapScene(p + n * h, t).x;
        occ += (h - d) * sca;
        sca *= 0.75;
    }
    return sat(1.0 - 1.4 * occ);
}

vec2 march(vec3 ro, vec3 rd, float t) {
    float d = 0.0;
    float id = -1.0;
    for (int i = 0; i < 72; i++) {
        vec2 h = mapScene(ro + rd * d, t);
        if (h.x < 0.001 || d > 40.0) { id = h.y; break; }
        d += h.x;
    }
    return vec2(d, id);
}

mat3 camera(vec3 ro, vec3 ta) {
    vec3 cw = normalize(ta - ro);
    vec3 cp = vec3(0.0, 1.0, 0.0);
    vec3 cu = normalize(cross(cw, cp));
    vec3 cv = cross(cu, cw);
    return mat3(cu, cv, cw);
}

vec3 renderScene(vec2 uv, float t) {
    float ang = t * cameraOrbit;
    float elv = cameraElevation;

    vec3 ro = vec3(
        4.2 * cos(ang) * cos(elv),
        1.15 + 1.35 * sin(elv),
        4.2 * sin(ang) * cos(elv)
    );
    vec3 ta = vec3(0.0, -0.05, 0.0);
    mat3 cam = camera(ro, ta);

    vec3 rd = normalize(cam * vec3(uv, 1.55));

    vec3 skyTop = vec3(0.02, 0.03, 0.06);
    vec3 skyBot = vec3(0.005, 0.006, 0.01);
    vec3 sky = mix(skyBot, skyTop, sat(0.5 + 0.5 * rd.y));

    vec2 hm = march(ro, rd, t);
    float dist = hm.x;
    float mid = hm.y;

    if (dist > 40.0) return sky;

    vec3 p = ro + rd * dist;
    vec3 n = calcNormal(p, t);

    vec3 lightDir = normalize(vec3(0.7, 0.95, 0.35));
    float diff = sat(dot(n, lightDir));
    float sh = softShadow(p + n * 0.01, lightDir, 0.05, 8.0, 18.0, t);
    float amb = 0.35 + 0.65 * sat(n.y * 0.5 + 0.5);
    float spec = pow(sat(dot(reflect(-lightDir, n), -rd)), 48.0) * sh;
    float rim = pow(1.0 - sat(dot(n, -rd)), 2.5);

    vec3 base = vec3(0.0);
    float emis = 0.0;
    float floorGlow = 0.0;

    if (mid < 1.5) {
        vec2 g = abs(fract(p.xz * 1.9) - 0.5);
        float grid = 1.0 - smoothstep(0.0, 0.06, min(g.x, g.y));
        floorGlow = grid;
        base = mix(vec3(0.02, 0.03, 0.04), vec3(0.08, 0.22, 0.18), grid);
        base += grid * vec3(0.06, 0.03, 0.08);
    } else if (mid < 2.5) {
        float a = atan(p.z, p.x);
        float band = 0.5 + 0.5 * sin(14.0 * a - 6.0 * p.y + t * 4.5);
        base = mix(vec3(0.2, 0.85, 1.2), vec3(1.2, 0.2, 0.95), band);
        emis = 0.55 + 0.45 * band;
    } else if (mid < 3.5) {
        float pulse = 0.5 + 0.5 * sin(t * 3.0 + p.y * 6.0);
        base = mix(vec3(1.5, 0.35, 0.08), vec3(1.0, 0.9, 0.2), pulse);
        emis = 0.45 + 0.55 * pulse;
    } else if (mid < 4.5) {
        base = vec3(0.18, 0.3, 1.15);
        emis = 0.12;
    } else {
        base = vec3(2.5, 1.9, 0.55);
        emis = 2.5;
    }

    float aoVal = ao(p, n, t);

    vec3 col = base * (0.12 + 1.2 * diff * sh) * amb * aoVal;
    col += base * 0.08;
    col += spec * vec3(1.0, 0.95, 0.9);
    col += rim * base * 0.45;
    col += base * emis;

    if (mid < 1.5) {
        col += 0.07 * vec3(0.12, 0.8, 1.0) * floorGlow;
    }

    float fog = 1.0 - exp(-0.03 * dist * dist);
    col = mix(col, sky, fog);

    float vign = 1.0 - 0.12 * dot(uv, uv);
    col *= vign;

    return col;
}

void main() {
    vec2 fragCoord = gl_FragCoord.xy;
    vec2 res = RENDERSIZE.xy;

    float t = TIME * sceneSpeed;

    vec2 uv = (fragCoord - 0.5 * res) / res.y;

    if (retroEnabled) {
        // PS1-ish pixel snapping
        float px = max(1.0, pixelSize);
        vec2 snapped = floor(fragCoord / px) * px;
        uv = (snapped - 0.5 * res) / res.y;

        // CRT-style curvature plus slight wobble
        float r2 = dot(uv, uv);
        uv *= 1.0 + 0.12 * r2;
        uv.x += sin(uv.y * 8.0 + t * 2.5) * warpAmount;
        uv.y += sin(uv.x * 6.0 + t * 1.7) * warpAmount;

        // VHS tracking tear
        float band = step(0.8, fract(uv.y * 3.0 + t * 0.2));
        uv.x += band * (sin(t * 20.0) * tearAmount);

        // Scanline jitter
        uv.x += (hash12(vec2(floor(uv.y * 200.0), floor(t * 60.0))) - 0.5) * (tearAmount * 0.5);
    }

    vec3 col = renderScene(uv, t);

    if (retroEnabled) {
        // Scene render with chroma split
        vec3 c;
        c.r = renderScene(uv + vec2(chromaSplit, 0.0), t).r;
        c.g = col.g;
        c.b = renderScene(uv - vec2(chromaSplit, 0.0), t).b;
        col = c;

        // Vertical bleed
        vec3 bleed = renderScene(uv + vec2(0.0, 0.002), t);
        col = mix(col, bleed, 0.25);

        // PS1 color quantization
        float levels = max(2.0, paletteLevels);
        col = floor(col * levels) / levels;

        // VHS noise
        float noise = hash12(fragCoord + vec2(t * 19.0, t * 17.0));
        col += (noise - 0.5) * noiseAmount;

        // Scanlines
        float scan = sin(fragCoord.y * 1.5) * scanlineStrength;
        col -= scan;

        // Dropout lines
        float dropout = step(1.0 - dropoutAmount * 0.01, hash12(vec2(floor(t * 10.0), fragCoord.y)));
        col *= 1.0 - dropout * dropoutAmount;

        // RGB mask
        vec3 mask = 0.9 + maskStrength * sin((fragCoord.x + vec3(0.0, 1.0, 2.0)) * 2.0943951);
        col *= mask;

        // Dark crush
        col = pow(max(col, 0.0), vec3(1.6));

        // Vignette
        float vig = smoothstep(1.2, 0.2, length((fragCoord - 0.5 * res) / res.y));
        col *= mix(1.0, vig, vignetteAmount);
    }

    // Tonemap
    col = col / (1.0 + col);
    col = pow(col, vec3(0.4545));

    gl_FragColor = vec4(col, 1.0);
}