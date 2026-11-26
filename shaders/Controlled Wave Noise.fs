/*{
  "CREDIT": "GEN8 and DeepSeek",
  "DESCRIPTION": "Abstract geometric pattern generator with distortion and motion.",
  "CATEGORIES": ["Generator", "Abstract", "Geometric"],
  "INPUTS": [
    {
      "NAME": "patternScale",
      "TYPE": "float",
      "DEFAULT": 10.0,
      "MIN": 1.0,
      "MAX": 50.0
    },
    {
      "NAME": "distortionAmount",
      "TYPE": "float",
      "DEFAULT": 0.1,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "NAME": "speed",
      "TYPE": "float",
      "DEFAULT": 1.0,
      "MIN": 0.0,
      "MAX": 5.0
    },
    {
      "NAME": "lineThickness",
      "TYPE": "float",
      "DEFAULT": 0.02,
      "MIN": 0.01,
      "MAX": 0.1
    },
    {
      "NAME": "particleDensity",
      "TYPE": "float",
      "DEFAULT": 0.5,
      "MIN": 0.1,
      "MAX": 1.0
    },
    {
      "NAME": "enableDistortion",
      "TYPE": "bool",
      "DEFAULT": true
    },
    {
      "NAME": "enableMotion",
      "TYPE": "bool",
      "DEFAULT": true
    }
  ]
}*/

// Simplex noise function for distortion
vec3 mod289(vec3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec4 mod289(vec4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec4 permute(vec4 x) { return mod289(((x * 34.0) + 1.0) * x); }
vec4 taylorInvSqrt(vec4 r) { return 1.79284291400159 - 0.85373472095314 * r; }
float snoise(vec3 v) {
  const vec2 C = vec2(1.0 / 6.0, 1.0 / 3.0);
  vec3 i = floor(v + dot(v, C.yyy));
  vec3 x0 = v - i + dot(i, C.xxx);
  vec3 g = step(x0.yzx, x0.xyz);
  vec3 l = 1.0 - g;
  vec3 i1 = min(g.xyz, l.zxy);
  vec3 i2 = max(g.xyz, l.zxy);
  vec3 x1 = x0 - i1 + C.xxx;
  vec3 x2 = x0 - i2 + C.yyy;
  vec3 x3 = x0 - 0.5;
  i = mod289(i);
  vec4 p = permute(permute(permute(
             i.z + vec4(0.0, i1.z, i2.z, 1.0))
           + i.y + vec4(0.0, i1.y, i2.y, 1.0))
           + i.x + vec4(0.0, i1.x, i2.x, 1.0));
  vec4 j = p - 49.0 * floor(p * (1.0 / 49.0));
  vec4 x_ = floor(j * (1.0 / 7.0));
  vec4 y_ = floor(j - 7.0 * x_);
  vec4 x = (x_ * 2.0 + 0.5) / 7.0 - 1.0;
  vec4 y = (y_ * 2.0 + 0.5) / 7.0 - 1.0;
  vec4 h = 1.0 - abs(x) - abs(y);
  vec4 b0 = vec4(x.xy, y.xy);
  vec4 b1 = vec4(x.zw, y.zw);
  vec4 s0 = floor(b0) * 2.0 + 1.0;
  vec4 s1 = floor(b1) * 2.0 + 1.0;
  vec4 sh = -step(h, vec4(0.0));
  vec4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
  vec4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
  vec3 g0 = vec3(a0.xy, h.x);
  vec3 g1 = vec3(a0.zw, h.y);
  vec3 g2 = vec3(a1.xy, h.z);
  vec3 g3 = vec3(a1.zw, h.w);
  vec4 norm = taylorInvSqrt(vec4(dot(g0, g0), dot(g1, g1), dot(g2, g2), dot(g3, g3)));
  g0 *= norm.x;
  g1 *= norm.y;
  g2 *= norm.z;
  g3 *= norm.w;
  vec4 m = max(0.6 - vec4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
  m = m * m;
  return 42.0 * dot(m * m, vec4(dot(g0, x0), dot(g1, x1), dot(g2, x2), dot(g3, x3)));
}

// Main function
void main() {
  vec2 uv = gl_FragCoord.xy / RENDERSIZE.xy;
  uv.y = 1.0 - uv.y; // Flip Y for correct orientation

  // Distortion effect
  if (enableDistortion) {
    vec3 noiseCoord = vec3(uv * patternScale, TIME * speed);
    float noise = snoise(noiseCoord) * distortionAmount;
    uv += vec2(noise);
  }

  // Geometric pattern (grid)
  vec2 grid = fract(uv * patternScale);
  float lines = step(lineThickness, grid.x) * step(lineThickness, grid.y);
  float pattern = 1.0 - lines; // Invert for black lines on white background

  // Particle motion
  if (enableMotion) {
    vec3 particleCoord = vec3(uv * 10.0, TIME * speed);
    float particles = snoise(particleCoord) * particleDensity;
    pattern = min(pattern, particles);
  }

  // Output (black and white)
  gl_FragColor = vec4(vec3(pattern), 1.0);
}