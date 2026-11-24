/*{
    "CATEGORIES": ["Humo"],
    "DESCRIPTION": "Haces en abanico desde un punto, que se abren y se mueven al ritmo. Para proyectar en humo.",
    "INPUTS": [
        {"NAME": "haces", "LABEL": "Haces", "TYPE": "float", "DEFAULT": 8, "MIN": 2, "MAX": 32},
        {"NAME": "apertura", "LABEL": "Apertura", "TYPE": "float", "DEFAULT": 0.6, "MIN": 0.05, "MAX": 1},
        {"NAME": "movimiento", "LABEL": "Movimiento", "TYPE": "float", "DEFAULT": 0.5, "MIN": 0, "MAX": 1},
        {"NAME": "ritmo", "LABEL": "Ciclo", "TYPE": "long", "VALUES": [1, 2, 4, 8], "LABELS": ["1 tiempo", "2 tiempos", "1 compas", "2 compases"], "DEFAULT": 4},
        {"NAME": "grosor", "LABEL": "Grosor (px)", "TYPE": "float", "DEFAULT": 2, "MIN": 0.5, "MAX": 12},
        {"NAME": "origenX", "LABEL": "Origen X", "TYPE": "float", "DEFAULT": 0.5, "MIN": 0, "MAX": 1},
        {"NAME": "origenY", "LABEL": "Origen Y", "TYPE": "float", "DEFAULT": 0, "MIN": 0, "MAX": 1},
        {"NAME": "color", "LABEL": "Color", "TYPE": "color", "DEFAULT": [0.0, 1.0, 0.2, 1.0]}
    ]
}*/

const float TAU = 6.2831853;

void main() {
    vec2 p = gl_FragCoord.xy - vec2(origenX, origenY) * RENDERSIZE;
    float ph = VISUAL_BEATS / float(ritmo);
    float spread = apertura * 3.14159 * (1.0 - movimiento * 0.5 * (0.5 - 0.5 * cos(ph * TAU))); // opens and closes
    float sway = sin(ph * TAU * 0.5) * 0.4 * movimiento;                                     // swings side to side
    float n = floor(haces);
    float ang = atan(p.x, p.y); // 0 = straight up
    float k = clamp(floor(((ang - sway) / spread + 0.5) * (n - 1.0) + 0.5), 0.0, n - 1.0);
    float a = (k / (n - 1.0) - 0.5) * spread + sway; // the nearest beam's angle
    float d = length(p) * abs(sin(ang - a));         // pixels from that beam
    float on = (1.0 - smoothstep(grosor * 0.5, grosor * 0.5 + 1.0, d)) * step(0.0, cos(ang - a));
    gl_FragColor = vec4(color.rgb * on, 1.0);
}
