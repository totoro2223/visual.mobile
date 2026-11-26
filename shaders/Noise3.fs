/*{
    "DESCRIPTION": "Generates a hand-drawn wavy stripe grid pattern with fixed type casting errors.",
    "CREDIT": "Derived from image reference by AI Assistant",
    "CATEGORIES": [
        "Generator",
        "Pattern"
    ],
    "INPUTS": [
        {
            "NAME": "patternColor",
            "TYPE": "color",
            "DEFAULT": [0.0, 0.0, 0.0, 1.0],
            "LABEL": "Pattern Color"
        },
        {
            "NAME": "bgColor",
            "TYPE": "color",
            "DEFAULT": [1.0, 1.0, 1.0, 1.0],
            "LABEL": "Background Color"
        },
        {
            "NAME": "scale",
            "TYPE": "float",
            "MIN": 1.0,
            "MAX": 100.0,
            "DEFAULT": 30.0,
            "LABEL": "Overall Scale"
        },
        {
            "NAME": "hStep",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.5,
            "LABEL": "Horizontal Step"
        },
        {
            "NAME": "vStep",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.5,
            "LABEL": "Vertical Step"
        },
        {
            "NAME": "wavyAmp",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 0.1,
            "DEFAULT": 0.02,
            "LABEL": "Wavy Amplitude"
        },
        {
            "NAME": "wavyFreq",
            "TYPE": "float",
            "MIN": 0.1,
            "MAX": 20.0,
            "DEFAULT": 10.0,
            "LABEL": "Wavy Frequency"
        },
        {
            "NAME": "thickness",
            "TYPE": "float",
            "MIN": 0.01,
            "MAX": 0.5,
            "DEFAULT": 0.05,
            "LABEL": "Line Thickness"
        },
        {
            "NAME": "handDrawnLook",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.2,
            "LABEL": "Hand-Drawn Noisiness"
        },
        {
            "NAME": "warpSpeed",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.1,
            "LABEL": "Pattern Warp Speed"
        }
    ]
}*/

vec2 hash22(vec2 p) {
    p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
    return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

float fbm2d(vec2 p) {
    float f = 0.0;
    float a = 0.5;
    mat2 m = mat2(1.6, 1.2, -1.2, 1.6);
    for (int i = 0; i < 4; i++) {
        f += a * length(hash22(p));
        p = m * p;
        a *= 0.5;
    }
    return f;
}

void main() {
    vec2 uv = isf_FragNormCoord.xy;
    vec2 s_uv = uv * scale;
    
    // 수정 포인트: float(FRAMEINDEX) 로 명시적 형변환
    float f_time = float(FRAMEINDEX);
    
    vec2 warpNoise = vec2(fbm2d(s_uv * 0.1 + f_time * warpSpeed * 0.01),
                          fbm2d(s_uv * 0.1 + f_time * warpSpeed * 0.012 + 1.0));
    s_uv += warpNoise * handDrawnLook * 2.0;

    vec2 grid_coord = s_uv / vec2(hStep, vStep);
    vec2 fStrips = fract(grid_coord);
    vec2 iStrips = floor(grid_coord);
    
    // 수정 포인트: 여기서도 f_time 사용
    float waviness = sin(fStrips.x * wavyFreq + iStrips.y * 10.0 + f_time * warpSpeed * 0.05);
    float wave_offset = waviness * wavyAmp;
    
    float noisy_thickness = fbm2d(fStrips * 5.0 + iStrips.xy * 0.5) * handDrawnLook * 0.1;

    float target_y = 0.5 + wave_offset;
    float dist_from_center = abs(fStrips.y - target_y);
    
    float stripMask = step(dist_from_center, thickness + noisy_thickness);
    
    gl_FragColor = mix(bgColor, patternColor, stripMask);
}