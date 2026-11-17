/*{
  "DESCRIPTION": "Osciloscopio (audio)",
  "ISFVSN": "2",
  "CATEGORIES": ["Audio"],
  "INPUTS": [
    { "NAME": "wave", "TYPE": "audio" },
    { "NAME": "amp", "LABEL": "Amplitud", "TYPE": "float", "DEFAULT": 1.0, "MIN": 0.1, "MAX": 4.0 },
    { "NAME": "thickness", "LABEL": "Grosor", "TYPE": "float", "DEFAULT": 0.006, "MIN": 0.001, "MAX": 0.05 },
    { "NAME": "color", "LABEL": "Color", "TYPE": "color", "DEFAULT": [0.2, 1.0, 0.6, 1.0] }
  ]
}*/

void main() {
    vec2 uv = isf_FragNormCoord;
    float s = IMG_NORM_PIXEL(wave, vec2(uv.x, 0.5)).r - 0.5;
    float d = abs(uv.y - (0.5 + s * amp));
    float line = 1.0 - smoothstep(0.0, thickness, d);
    float glow = 0.006 / (d + 0.02);
    gl_FragColor = vec4(color.rgb * (line + glow), 1.0);
}
