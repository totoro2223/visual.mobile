/*{
    "CATEGORIES": [
        "Generator",
        "Geometric"
    ],
    "CREDIT": "Isometric Facet Hatching (Crisp Lines)",
    "DESCRIPTION": "Isometric triangular facet hatching with sharp, non-blurred vector-style lines.",
    "INPUTS": [
        {
            "NAME": "scale",
            "TYPE": "float",
            "MIN": 2.0,
            "MAX": 15.0,
            "DEFAULT": 4.3,
            "LABEL": "Grid Scale"
        },
        {
            "NAME": "lineDensity",
            "TYPE": "float",
            "MIN": 5.0,
            "MAX": 50.0,
            "DEFAULT": 18.0,
            "LABEL": "Line Density"
        },
        {
            "NAME": "borderThickness",
            "TYPE": "float",
            "MIN": 0.01,
            "MAX": 0.2,
            "DEFAULT": 0.01,
            "LABEL": "Triangle Border Thickness"
        },
        {
            "NAME": "clusterSize",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 2.0,
            "DEFAULT": 0.8,
            "LABEL": "Cluster Size"
        },
        {
            "NAME": "randomness",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.7,
            "LABEL": "Density Variation"
        },
        {
            "NAME": "backgroundColor",
            "TYPE": "color",
            "DEFAULT": [0.95, 0.96, 0.97, 1.0],
            "LABEL": "Background Color"
        },
        {
            "NAME": "lineColor",
            "TYPE": "color",
            "DEFAULT": [0.08, 0.12, 0.20, 1.0],
            "LABEL": "Line Color"
        }
    ]
}*/

#ifdef GL_ES
precision mediump float;
#endif

#define PI 3.14159265359
#define SQRT3 1.73205080757

// Hash acak berdasarkan titik grid
float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

// Fungsi grid segitiga isometrik
vec4 simplexGrid(vec2 p, out float edgeDist) {
    vec2 s = vec2(p.x - p.y / SQRT3, p.y * 2.0 / SQRT3);
    
    vec2 i = floor(s);
    vec2 f = fract(s);
    
    float triType = (f.x + f.y > 1.0) ? 1.0 : 0.0;
    
    float d1 = f.x;
    float d2 = f.y;
    float d3 = abs(1.0 - f.x - f.y);
    
    edgeDist = min(min(d1, d2), d3);
    
    vec2 triID = i + vec2(triType);
    
    return vec4(f, triID);
}

// Generator garis hatching TAJAM (tanpa blur)
float drawHatchingCrisp(vec2 p, float angle, float density, float thickness) {
    float c = cos(angle);
    float s = sin(angle);
    float pos = p.x * c + p.y * s;
    
    float wave = fract(pos * density);
    
    // Menggunakan step untuk batas yang benar-benar tajam/padat
    float val = abs(wave - 0.5) * 2.0;
    return step(val, thickness);
}

void main() {
    // Normalisasi UV (Pusat di 0,0)
    vec2 uv = (gl_FragCoord.xy * 2.0 - RENDERSIZE.xy) / RENDERSIZE.y;
    
    float distFromCenter = length(uv);
    vec2 st = uv * scale;
    
    float edgeDist;
    vec4 grid = simplexGrid(st, edgeDist);
    vec2 triID = grid.zw;
    
    float randVal = hash(triID);
    float randVal2 = hash(triID + vec2(15.4, 7.8));
    
    float clusterMask = step(randVal * 0.4 + (1.0 - clusterSize), distFromCenter);
    
    // Arah garis hatching (0°, 60°, 120°)
    float angleChoice = floor(randVal * 3.0);
    float angle = 0.0;
    if (angleChoice == 1.0) angle = PI / 3.0;
    else if (angleChoice == 2.0) angle = 2.0 * PI / 3.0;
    
    float lineThickness = mix(0.2, 0.7, pow(randVal2, 2.0) * randomness);
    
    // 1. Gambar garis hatching tajam
    float lines = drawHatchingCrisp(st, angle, lineDensity, lineThickness);
    
    // 2. Gambar garis tepi (border) tajam tanpa blur
    float border = step(edgeDist, borderThickness);
    
    // Gabungkan garis isi dan garis tepi
    float pattern = max(lines, border);
    
    // Sembunyikan bagian di luar kluster utama
    if (clusterMask > 0.5) {
        pattern = 0.0;
    }
    
    // Render Warna Akhir
    vec3 color = mix(backgroundColor.rgb, lineColor.rgb, pattern);

    gl_FragColor = vec4(color, 1.0);
}