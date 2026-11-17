/*{
  "DESCRIPTION": "Anillos: prueba de todos los tipos de input (point2D, long, bool, event)",
  "ISFVSN": "2",
  "CATEGORIES": ["Generator"],
  "INPUTS": [
    { "NAME": "center", "LABEL": "Centro", "TYPE": "point2D", "DEFAULT": [0.5, 0.5], "MIN": [0.0, 0.0], "MAX": [1.0, 1.0] },
    { "NAME": "shape", "LABEL": "Forma", "TYPE": "long", "VALUES": [0, 1, 2], "LABELS": ["Círculos", "Cuadrados", "Diamantes"], "DEFAULT": 0 },
    { "NAME": "count", "LABEL": "Cantidad", "TYPE": "float", "DEFAULT": 10.0, "MIN": 1.0, "MAX": 40.0 },
    { "NAME": "pulse", "LABEL": "Pulso (asignalo a graves)", "TYPE": "float", "DEFAULT": 0.0, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "tint", "LABEL": "Color", "TYPE": "color", "DEFAULT": [1.0, 0.6, 0.2, 1.0] },
    { "NAME": "invert", "LABEL": "Invertir", "TYPE": "bool", "DEFAULT": false },
    { "NAME": "flash", "LABEL": "Flash", "TYPE": "event" }
  ]
}*/

void main() {
    vec2 p = isf_FragNormCoord - center;
    p.x *= RENDERSIZE.x / RENDERSIZE.y;
    float d = shape == 0 ? length(p) : (shape == 1 ? max(abs(p.x), abs(p.y)) : abs(p.x) + abs(p.y));
    float v = 0.5 + 0.5 * sin((d * count - TIME) * 3.1415927 - pulse * 6.0);
    v = smoothstep(0.4, 0.6, v);
    vec3 col = tint.rgb * v * (1.0 + pulse);
    if (invert) col = 1.0 - col;
    if (flash) col = vec3(1.0);
    gl_FragColor = vec4(col, 1.0);
}
