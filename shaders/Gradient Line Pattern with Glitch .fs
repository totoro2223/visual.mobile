/*{
  "CATEGORIES": ["Generator"],
  "DESCRIPTION": "Gradient Line Pattern with Glitch - Audio Reactive Ready",
  "ISFVSN": "2",
  "INPUTS": [
    { "NAME": "time", "TYPE": "float" },
    { "NAME": "lineDensity", "TYPE": "float", "DEFAULT": 20.0, "MIN": 5.0, "MAX": 100.0 },
    { "NAME": "glitchAmount", "TYPE": "float", "DEFAULT": 0.2, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "lineDirection", "TYPE": "long", "VALUES": [0, 1], "LABELS": ["Vertical", "Horizontal"], "DEFAULT": 0 },
    { "NAME": "gradientType", "TYPE": "long", "VALUES": [0, 1], "LABELS": ["Linear", "Radial"], "DEFAULT": 0 },
    { "NAME": "baseColor", "TYPE": "color", "DEFAULT": [0.2, 0.6, 1.0, 1.0] },
    { "NAME": "glitchSpeed", "TYPE": "float", "DEFAULT": 1.0, "MIN": 0.0, "MAX": 10.0 }
  ]
}*/

float rand(vec2 co) {
    return fract(sin(dot(co.xy ,vec2(12.9898,78.233))) * 43758.5453);
}

float linePattern(vec2 uv) {
    float lines;
    if (lineDirection == 0) {
        lines = step(0.5, fract(uv.x * lineDensity));
    } else {
        lines = step(0.5, fract(uv.y * lineDensity));
    }

    return lines;
}

vec3 getGradient(vec2 uv) {
    if (gradientType == 0) {
        return mix(vec3(0.0), baseColor.rgb, uv.y); // Linear
    } else {
        float r = length(uv - 0.5);
        return mix(vec3(0.0), baseColor.rgb, 1.0 - smoothstep(0.0, 0.7, r)); // Radial
    }
}

void main() {
    vec2 uv = gl_FragCoord.xy / RENDERSIZE.xy;

    // Glitch UV offset
    vec2 glitchUV = uv;
    glitchUV.x += glitchAmount * (rand(vec2(time * glitchSpeed, uv.y)) - 0.5);
    glitchUV.y += glitchAmount * (rand(vec2(uv.x, time * glitchSpeed)) - 0.5);

    float lines = linePattern(glitchUV);
    vec3 grad = getGradient(uv);

    vec3 color = mix(grad, vec3(1.0), lines);

    gl_FragColor = vec4(color, 1.0);
}
