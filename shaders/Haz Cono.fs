/*{
    "CATEGORIES": ["Humo"],
    "DESCRIPTION": "Anillos finos que laten al ritmo: en humo forman conos huecos. Con cortes, el cono gira.",
    "INPUTS": [
        {"NAME": "radio", "LABEL": "Radio", "TYPE": "float", "DEFAULT": 0.3, "MIN": 0.02, "MAX": 0.5},
        {"NAME": "pulso", "LABEL": "Pulso", "TYPE": "float", "DEFAULT": 0.3, "MIN": 0, "MAX": 1},
        {"NAME": "anillos", "LABEL": "Anillos", "TYPE": "float", "DEFAULT": 1, "MIN": 1, "MAX": 5},
        {"NAME": "cortes", "LABEL": "Cortes", "TYPE": "float", "DEFAULT": 0, "MIN": 0, "MAX": 24},
        {"NAME": "ritmo", "LABEL": "Ciclo", "TYPE": "long", "VALUES": [1, 2, 4, 8], "LABELS": ["1 tiempo", "2 tiempos", "1 compas", "2 compases"], "DEFAULT": 4},
        {"NAME": "grosor", "LABEL": "Grosor (px)", "TYPE": "float", "DEFAULT": 2, "MIN": 0.5, "MAX": 12},
        {"NAME": "color", "LABEL": "Color", "TYPE": "color", "DEFAULT": [0.1, 0.4, 1.0, 1.0]}
    ]
}*/

const float TAU = 6.2831853;

void main() {
    vec2 p = gl_FragCoord.xy - 0.5 * RENDERSIZE;
    float beat = VISUAL_BEATS;
    float env = exp(-fract(beat) * 5.0);                       // a kick on every beat, fading
    float r0 = radio * RENDERSIZE.y * (1.0 + pulso * env * 0.5);
    float n = floor(anillos), len = length(p), on = 0.0;
    for (int i = 0; i < 5; i++) {
        if (float(i) >= n) break;
        float r = r0 * (float(i) + 1.0) / n;
        on = max(on, 1.0 - smoothstep(grosor * 0.5, grosor * 0.5 + 1.0, abs(len - r)));
    }
    if (cortes >= 1.0) { // dashes turning one way, a turn every cycle
        float a = atan(p.y, p.x) / TAU + beat / float(ritmo) / floor(cortes);
        on *= step(0.5, fract(a * floor(cortes)));
    }
    gl_FragColor = vec4(color.rgb * on, 1.0);
}
