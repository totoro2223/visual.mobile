/*{
    "CATEGORIES": ["Humo"],
    "DESCRIPTION": "Lineas horizontales que ondulan al ritmo: en humo, un techo de luz que se mueve.",
    "INPUTS": [
        {"NAME": "altura", "LABEL": "Altura", "TYPE": "float", "DEFAULT": 0.5, "MIN": 0, "MAX": 1},
        {"NAME": "lineas", "LABEL": "Lineas", "TYPE": "float", "DEFAULT": 1, "MIN": 1, "MAX": 6},
        {"NAME": "separacion", "LABEL": "Separacion", "TYPE": "float", "DEFAULT": 0.08, "MIN": 0.01, "MAX": 0.3},
        {"NAME": "onda", "LABEL": "Onda", "TYPE": "float", "DEFAULT": 0.08, "MIN": 0, "MAX": 0.4},
        {"NAME": "ondas", "LABEL": "Ondas a lo ancho", "TYPE": "float", "DEFAULT": 1.5, "MIN": 0.25, "MAX": 8},
        {"NAME": "ritmo", "LABEL": "Ciclo", "TYPE": "long", "VALUES": [1, 2, 4, 8], "LABELS": ["1 tiempo", "2 tiempos", "1 compas", "2 compases"], "DEFAULT": 4},
        {"NAME": "grosor", "LABEL": "Grosor (px)", "TYPE": "float", "DEFAULT": 2, "MIN": 0.5, "MAX": 12},
        {"NAME": "color", "LABEL": "Color", "TYPE": "color", "DEFAULT": [1.0, 0.0, 0.3, 1.0]}
    ]
}*/

const float TAU = 6.2831853;

void main() {
    vec2 uv = gl_FragCoord.xy / RENDERSIZE;
    float ph = VISUAL_BEATS / float(ritmo) * TAU;
    float n = floor(lineas), on = 0.0;
    for (int i = 0; i < 6; i++) {
        if (float(i) >= n) break;
        float k = float(i) - (n - 1.0) * 0.5;
        float y = altura + k * separacion + onda * sin(uv.x * ondas * TAU + ph + k * 0.7);
        on = max(on, 1.0 - smoothstep(grosor * 0.5, grosor * 0.5 + 1.0, abs(uv.y - y) * RENDERSIZE.y));
    }
    gl_FragColor = vec4(color.rgb * on, 1.0);
}
