/*{
    "DESCRIPTION": "Frequencies of the Unheard - ink grid with slow morphing patterns",
    "CREDIT": "for ArtTech / FREQUENCIES OF THE UNHEARD exhibition",
    "CATEGORIES": ["Generator", "Texture"],
    "INPUTS": [
        {
            "NAME": "speed",
            "TYPE": "float",
            "DEFAULT": 0.15,
            "MIN": 0.0,
            "MAX": 1.0,
            "LABEL": "Animation Speed"
        },
        {
            "NAME": "gridScale",
            "TYPE": "float",
            "DEFAULT": 18.0,
            "MIN": 4.0,
            "MAX": 60.0,
            "LABEL": "Grid Scale (columns)"
        },
        {
            "NAME": "cellAspect",
            "TYPE": "float",
            "DEFAULT": 0.55,
            "MIN": 0.3,
            "MAX": 1.5,
            "LABEL": "Cell Aspect (vertical density)"
        },
        {
            "NAME": "cellSize",
            "TYPE": "float",
            "DEFAULT": 0.78,
            "MIN": 0.3,
            "MAX": 0.95,
            "LABEL": "Cell Size"
        },
        {
            "NAME": "cellRoundness",
            "TYPE": "float",
            "DEFAULT": 0.35,
            "MIN": 0.0,
            "MAX": 0.5,
            "LABEL": "Cell Corner Roundness"
        },
        {
            "NAME": "edgeDistortion",
            "TYPE": "float",
            "DEFAULT": 0.18,
            "MIN": 0.0,
            "MAX": 0.5,
            "LABEL": "Edge Distortion (thorns/bleed)"
        },
        {
            "NAME": "inkTexture",
            "TYPE": "float",
            "DEFAULT": 0.7,
            "MIN": 0.0,
            "MAX": 1.5,
            "LABEL": "Ink Texture Intensity"
        },
        {
            "NAME": "inkScale",
            "TYPE": "float",
            "DEFAULT": 3.5,
            "MIN": 0.5,
            "MAX": 12.0,
            "LABEL": "Ink Texture Scale"
        },
        {
            "NAME": "densityVariation",
            "TYPE": "float",
            "DEFAULT": 0.85,
            "MIN": 0.0,
            "MAX": 1.5,
            "LABEL": "Column Density Variation"
        },
        {
            "NAME": "cellJitter",
            "TYPE": "float",
            "DEFAULT": 0.12,
            "MIN": 0.0,
            "MAX": 0.4,
            "LABEL": "Cell Position Jitter"
        },
        {
            "NAME": "verticalFlow",
            "TYPE": "float",
            "DEFAULT": 0.4,
            "MIN": -2.0,
            "MAX": 2.0,
            "LABEL": "Vertical Flow Speed"
        },
        {
            "NAME": "morphRate",
            "TYPE": "float",
            "DEFAULT": 0.25,
            "MIN": 0.0,
            "MAX": 2.0,
            "LABEL": "Pattern Morph Rate"
        },
        {
            "NAME": "contrast",
            "TYPE": "float",
            "DEFAULT": 1.4,
            "MIN": 0.5,
            "MAX": 3.0,
            "LABEL": "Contrast"
        },
        {
            "NAME": "brightness",
            "TYPE": "float",
            "DEFAULT": 0.0,
            "MIN": -0.5,
            "MAX": 0.5,
            "LABEL": "Brightness"
        },
        {
            "NAME": "inkColor",
            "TYPE": "color",
            "DEFAULT": [0.05, 0.05, 0.05, 1.0],
            "LABEL": "Ink Color"
        },
        {
            "NAME": "paperColor",
            "TYPE": "color",
            "DEFAULT": [0.96, 0.95, 0.92, 1.0],
            "LABEL": "Paper Color"
        },
        {
            "NAME": "paperTexture",
            "TYPE": "float",
            "DEFAULT": 0.08,
            "MIN": 0.0,
            "MAX": 0.5,
            "LABEL": "Paper Grain"
        },
        {
            "NAME": "invert",
            "TYPE": "bool",
            "DEFAULT": false,
            "LABEL": "Invert"
        }
    ]
}*/

// ----------------------------------------------------------
// Hash & noise functions
// ----------------------------------------------------------

float hash11(float p) {
    p = fract(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return fract(p);
}

float hash21(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

vec2 hash22(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.xx + p3.yz) * p3.zy);
}

// Value noise
float valueNoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

// Fractal Brownian motion for ink texture
float fbm(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 5; i++) {
        v += a * valueNoise(p);
        p *= 2.07;
        a *= 0.5;
    }
    return v;
}

// Ridged noise — creates the cracked / crystallized look of dried ink
float ridged(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++) {
        float n = valueNoise(p);
        n = 1.0 - abs(n - 0.5) * 2.0;
        v += a * n;
        p *= 2.13;
        a *= 0.5;
    }
    return v;
}

