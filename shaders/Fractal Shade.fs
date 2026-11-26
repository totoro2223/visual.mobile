/*{
  "DESCRIPTION": "Centered infinite fractal kaleidoscope nature pattern",
  "CREDIT": "ISF adaptation, centered fractal + kaleidoscope",
  "ISFVSN": "2.0",
  "INPUTS": [
    {
      "NAME": "segments",
      "TYPE": "float",
      "DEFAULT": 8.0,
      "MIN": 2.0,
      "MAX": 24.0
    },
    {
      "NAME": "fractalDepth",
      "TYPE": "float",
      "DEFAULT": 50.0,
      "MIN": 10.0,
      "MAX": 150.0
    }
  ]
}*/

void main() {
    vec2 uv = isf_FragNormCoord - 0.5;
    uv.x *= 1.0; // horizontal aspect, tweak if needed for editor

    // Center the coordinates and animate zoom for infinite feel
    float zoom = 1.5 + 0.5 * sin(TIME * 0.18);
    uv *= zoom;

    // Convert to polar and kaleidoscope symmetry
    float theta = atan(uv.y, uv.x);
    float radius = length(uv);
    // Symmetry mirroring
    theta = mod(theta, 6.2831853 / segments) * segments / 2.0;
    uv = vec2(cos(theta), sin(theta)) * radius;

    // Mandelbrot fractal centered at origin, infinite feel with zoom
    vec2 c = uv;
    vec2 z = vec2(0.0);
    float iterations = 0.0;
    for (int i = 0; i < 200; i++) {
        if (i > int(fractalDepth)) break;
        float x = (z.x * z.x - z.y * z.y) + c.x;
        float y = (2.0 * z.x * z.y) + c.y;
        if ((x * x + y * y) > 4.0) break;
        z.x = x;
        z.y = y;
        iterations += 1.0;
    }
    float norm = iterations / fractalDepth;

    // Nature-like coloring
    vec3 color = 0.5 + 0.5 * vec3(
        cos(2.3 * norm + 0.0) + radius,
        cos(2.3 * norm + 2.0) + (1.0 - radius),
        cos(2.3 * norm + 4.0)
    );
    color *= vec3(0.6, 0.86, 0.4);

    // Boost contrast and depth for infinite feel
    color = mix(color, vec3(0.1, 0.23, 0.07) + norm, 0.5);

    gl_FragColor = vec4(color, 1.0);
}
