/*{
  "DESCRIPTION": "Barras de espectro (audioFFT)",
  "ISFVSN": "2",
  "CATEGORIES": ["Audio"],
  "INPUTS": [
    { "NAME": "fft", "TYPE": "audioFFT" },
    { "NAME": "bars", "LABEL": "Barras", "TYPE": "float", "DEFAULT": 64.0, "MIN": 8.0, "MAX": 256.0 },
    { "NAME": "height", "LABEL": "Altura", "TYPE": "float", "DEFAULT": 1.0, "MIN": 0.2, "MAX": 3.0 },
    { "NAME": "colorLow", "LABEL": "Color bajo", "TYPE": "color", "DEFAULT": [0.1, 0.3, 1.0, 1.0] },
    { "NAME": "colorHigh", "LABEL": "Color alto", "TYPE": "color", "DEFAULT": [1.0, 0.2, 0.6, 1.0] },
    { "NAME": "mirror", "LABEL": "Espejo", "TYPE": "bool", "DEFAULT": true }
  ]
}*/

void main() {
    vec2 uv = isf_FragNormCoord;
    float x = floor(uv.x * bars) / bars;
    float f = x * x * 0.6;                      // squared axis: more room for the bass
    float v = IMG_NORM_PIXEL(fft, vec2(f, 0.5)).r * height;
    float y = mirror ? abs(uv.y - 0.5) * 2.0 : uv.y;
    float gap = step(0.15, fract(uv.x * bars));
    float on = step(y, v) * gap;
    vec3 col = mix(colorLow.rgb, colorHigh.rgb, y);
    gl_FragColor = vec4(col * on + colorHigh.rgb * 0.15 * v * (1.0 - y), 1.0);
}
