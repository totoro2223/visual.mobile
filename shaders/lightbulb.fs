/*{
  "DESCRIPTION": "Grilla de lamparitas con halo; se prenden todas, en ola por filas o columnas, o al azar",
  "ISFVSN": "2",
  "CATEGORIES": ["Generator"],
  "INPUTS": [
    { "NAME": "columnas", "LABEL": "Columnas", "TYPE": "float", "DEFAULT": 8.0, "MIN": 1.0, "MAX": 64.0 },
    { "NAME": "filas", "LABEL": "Filas", "TYPE": "float", "DEFAULT": 6.0, "MIN": 1.0, "MAX": 64.0 },
    { "NAME": "ancho", "LABEL": "Lamparita: ancho", "TYPE": "float", "DEFAULT": 0.5, "MIN": 0.02, "MAX": 1.0 },
    { "NAME": "alto", "LABEL": "Lamparita: alto", "TYPE": "float", "DEFAULT": 0.5, "MIN": 0.02, "MAX": 1.0 },
    { "NAME": "redondez", "LABEL": "Redondez (0 = rectángulo, 1 = círculo)", "TYPE": "float", "DEFAULT": 1.0, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "glowTamano", "LABEL": "Glow: tamaño", "TYPE": "float", "DEFAULT": 0.3, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "glowIntensidad", "LABEL": "Glow: intensidad", "TYPE": "float", "DEFAULT": 0.6, "MIN": 0.0, "MAX": 2.0 },
    { "NAME": "hue", "LABEL": "Color: tono", "TYPE": "float", "DEFAULT": 0.11, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "saturation", "LABEL": "Color: saturación (0 = blanco)", "TYPE": "float", "DEFAULT": 0.55, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "brightness", "LABEL": "Color: brillo", "TYPE": "float", "DEFAULT": 1.0, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "apagadas", "LABEL": "Apagadas: brillo", "TYPE": "float", "DEFAULT": 0.06, "MIN": 0.0, "MAX": 0.3 },
    { "NAME": "modo", "LABEL": "Modo", "TYPE": "long", "DEFAULT": 1, "VALUES": [0, 1, 2, 3],
      "LABELS": ["Todas encendidas", "Ola por filas", "Ola por columnas", "Al azar"] },
    { "NAME": "encendidas", "LABEL": "Encendidas a la vez (filas o columnas; al azar: equivale a filas)", "TYPE": "float", "DEFAULT": 1.0, "MIN": 1.0, "MAX": 64.0 },
    { "NAME": "direccion", "LABEL": "Dirección", "TYPE": "long", "DEFAULT": 0, "VALUES": [0, 1, 2],
      "LABELS": ["Hacia arriba / derecha", "Hacia abajo / izquierda", "Ida y vuelta"] },
    { "NAME": "velocidad", "LABEL": "Velocidad (pasos/s, 0 = quieta)", "TYPE": "float", "DEFAULT": 4.0, "MIN": 0.0, "MAX": 30.0 },
    { "NAME": "posicion", "LABEL": "Posición de la ola (a mano o con modificador)", "TYPE": "float", "DEFAULT": 0.0, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "suavidad", "LABEL": "Suavidad (0 = de golpe, 1 = se apaga lento)", "TYPE": "float", "DEFAULT": 0.3, "MIN": 0.0, "MAX": 1.0 }
  ]
}*/

vec3 hsv2rgb(vec3 c) {
    vec3 p = abs(fract(c.xxx + vec3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return c.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), c.y);
}

float hash(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }

// how much a step ago still glows: 1 now, fading with "suavidad" (0 = off at once)
float tail(float stepsAgo) { return pow(max(suavidad, 1e-4), stepsAgo); }

// 0..1: how lit the lamp at cell id is
float level(vec2 id, vec2 grid) {
    if (modo == 0) return 1.0;
    float n = modo == 2 ? grid.x : grid.y;                 // lamps along the wave
    float h = TIME * velocidad + posicion * n;              // wave head, in steps
    float s = floor(h), f = fract(h), on = min(floor(encendidas), n);

    if (modo == 3) { // at random: each step lights a new set, the last ones fade
        float p = on / grid.y, v = 0.0;
        for (int j = 0; j < 4; j++)
            if (hash(vec3(id, s - float(j))) < p) v = max(v, j == 0 ? 1.0 : tail(float(j - 1) + f));
        return v;
    }

    float r = modo == 2 ? id.x : id.y;
    if (direccion == 1) r = n - 1.0 - r;
    float d;
    if (direccion == 2) { // back and forth: head bounces between the ends
        float period = max(2.0 * n - 2.0, 1.0), x = mod(s, period);
        float head = x < n ? x : period - x, dir = x < n - 1.0 ? 1.0 : -1.0;
        d = (head - r) * dir;
        if (d < 0.0) return 0.0;
    } else d = mod(s - r, n);
    return d < on ? 1.0 : tail(d - on + f);
}

float roundBox(vec2 p, vec2 hs, float rad) {
    vec2 q = abs(p) - hs + rad;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - rad;
}

void main() {
    vec2 grid = vec2(floor(clamp(columnas, 1.0, 64.0)), floor(clamp(filas, 1.0, 64.0)));
    vec2 cellPx = RENDERSIZE / grid;
    vec2 g = isf_FragNormCoord * grid;
    vec2 hs = vec2(ancho, alto) * 0.5 * cellPx;
    float rad = redondez * min(hs.x, hs.y);
    float glowPx = glowTamano * min(cellPx.x, cellPx.y) * 0.35 + 1e-3;
    vec3 col = hsv2rgb(vec3(hue, saturation, brightness));

    vec3 c = vec3(0.0);
    for (int j = -1; j <= 1; j++)
        for (int i = -1; i <= 1; i++) { // neighbors too: their glow spills into this cell
            vec2 id = floor(g) + vec2(float(i), float(j));
            if (id.x < 0.0 || id.y < 0.0 || id.x >= grid.x || id.y >= grid.y) continue;
            vec2 p = (g - id - 0.5) * cellPx;
            float sd = roundBox(p, hs, rad);
            float lv = level(id, grid);
            float body = 1.0 - smoothstep(-1.0, 1.0, sd);
            float core = body * lv * (1.0 - clamp(length(p / max(hs, vec2(1.0))), 0.0, 1.0)); // hot center
            c += col * body * max(lv, apagadas) + vec3(0.5) * core * brightness
               + col * lv * glowIntensidad * exp(-max(sd, 0.0) / glowPx) * (1.0 - body);
        }
    gl_FragColor = vec4(c, 1.0);
}
