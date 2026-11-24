/*{
    "CATEGORIES": ["Humo"],
    "DESCRIPTION": "Una linea que cruza la pantalla en cada ciclo del ritmo: en humo, una lamina que barre el lugar.",
    "INPUTS": [
        {"NAME": "direccion", "LABEL": "Direccion", "TYPE": "long", "VALUES": [0, 1, 2, 3], "LABELS": ["Izquierda a derecha", "Abajo a arriba", "Ida y vuelta horizontal", "Ida y vuelta vertical"], "DEFAULT": 2},
        {"NAME": "lineas", "LABEL": "Lineas", "TYPE": "float", "DEFAULT": 1, "MIN": 1, "MAX": 8},
        {"NAME": "ritmo", "LABEL": "Ciclo", "TYPE": "long", "VALUES": [1, 2, 4, 8], "LABELS": ["1 tiempo", "2 tiempos", "1 compas", "2 compases"], "DEFAULT": 4},
        {"NAME": "grosor", "LABEL": "Grosor (px)", "TYPE": "float", "DEFAULT": 3, "MIN": 0.5, "MAX": 20},
        {"NAME": "color", "LABEL": "Color", "TYPE": "color", "DEFAULT": [0.0, 1.0, 1.0, 1.0]}
    ]
}*/

void main() {
    vec2 uv = gl_FragCoord.xy / RENDERSIZE;
    float t = fract(VISUAL_BEATS / float(ritmo));
    if (direccion >= 2) t = 1.0 - abs(2.0 * t - 1.0); // there and back
    bool vertical = direccion == 1 || direccion == 3;
    float x = vertical ? uv.y : uv.x, size = vertical ? RENDERSIZE.y : RENDERSIZE.x;
    float n = floor(lineas);
    float d = abs(fract(x * n - t + 0.5) - 0.5) / n * size; // pixels to the nearest of n evenly spaced lines
    gl_FragColor = vec4(color.rgb * (1.0 - smoothstep(grosor * 0.5, grosor * 0.5 + 1.0, d)), 1.0);
}
