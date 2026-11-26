/*{
  "DESCRIPTION": "Spinning Vortex Illusion Pattern.",
  "ISFVSN": "2",
  "INPUTS": [
    {
      "NAME": "center",
      "TYPE": "point2D",
      "LABEL": "XY Center",
      "DEFAULT": [0.5, 0.5],
      "MIN": [0.0, 0.0],
      "MAX": [1.0, 1.0]
    },
    {
      "NAME": "brightness",
      "TYPE": "float",
      "LABEL": "Brightness",
      "DEFAULT": 1.0,
      "MIN": 0.0,
      "MAX": 2.0
    },
    {
      "NAME": "noiseDensity",
      "TYPE": "float",
      "LABEL": "Noise Density",
      "DEFAULT": 10.0,
      "MIN": 0.0,
      "MAX": 18.0
    },
    {
      "NAME": "red",
      "TYPE": "float",
      "LABEL": "Illusion Red",
      "DEFAULT": 1.0,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "NAME": "green",
      "TYPE": "float",
      "LABEL": "Illusion Green",
      "DEFAULT": 1.0,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "NAME": "blue",
      "TYPE": "float",
      "LABEL": "Illusion Blue",
      "DEFAULT": 1.0,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "NAME": "rotationSpeed",
      "TYPE": "float",
      "LABEL": "Rotation Speed",
      "DEFAULT": 0.18,
      "MIN": -2.0,
      "MAX": 2.0
    }
  ]
}*/

float hash21(vec2 point) {
  point = fract(point * vec2(123.34, 456.21));
  point += dot(point, point + 45.32);
  return fract(point.x * point.y);
}

float valueNoise(vec2 point) {
  vec2 cell = floor(point);
  vec2 local = fract(point);
  local = local * local * (3.0 - 2.0 * local);

  float bottomLeft = hash21(cell);
  float bottomRight = hash21(cell + vec2(1.0, 0.0));
  float topLeft = hash21(cell + vec2(0.0, 1.0));
  float topRight = hash21(cell + vec2(1.0, 1.0));

  float bottom = mix(bottomLeft, bottomRight, local.x);
  float top = mix(topLeft, topRight, local.x);
  return mix(bottom, top, local.y);
}

void main() {
  vec2 aspect = vec2(RENDERSIZE.x / RENDERSIZE.y, 1.0);
  vec2 centered = (gl_FragCoord.xy / RENDERSIZE.xy - center) * aspect;

  float radius = length(centered);
  float angle = atan(centered.y, centered.x);
  float safeRadius = max(radius, 0.004);

  float cycleTime = mod(TIME, 20.0);
  float spinDirection = (cycleTime < 10.0) ? 1.0 : -1.0;
  float effectiveRotation = rotationSpeed * spinDirection;

  float noise = valueNoise(centered * (2.5 + noiseDensity * 0.8));
  float spiralPhase = angle * (9.0 + noiseDensity * 0.7)
                   + log(safeRadius) * (6.0 + noiseDensity * 0.4)
                   + TIME * effectiveRotation * 8.0;

  float orbital = sin(angle * 18.0 + radius * 26.0 - TIME * 1.2);
  float swirl = 0.5 + 0.5 * cos(spiralPhase + orbital * 2.2 + noise * 5.0);
  float denseBands = smoothstep(0.22, 0.92, swirl + noise * 0.4 - radius * 0.45);

  float radialRipple = 0.5 + 0.5 * sin(radius * (28.0 + noiseDensity * 1.2) - TIME * 1.6 + angle * 10.0);
  float contour = smoothstep(0.18, 0.96, denseBands * (0.6 + radialRipple * 0.8));

  float vortexGlow = 1.0 - smoothstep(0.08, 1.45, radius);
  float innerCore = smoothstep(0.005, 0.14, radius);
  float edgeFade = 1.0 - smoothstep(0.54, 1.45, radius);

  float illumination = contour * vortexGlow * innerCore * edgeFade;
  illumination += pow(max(0.0, 1.0 - radius * 1.8), 3.0) * 0.35;
  illumination = clamp(illumination, 0.0, 1.0);

  vec3 illusionColor = vec3(red, green, blue);
  vec3 base = vec3(0.003, 0.004, 0.009);
  vec3 color = mix(base, illusionColor, illumination);
  color *= brightness;
  color += illusionColor * pow(illumination, 5.0) * 0.28 * brightness;

  float vignette = 1.0 - smoothstep(0.8, 1.5, radius);
  color *= vignette;

  gl_FragColor = vec4(color, 1.0);
}
