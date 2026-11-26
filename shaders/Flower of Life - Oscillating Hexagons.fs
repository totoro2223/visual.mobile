/*
{
  "CATEGORIES": [
    "Generative",
    "Hexagonal Grid",
    "Psychedelic"
  ],
  "DESCRIPTION": "Generates a Flower of Life pattern with oscillating hexagons.",
  "IMPORTED": {},
  "INPUTS": [
    {
      "NAME": "timeScale",
      "TYPE": "float",
      "DEFAULT": 0.5,
      "MIN": 0.01,
      "MAX": 2.0
    },
    {
      "NAME": "gridSize",
      "TYPE": "float",
      "DEFAULT": 36.0,
      "MIN": 10.0,
      "MAX": 100.0
    },
    {
      "NAME": "oscillationStrength",
      "TYPE": "float",
      "DEFAULT": 0.5,
      "MIN": 0.1,
      "MAX": 1.0
    },
    {
      "NAME": "brightness",
      "TYPE": "float",
      "DEFAULT": 1.5,
      "MIN": 0.5,
      "MAX": 3.0
    }
  ],
  "ISFVSN": "2"
}
*/

#define TWO_PI 6.2831853072
#define PI 3.14159265359

// Helper functions
vec2 rotate(vec2 v, float angle) {
    float c = cos(angle);
    float s = sin(angle);
    return v * mat2(c, -s, s, c);
}

vec3 coordToHex(vec2 coord, float scale, float angle) {
    vec2 c = rotate(coord, angle);
    float q = (1.0 / 3.0 * sqrt(3.0) * c.x - 1.0 / 3.0 * c.y) * scale;
    float r = 2.0 / 3.0 * c.y * scale;
    return vec3(q, r, -q - r);
}

float hexToFloat(vec3 hex, float amt) {
    return mix(max(abs(hex.x), max(abs(hex.y), abs(hex.z))), 1.0 - length(hex) / sqrt(3.0), amt);
}

float nsin(float value) {
    return sin(value * TWO_PI) * 0.5 + 0.5;
}

void main() {
    vec2 uv = (gl_FragCoord.xy / RENDERSIZE.xy) - 0.5;
    uv.x *= RENDERSIZE.x / RENDERSIZE.y;

    float time = TIME * timeScale;
    float angle = PI * nsin(time * 0.1) + PI / 6.0;

    vec3 hex = coordToHex(uv, gridSize, angle);
    float value = 0.0;

    for (int i = 0; i < 6; i++) {
        float offset = float(i) / 6.0;
        vec3 cell = fract(hex / (1.0 + float(i))) * 2.0 - 1.0;
        float wrappedTime = mod(time, 10.0);
        value += nsin(hexToFloat(cell, nsin(oscillationStrength + wrappedTime + offset)) * 5.0 + wrappedTime);
    }

    value = 0.5 + 0.5 * sin(value);
    value = clamp(value, 0.0, 1.0);

    vec3 color = vec3(
        abs(sin(value * 1.1)),
        abs(sin(value * 1.0)),
        abs(sin(value * 0.9))
    );

    color *= brightness;
    gl_FragColor = vec4(color, 1.0);
}