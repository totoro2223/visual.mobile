/*{
  "DESCRIPTION": "Circulo que aparece creciendo desde un punto de origen hasta su posicion, con copias, movimiento aleatorio y espejo",
  "ISFVSN": "2",
  "CATEGORIES": ["Generator"],
  "INPUTS": [
    { "NAME": "hue", "LABEL": "Color: tono", "TYPE": "float", "DEFAULT": 0.0, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "saturation", "LABEL": "Color: saturación (0 = blanco)", "TYPE": "float", "DEFAULT": 0.0, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "brightness", "LABEL": "Color: brillo", "TYPE": "float", "DEFAULT": 1.0, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "origenX", "LABEL": "Origen X (desde dónde aparece)", "TYPE": "float", "DEFAULT": 0.5, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "origenY", "LABEL": "Origen Y (desde dónde aparece)", "TYPE": "float", "DEFAULT": 0.5, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "appear", "LABEL": "Duración de la aparición (s)", "TYPE": "float", "DEFAULT": 1.0, "MIN": 0.0, "MAX": 10.0 },
    { "NAME": "tamano", "LABEL": "Tamaño", "TYPE": "float", "DEFAULT": 0.15, "MIN": 0.0, "MAX": 0.5 },
    { "NAME": "posX", "LABEL": "Posición X", "TYPE": "float", "DEFAULT": 0.5, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "posY", "LABEL": "Posición Y", "TYPE": "float", "DEFAULT": 0.5, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "cantidad", "LABEL": "Cantidad", "TYPE": "float", "DEFAULT": 1.0, "MIN": 1.0, "MAX": 32.0 },
    { "NAME": "randomMovement", "LABEL": "Movimiento aleatorio", "TYPE": "float", "DEFAULT": 0.0, "MIN": 0.0, "MAX": 0.5 },
    { "NAME": "splitMirror", "LABEL": "Split mirror (0 = sin espejo)", "TYPE": "float", "DEFAULT": 0.0, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "quantityMirror", "LABEL": "Quantity mirror (ejes)", "TYPE": "float", "DEFAULT": 1.0, "MIN": 1.0, "MAX": 12.0 }
  ]
}*/

const float PI = 3.14159265;

vec3 hsv2rgb(vec3 c) {
    vec3 p = abs(fract(c.xxx + vec3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return c.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), c.y);
}

float hash(float n) { return fract(sin(n * 127.1) * 43758.5453); }

// folds p into the first wedge of a mirror with n axes through the screen center (n = 1: left/right split)
vec2 fold(vec2 p, float n) {
    float w = PI / n;
    float a = abs(mod(atan(p.x, p.y) + w, 2.0 * w) - w);
    return length(p) * vec2(sin(a), cos(a));
}

void main() {
    float aspect = RENDERSIZE.x / RENDERSIZE.y;
    vec2 sc = vec2(aspect, 1.0);
    vec2 p = (isf_FragNormCoord - 0.5) * sc;
    vec2 fp = fold(p, max(1.0, floor(quantityMirror)));

    // appear: grows from 0 and travels from the origin to its position
    float k = appear > 0.0 ? smoothstep(0.0, 1.0, TIME / appear) : 1.0;
    vec2 base = (mix(vec2(origenX, origenY), vec2(posX, posY), k) - 0.5) * sc;
    float r = tamano * k;
    float aa = 1.5 / RENDERSIZE.y;

    int n = int(floor(cantidad));
    float v = 0.0;
    for (int i = 0; i < 32; i++) {
        if (i >= n) break;
        float s = float(i) + 1.0;
        // smooth wander from a few sines with random speeds and phases per circle
        vec2 wander = vec2(
            sin(TIME * (0.3 + hash(s) * 0.7) + hash(s + 1.0) * 6.2832) + 0.5 * sin(TIME * (0.7 + hash(s + 2.0)) + hash(s + 3.0) * 6.2832),
            sin(TIME * (0.3 + hash(s + 4.0) * 0.7) + hash(s + 5.0) * 6.2832) + 0.5 * sin(TIME * (0.7 + hash(s + 6.0)) + hash(s + 7.0) * 6.2832)
        ) / 1.5;
        vec2 c = base + wander * randomMovement;
        v = max(v, 1.0 - smoothstep(r - aa, r + aa, length(p - c)));
        // mirrored copies: folding both the pixel and the center lands every reflection of c on the same spot
        v = max(v, splitMirror * (1.0 - smoothstep(r - aa, r + aa, length(fp - fold(c, max(1.0, floor(quantityMirror)))))));
    }

    vec3 col = hsv2rgb(vec3(hue, saturation, brightness));
    gl_FragColor = vec4(col * v, 1.0);
}
