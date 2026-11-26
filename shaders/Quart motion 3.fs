/*{
  "DESCRIPTION": "Minimal drifting rectangular line pattern - alternate version",
  "CREDIT": "ChatGPT",
  "ISFVSN": "2.0",
  "CATEGORIES": [
    "Generator",
    "Pattern",
    "Monochrome"
  ],
  "INPUTS": [
    {
      "NAME": "lineDensity",
      "TYPE": "float",
      "DEFAULT": 110.0,
      "MIN": 20.0,
      "MAX": 260.0,
      "LABEL": "Line Density"
    },
    {
      "NAME": "lineThickness",
      "TYPE": "float",
      "DEFAULT": 0.48,
      "MIN": 0.05,
      "MAX": 0.95,
      "LABEL": "Line Thickness"
    },
    {
      "NAME": "lineSpeed",
      "TYPE": "float",
      "DEFAULT": 0.35,
      "MIN": -4.0,
      "MAX": 4.0,
      "LABEL": "Line Speed"
    },
    {
      "NAME": "blockMotion",
      "TYPE": "float",
      "DEFAULT": 0.020,
      "MIN": 0.0,
      "MAX": 0.15,
      "LABEL": "Block Motion"
    },
    {
      "NAME": "patternScale",
      "TYPE": "float",
      "DEFAULT": 1.0,
      "MIN": 0.5,
      "MAX": 1.8,
      "LABEL": "Pattern Scale"
    },
    {
      "NAME": "edgeSoftness",
      "TYPE": "float",
      "DEFAULT": 0.006,
      "MIN": 0.0,
      "MAX": 0.05,
      "LABEL": "Edge Softness"
    },
    {
      "NAME": "distortion",
      "TYPE": "float",
      "DEFAULT": 0.010,
      "MIN": 0.0,
      "MAX": 0.08,
      "LABEL": "Line Distortion"
    },
    {
      "NAME": "grainAmount",
      "TYPE": "float",
      "DEFAULT": 0.055,
      "MIN": 0.0,
      "MAX": 0.35,
      "LABEL": "Grain"
    },
    {
      "NAME": "backgroundTone",
      "TYPE": "float",
      "DEFAULT": 0.93,
      "MIN": 0.0,
      "MAX": 1.0,
      "LABEL": "Background Tone"
    },
    {
      "NAME": "contrast",
      "TYPE": "float",
      "DEFAULT": 1.0,
      "MIN": 0.2,
      "MAX": 2.0,
      "LABEL": "Contrast"
    },
    {
      "NAME": "invert",
      "TYPE": "bool",
      "DEFAULT": false,
      "LABEL": "Invert"
    }
  ]
}*/

float hash11(float p) {
    p = fract(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return fract(p);
}

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 345.45));
    p += dot(p, p + 34.345);
    return fract(p.x * p.y);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);

    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));

    vec2 u = f * f * (3.0 - 2.0 * f);

    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float rectMask(vec2 uv, vec2 center, vec2 size, float blur) {
    vec2 d = abs(uv - center) - size * 0.5;
    float dist = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
    return 1.0 - smoothstep(0.0, blur, dist);
}

float linePattern(vec2 uv, float density, float thickness, float phase, float wobble) {
    float y = uv.y * density;

    y += sin(uv.x * 8.0 + phase * 0.9) * wobble;
    y += sin(uv.x * 19.0 - phase * 1.2) * wobble * 0.5;

    float f = fract(y + phase);

    float line = 1.0 - smoothstep(thickness, thickness + 0.03, f);
    line += smoothstep(1.0 - thickness - 0.03, 1.0 - thickness, f);
    line = clamp(line, 0.0, 1.0);

    return line;
}

vec2 animateCenter(vec2 center, float seed, float amt, float speedMul) {
    float t = TIME * lineSpeed * speedMul;
    center.x += sin(t + seed * 5.7) * amt;
    center.y += cos(t * 0.73 + seed * 8.3) * amt * 0.65;
    return center;
}

float blockLayer(
    vec2 uv,
    vec2 center,
    vec2 size,
    float densityMul,
    float darkness,
    float opacity,
    float seed,
    float speedMul
) {
    vec2 c = animateCenter(center, seed, blockMotion, speedMul);
    vec2 s = size * patternScale;

    float mask = rectMask(uv, c, s, edgeSoftness);

    float localNoise = noise(uv * vec2(110.0, 280.0) + seed * 20.0);
    float phase = TIME * lineSpeed * (0.8 + densityMul * 0.8) + seed * 10.0;
    float wobble = distortion * (0.4 + localNoise * 0.9);

    float lines = linePattern(uv, lineDensity * densityMul, lineThickness, phase, wobble);

    float value = mix(1.0, 1.0 - lines, darkness);
    return mix(1.0, value, mask * opacity);
}