// ----------------------------------------------------------
// Rounded box SDF — for cell shape
// ----------------------------------------------------------

float sdRoundBox(vec2 p, vec2 b, float r) {
    vec2 q = abs(p) - b + r;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
}

void main() {
    // Normalized coordinates, centered, aspect-corrected
    vec2 uv = isf_FragNormCoord;
    vec2 res = RENDERSIZE;
    float aspect = res.x / res.y;
    
    vec2 p = uv;
    p.x *= aspect;
    
    float t = TIME * speed;
    
    // ------------------------------------------------------
    // Grid coordinates
    // ------------------------------------------------------
    // Columns horizontally, more cells vertically (controlled by cellAspect)
    vec2 gridUV = vec2(p.x * gridScale, p.y * gridScale / cellAspect);
    
    // Vertical flow — the cells drift slowly downward (or upward if negative)
    gridUV.y += t * verticalFlow;
    
    vec2 cellId = floor(gridUV);
    vec2 cellLocal = fract(gridUV) - 0.5;  // -0.5..0.5 within each cell
    
    // ------------------------------------------------------
    // Per-cell jitter — handcrafted-grid feel
    // ------------------------------------------------------
    vec2 jitter = (hash22(cellId) - 0.5) * cellJitter;
    vec2 cp = cellLocal - jitter;
    
    // ------------------------------------------------------
    // Cell shape with distorted edges (thorns / bleed)
    // ------------------------------------------------------
    // Edge distortion — noise pushes the boundary in/out
    float edgeNoise = fbm(cellId * 2.7 + cp * 4.0 + t * 0.3) - 0.5;
    float thornNoise = ridged(cellId * 5.3 + cp * 12.0) - 0.5;
    
    vec2 distortedP = cp + vec2(
        edgeNoise * edgeDistortion * 0.5,
        thornNoise * edgeDistortion * 0.3
    );
    
    // Cell half-size
    float halfSize = cellSize * 0.5;
    float d = sdRoundBox(distortedP, vec2(halfSize), cellRoundness * halfSize);
    
    // Soft cell mask
    float cellMask = 1.0 - smoothstep(-0.02, 0.02, d);
    
    // ------------------------------------------------------
    // Per-column density — like spectrogram bands
    // ------------------------------------------------------
    // Each column has its own slowly-morphing brightness
    float colSeed = cellId.x * 1.317;
    float colDensity = fbm(vec2(colSeed, t * morphRate * 0.5));
    
    // Per-cell extra variation
    float cellSeed = hash21(cellId);
    float cellDensity = fbm(vec2(cellSeed * 7.13 + t * morphRate * 0.3, 
                                  cellId.y * 0.15 + t * morphRate * 0.2));
    
    // Combined density: column trend + per-cell variation
    float density = mix(0.5, colDensity, densityVariation) * 0.7 
                  + cellDensity * 0.5 * densityVariation;
    density = clamp(density, 0.0, 1.0);
    
    // ------------------------------------------------------
    // Ink texture inside the cell — crystallized / cracked pattern
    // ------------------------------------------------------
    vec2 inkUV = (cp + cellId * 0.13) * inkScale;
    inkUV += vec2(t * 0.05, t * 0.08);  // very slow drift inside cells
    
    float inkPattern = fbm(inkUV) * 0.6 + ridged(inkUV * 1.7 + cellId) * 0.4;
    inkPattern = pow(inkPattern, 1.3);
    
    // Mix density with texture
    float inkValue = density + (inkPattern - 0.5) * inkTexture;
    inkValue = clamp(inkValue, 0.0, 1.0);
    
    // ------------------------------------------------------
    // Cell bridges — faint vertical connections between cells
    // ------------------------------------------------------
    // Thin vertical strip in center of cell, only between very dark cells
    float bridgeX = 1.0 - smoothstep(0.0, 0.03, abs(cp.x - jitter.x));
    float bridgeY = step(0.35, abs(cellLocal.y));  // near top/bottom of cell
    float neighborDarkness = fbm(vec2(cellId.x * 1.317, 
                                       (cellId.y + sign(cellLocal.y)) * 0.15 + t * morphRate * 0.2));
    float bridge = bridgeX * bridgeY * step(0.6, density * neighborDarkness) * 0.4;
    
    // ------------------------------------------------------
    // Combine: cell mask + ink value + bridges
    // ------------------------------------------------------
    float ink = cellMask * inkValue + bridge;
    
    // Contrast & brightness
    ink = (ink - 0.5) * contrast + 0.5 + brightness;
    ink = clamp(ink, 0.0, 1.0);
    
    // Paper grain
    float grain = (hash21(p * 800.0 + t) - 0.5) * paperTexture;
    
    // ------------------------------------------------------
    // Final color
    // ------------------------------------------------------
    vec3 col = mix(paperColor.rgb, inkColor.rgb, ink);
    col += grain;
    
    if (invert) {
        col = 1.0 - col;
    }
    
    gl_FragColor = vec4(col, 1.0);
}