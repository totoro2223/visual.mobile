/*
{
    "DESCRIPTION": "Psychedelic spiral pattern with tunable distortion, color pulse, and morphing",
    "CATEGORIES": ["Automatically Converted", "Shadertoy", "Color", "Psychedelic"],
    "INPUTS": [
        {
            "NAME": "DistortionStrength",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 2.0,
            "DEFAULT": 1.0,
            "LABEL": "Distortion Strength"
        },
        {
            "NAME": "ColorPulseFreq",
            "TYPE": "float",
            "MIN": 0.1,
            "MAX": 5.0,
            "DEFAULT": 1.0,
            "LABEL": "Color Pulse Frequency"
        },
        {
            "NAME": "ColorPulseAmp",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.5,
            "LABEL": "Color Pulse Amplitude"
        },
        {
            "NAME": "SpiralArms",
            "TYPE": "float",
            "MIN": 1.0,
            "MAX": 16.0,
            "DEFAULT": 4.0,
            "LABEL": "Spiral Arms"
        },
        {
            "NAME": "SpiralPower",
            "TYPE": "float",
            "MIN": 0.1,
            "MAX": 10.0,
            "DEFAULT": 1.0,
            "LABEL": "Spiral Power"
        },
        {
            "NAME": "InvertSpiral",
            "TYPE": "bool",
            "DEFAULT": false,
            "LABEL": "Invert Spiral"
        },
        {
            "NAME": "ColorMode",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 2.0,
            "DEFAULT": 0.0,
            "LABEL": "Color Mode",
            "VALUES": ["Classic", "Acid", "Chill"]
        }
    ],
    "ISFVSN": "2"
}
*/

#define TAU 6.28318530718
#define rotation(angle) mat2(cos(angle), -sin(angle), sin(angle), cos(angle))

float eqTri(vec2 p, float r) {
    const float k = sqrt(3.0);
    p.x = abs(p.x) - r;
    p.x = p.x + r / k;
    if (p.x + k * p.y > 0.0) p = vec2(p.x - k * p.y, -k * p.x - p.y) / 2.0;
    p.x -= clamp(p.x, -2.0 * r, 0.0);
    return -length(p) * sign(p.y);
}

vec3 rainbowPalette(float t, float mode) {
    float h = t * 1.0 + mode;
    float s = 0.9;
    float v = 1.0;

    float i = floor(h * 6.0);
    float f = fract(h * 6.0);

    vec3 rgb = vec3(1.0, 0.0, 0.0);

    if (i == 0.0) {
        rgb = vec3(1.0, f, 0.0);
    } else if (i == 1.0) {
        rgb = vec3(1.0 - f, 1.0, 0.0);
    } else if (i == 2.0) {
        rgb = vec3(0.0, 1.0, f);
    } else if (i == 3.0) {
        rgb = vec3(0.0, 1.0 - f, 1.0);
    } else if (i == 4.0) {
        rgb = vec3(f, 0.0, 1.0);
    } else if (i == 5.0) {
        rgb = vec3(1.0, 0.0, 1.0 - f);
    }

    return rgb * v;
}

vec3 palette(float t, float mode) {
    if (mode == 1.0) return rainbowPalette(t, 0.0); // Acid
    if (mode == 2.0) return rainbowPalette(t, 0.33);    // Chill
    return rainbowPalette(t, 0.66);            // Classic
}

// Color pulse function
float pulse(float t, float freq, float amp) {
    return 0.5 + 0.5 * sin(t * freq) * amp;
}

void main() {
    vec2 uv = (gl_FragCoord.xy * 2.0 - RENDERSIZE.xy) / RENDERSIZE.y;
    vec2 uv0 = uv;
    uv = normalize(vec3(uv, 1.2)).xy;

    float time = TIME;
    float rep = 3.0;
    float r3 = mod(length(uv), 22.0);
    float a3 = sqrt(r3) - time * 0.12;

    float theta = atan(uv.y, uv.x);
    vec2 polarUV = vec2(-theta, a3);
    uv = fract(polarUV);

    vec3 finalColor = vec3(0.0);
    vec2 cPos = -1.0 + 2.0 * gl_FragCoord.xy / RENDERSIZE.xy;
    float cLength = length(cPos);

    for (float j = 0.0; j < 4.0; j++) {
        vec3 col2 = vec3(0.0);
        float tShift = fract(0.1 * time * 0.51);
        uv *= rotation(3.0 * TAU * (0.3 - clamp(length(uv), 0.0, 0.3))); // FIX: Added missing closing parenthesis

        // Optional ripple warp
        uv += (cPos / cLength) * cos(cLength * 4.0 - time * 4.0) * 0.53 * DistortionStrength;

        float s = -1.0;
        for (float i = 0.0; i < 5.0; i++) {
            float rad = 1.4 / pow(2.0, i) * (0.9 - 0.2 * i);
            uv *= rotation(-2.0 * s * (i + 1.0) * TAU * tShift);
            float tri = eqTri(uv, rad);
            s *= -1.0;
            col2 += 1.004 / abs(tri);
        }

        uv = fract(uv * 1.5) - 0.5;
        float d = length(uv) * exp(-length(uv0));
        vec3 col = palette(length(uv0) + j * 0.4 + time * ColorPulseFreq, ColorMode);
        d = sin(d * 8.0 + time) / 8.0;
        d = abs(d);
        d = pow(0.01 / d, 1.2);
        finalColor += col * d;
    }

    gl_FragColor = vec4(finalColor, 1.0); // FIX: Added missing semicolon
}