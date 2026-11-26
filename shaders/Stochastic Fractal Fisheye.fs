/*{
    "DESCRIPTION": "Stochastic cell patterns fused with a KIFS fractal field and fisheye projection.",
    "CREDIT": "Converted by Gemini - Credit: spherical fractalic moving by nayk https://www.shadertoy.com/view/lcKczK",
    "ISFVSN": "2",
    "CATEGORIES": [
        "Fractal",
        "Digital",
        "Projection"
    ],
    "INPUTS": [
        {
            "NAME": "cellFrequency",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 0.05,
            "DEFAULT": 0.009
        },
        {
            "NAME": "fisheyeStrength",
            "TYPE": "float",
            "MIN": 0.1,
            "MAX": 2.0,
            "DEFAULT": 1.45
        },
        {
            "NAME": "mouse",
            "TYPE": "point2D",
            "DEFAULT": [0.5, 0.5]
        }
    ]
}*/

#ifdef GL_ES
precision highp float;
#endif

#define PI 3.14159265359
#define H(h)(cos((h)*6.3+vec3(0,23,21))*.5+.5)

// --- Stochastic Math ---
float rand(vec2 pos) {
    return fract(sin(dot(pos, vec2(23.2342, 82.29561))) * 82931.1857193);
}

mat2 rotate2d(float angle) {
    float s = sin(angle), c = cos(angle);
    return mat2(s, -c, c, s);
}

// Generates the 2D background pattern layers
vec3 pattern(vec2 pos, float size, float frequency, float angle, vec3 color, float transparency) {
    pos *= rotate2d(angle);
    vec2 p_scaled = pos * size;
    float t = TIME;
    
    // Multi-sampled random grid for "cell" look
    float r = floor(rand(floor(p_scaled + vec2(t, 0.0))) + frequency)
            + 0.4 * floor(rand(floor(p_scaled + vec2(-1.0 + t, 0.0))) + frequency)
            + 0.15 * floor(rand(floor(p_scaled + vec2(0.0, -1.0 + t))) + frequency);
            
    return r * color * transparency * (1.0 - 0.5 * rand(floor(p_scaled + vec2(-1.0 + t, 0.0))));
}

void main() {
    vec2 C = gl_FragCoord.xy;
    vec2 uv = (C / RENDERSIZE.xy - 0.5);
    uv.x *= RENDERSIZE.x / RENDERSIZE.y;

    // --- Phase 1: Camera & Fisheye Logic ---
    vec2 xy = uv; // Simplified uv for camera
    float time = TIME * 0.5;
    
    // Calculate 3D camera vectors
    vec3 ro = 5.5 * normalize(vec3(cos(time), cos(time) * 1.2, sin(time)));
    vec3 eyed = normalize(-ro);
    vec3 ud = normalize(cross(vec3(0.0, 1.0, 0.0), eyed));
    vec3 vd = normalize(cross(eyed, ud));
    
    // 
    float fov = PI * 0.8 + sin(time * 1.334) * fisheyeStrength;
    float f = fov * length(xy);
    vec3 rd = normalize(normalize(xy.x * ud + xy.y * vd) + (1.0 / tan(f)) * eyed);

    // Mouse interactions for normal mapping
    vec2 interp = C / RENDERSIZE.xy;
    vec2 mp = mouse / RENDERSIZE.xy;
    float hAngle = (2.0 * PI - 1.0) * 0.5 + interp.x;
    float vAngle = (PI - 1.0) * 0.5 + interp.y;
    
    vec3 n_vec;    
    n_vec.x = sin(vAngle) * sin(hAngle);
    n_vec.y = cos(vAngle);
    n_vec.z = sin(vAngle) * cos(hAngle);
    
    // --- Phase 2: Distortion Vectors ---
    // 
    vec3 r2 = normalize(vec3(uv, 1.1 - dot(uv, uv) * 15.0 * cos(TIME)));
    vec3 r3 = normalize(vec3(uv, 1.0 - dot(uv, uv) * 1.2 * sin(TIME)));

    // --- Phase 3: Fractal Raymarching ---
    vec4 O = vec4(0.0);
    vec3 n1, r_size = vec3(RENDERSIZE.xy, 1.0);
    vec3 d = normalize(vec3((C * 2.0 - r_size.xy) / r_size.y, 1.0));  

    for(float i = 0.0; i < 70.0; i++) {
        n1 = (i * 0.1) * d + r2 * 5.0 + r3 * rd * n_vec;
        n1.xz *= rotate2d(TIME);
        n1.yz *= rotate2d(TIME);
        n1.z += TIME * 10.0;
     
        float a_mod = 20.0;
        n1 = mod(n1 - a_mod, a_mod * 2.0) - a_mod;
        
        float s_scale = 2.0;
        float e_step = 0.0;
        
        // KIFS Folding loop
        for(int j = 0; j < 8; j++) {
            n1.xz *= rotate2d(TIME * 0.01);
            n1.yz *= rotate2d(TIME * 0.01);
            n1 = 0.3 - abs(n1 + r2);
            if(n1.x < n1.z) n1 = n1.zyx;
            if(n1.z < n1.y) n1 = n1.xzy;
            
            e_step = 1.7 + sin(TIME * 0.01) * 0.1;
            s_scale *= e_step;
            n1 = abs(n1) * e_step - vec3(15.0, 120.0, 40.0) + r3;
        }
        
        float dist = length(n1.y) / s_scale;
        O.xyz += mix(vec3(0.1, 0.2, 2.0), H(i * 0.1), 0.8) * 10.0 / (dist * 8000.0);
        if (dist < 0.001) break;
    }

    // Blend background stochastic pattern
    vec3 patternCol = pattern(uv, 500.0, cellFrequency, PI, vec3(76, 149, 156)/255.0, 0.9);
    gl_FragColor = vec4(O.xyz + patternCol * 0.1, 1.0);
}