/*{
    "DESCRIPTION": "Tiger stripe pattern with color and position control",
    "CREDIT": "Custom",
    "ISFVSN": "2.0",
    "INPUTS": [
        { "NAME": "color1", "TYPE": "color", "DEFAULT": [1.0, 0.6, 0.2, 1.0], "LABEL": "Base Color (Orange)" },
        { "NAME": "color2", "TYPE": "color", "DEFAULT": [0.1, 0.05, 0.0, 1.0], "LABEL": "Stripe Color (Black)" },
        { "NAME": "rotation", "TYPE": "float", "MIN": 0.0, "MAX": 360.0, "DEFAULT": 0.0, "LABEL": "Rotation (degrees)" },
        { "NAME": "panX", "TYPE": "float", "MIN": -100.0, "MAX": 100.0, "DEFAULT": 0.0, "LABEL": "Pan X" },
        { "NAME": "panY", "TYPE": "float", "MIN": -100.0, "MAX": 100.0, "DEFAULT": 0.0, "LABEL": "Pan Y" },
        { "NAME": "stripeScale", "TYPE": "float", "MIN": 1.0, "MAX": 20.0, "DEFAULT": 8.0, "LABEL": "Stripe Scale" },
        { "NAME": "stripeThickness", "TYPE": "float", "MIN": 0.1, "MAX": 1.0, "DEFAULT": 0.4, "LABEL": "Stripe Thickness" },
        { "NAME": "irregularity", "TYPE": "float", "MIN": 0.0, "MAX": 1.0, "DEFAULT": 0.6, "LABEL": "Stripe Irregularity" },
        { "NAME": "mergeFactor", "TYPE": "float", "MIN": 0.0, "MAX": 1.0, "DEFAULT": 0.5, "LABEL": "Stripe Merging" },
        { "NAME": "curvature", "TYPE": "float", "MIN": 0.0, "MAX": 1.0, "DEFAULT": 0.4, "LABEL": "Stripe Curvature" },
        { "NAME": "contrast", "TYPE": "float", "MIN": 0.0, "MAX": 1.0, "DEFAULT": 0.8, "LABEL": "Contrast" }
    ]
}*/

#define PI 3.14159265359

vec2 rotate2D(vec2 v, float angle) {
    float s = sin(angle);
    float c = cos(angle);
    mat2 m = mat2(c, -s, s, c);
    return m * v;
}

// Pseudo-random noise function
float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

// Perlin-like noise
float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// Fractal noise for more organic patterns
float fbm(vec2 p) {
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;
    
    for (int i = 0; i < 5; i++) {
        value += amplitude * noise(p * frequency);
        frequency *= 2.0;
        amplitude *= 0.5;
    }
    
    return value;
}

// Generate tiger-like stripes with merging
float tigerStripes(vec2 uv, float scale, float thickness, float irregularity, float merge) {
    // Create vertical stripes with variation
    float stripe = sin(uv.x * scale);
    
    // Add irregularity with noise
    float noisePattern = fbm(uv * 3.0) * irregularity;
    stripe += noisePattern;
    
    // Add horizontal variation to break up stripes
    float horizontalBreak = fbm(vec2(uv.y * 2.0, uv.x * 0.5)) * 0.3 * irregularity;
    stripe += horizontalBreak;
    
    // Create merging effect - some stripes connect at random intervals
    float mergeNoise = fbm(vec2(uv.y * 1.5, uv.x * 4.0));
    float mergeMask = smoothstep(0.3, 0.7, mergeNoise);
    
    // When merge is active, widen stripes to create connection points
    float mergeEffect = mix(1.0, 1.5 + mergeMask * 0.8, merge);
    
    // Add branching - some stripes split or merge
    float branchNoise = fbm(vec2(uv.x * 2.0, uv.y * 3.0));
    float branch = sin(uv.x * scale * 0.5 + branchNoise * 2.0) * merge * 0.3;
    stripe += branch;
    
    // Create thickness variation
    float thicknessMod = 1.0 + fbm(uv * 2.0) * 0.3 * irregularity;
    thicknessMod *= mergeEffect;
    
    // Create hard-edged stripes
    float stripePattern = smoothstep(thickness * thicknessMod, thickness * thicknessMod + 0.1, abs(stripe));
    
    return stripePattern;
}

void main() {
    vec2 uv = isf_FragNormCoord - 0.5;
    float aspect = RENDERSIZE.x / RENDERSIZE.y;
    uv.x *= aspect;
    
    // Apply pan (normalize from -100/100 to reasonable values)
    vec2 pan = vec2(panX, panY) * 0.01;
    uv -= pan;
    
    // Apply rotation
    float angleRad = rotation * PI / 180.0;
    uv = rotate2D(uv, angleRad);
    
    // Generate tiger stripe pattern
    float stripes = tigerStripes(uv, stripeScale, stripeThickness, irregularity, mergeFactor);
    
    // Apply contrast
    stripes = pow(stripes, mix(0.5, 2.0, contrast));
    
    // Mix colors based on stripe pattern
    vec3 finalColor = mix(color2.rgb, color1.rgb, stripes);
    
    gl_FragColor = vec4(finalColor, 1.0);
}