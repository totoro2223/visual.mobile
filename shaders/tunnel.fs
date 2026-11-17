/*{
  "DESCRIPTION": "Túnel sin audio propio: probá modificadores en sus parámetros",
  "ISFVSN": "2",
  "CATEGORIES": ["Generator"],
  "INPUTS": [
    { "NAME": "speed", "LABEL": "Velocidad", "TYPE": "float", "DEFAULT": 0.5, "MIN": 0.0, "MAX": 3.0 },
    { "NAME": "twist", "LABEL": "Giro", "TYPE": "float", "DEFAULT": 1.0, "MIN": 0.0, "MAX": 4.0 },
    { "NAME": "rings", "LABEL": "Anillos", "TYPE": "float", "DEFAULT": 12.0, "MIN": 2.0, "MAX": 40.0 },
    { "NAME": "hueShift", "LABEL": "Tono", "TYPE": "float", "DEFAULT": 0.0, "MIN": 0.0, "MAX": 1.0 }
  ]
}*/

void main() {
    vec2 p = (gl_FragCoord.xy - 0.5 * RENDERSIZE) / RENDERSIZE.y;
    float r = length(p) + 0.0001;
    float a = atan(p.y, p.x) / 6.2831853;
    float z = 1.0 / r + TIME * speed;
    float pat = sin(z * rings * 0.25) * sin((a + z * 0.1 * twist) * 6.2831853 * 6.0);
    vec3 col = 0.5 + 0.5 * cos(6.2831853 * (hueShift + z * 0.05 + vec3(0.0, 0.33, 0.67)));
    col *= smoothstep(0.0, 0.6, r) * (0.6 + 0.4 * sign(pat));
    gl_FragColor = vec4(col, 1.0);
}