void main() {
    vec2 uv = isf_FragNormCoord.xy;

    float aspect = RENDERSIZE.x / RENDERSIZE.y;
    vec2 uva = uv;
    uva.x = (uv.x - 0.5) * aspect + 0.5;

    float paper = backgroundTone;
    paper += (noise(uv * RENDERSIZE.xy * 0.35) - 0.5) * grainAmount;
    paper += (noise(uv * RENDERSIZE.xy * 0.95 + 17.0) - 0.5) * grainAmount * 0.45;

    float col = paper;

    // 큰 좌상단 블록
    col *= blockLayer(
        uva,
        vec2(0.20, 0.80),
        vec2(0.52, 0.36),
        0.75,
        0.48,
        1.0,
        0.11,
        0.60
    );

    // 우상단 큰 진한 블록
    col *= blockLayer(
        uva,
        vec2(0.80, 0.78),
        vec2(0.56, 0.54),
        1.10,
        0.82,
        1.0,
        0.23,
        0.42
    );

    // 중앙 밝은 블록
    col *= blockLayer(
        uva,
        vec2(0.37, 0.42),
        vec2(0.44, 0.44),
        1.00,
        0.72,
        1.0,
        0.37,
        0.85
    );

    // 우하단 넓은 밝은 블록
    col *= blockLayer(
        uva,
        vec2(0.76, 0.25),
        vec2(0.58, 0.38),
        0.88,
        0.42,
        1.0,
        0.49,
        0.55
    );

    // 좌하단 가로 블록
    col *= blockLayer(
        uva,
        vec2(0.13, 0.16),
        vec2(0.26, 0.28),
        0.92,
        0.70,
        1.0,
        0.62,
        0.68
    );

    // 반투명 회색 상단 중앙 블록
    {
        vec2 c = animateCenter(vec2(0.40, 0.66), 0.75, blockMotion * 0.8, 0.75);
        vec2 s = vec2(0.34, 0.20) * patternScale;
        float mask = rectMask(uva, c, s, edgeSoftness);
        float phase = TIME * lineSpeed * 0.95 + 7.5;
        float lines = linePattern(uva, lineDensity * 0.82, lineThickness * 0.85, phase, distortion * 0.6);
        float grey = mix(0.68, 0.50 + (1.0 - lines) * 0.18, 0.55);
        col = mix(col, grey, mask * 0.75);
    }

    // 좌하단 진한 세로 블록
    {
        vec2 c = animateCenter(vec2(0.30, 0.22), 0.89, blockMotion * 0.65, 0.95);
        vec2 s = vec2(0.12, 0.28) * patternScale;
        float mask = rectMask(uva, c, s, edgeSoftness);
        float phase = TIME * lineSpeed * 1.30 + 2.0;
        float lines = linePattern(uva, lineDensity * 1.18, lineThickness * 0.72, phase, distortion * 0.7);
        float darkVal = mix(0.28, 0.20 + (1.0 - lines) * 0.20, 0.75);
        col = mix(col, darkVal, mask * 0.95);
    }

    // 중앙 우측 세로 반투명 블록
    {
        vec2 c = animateCenter(vec2(0.58, 0.32), 0.96, blockMotion * 0.75, 0.70);
        vec2 s = vec2(0.12, 0.34) * patternScale;
        float mask = rectMask(uva, c, s, edgeSoftness);
        float phase = TIME * lineSpeed * 0.72 + 11.0;
        float lines = linePattern(uva, lineDensity * 0.80, lineThickness * 0.95, phase, distortion * 0.5);
        float midVal = mix(0.62, 0.48 + (1.0 - lines) * 0.14, 0.55);
        col = mix(col, midVal, mask * 0.70);
    }

    // 미세한 전체 스캔 떨림
    float scan = sin(uv.y * RENDERSIZE.y * 0.45 + TIME * 7.0) * 0.0025;
    col += scan;

    // 종이 질감 추가
    col += (noise(uv * vec2(900.0, 1200.0)) - 0.5) * grainAmount * 0.55;

    // 콘트라스트
    col = (col - 0.5) * contrast + 0.5;
    col = clamp(col, 0.0, 1.0);

    if (invert) {
        col = 1.0 - col;
    }

    gl_FragColor = vec4(vec3(col), 1.0);
}