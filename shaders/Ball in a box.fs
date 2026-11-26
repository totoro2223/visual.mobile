/*{
  "DESCRIPTION": "Rotating patterned room (triangle lattice, big flat polygons, chevrons, stripes) with a glass / mirror ball in the centre. Camera, patterns and rotation re-roll on beat-locked cuts.",
  "CREDIT": "Harald Walker",
  "ISFVSN": "2",
  "CATEGORIES": ["Generator"],
  "INPUTS": [
    {"NAME": "bpm", "LABEL": "BPM", "TYPE": "float", "DEFAULT": 125.0, "MIN": 60.0, "MAX": 200.0},
    {"NAME": "phase", "LABEL": "Beat offset", "TYPE": "float", "DEFAULT": 0.0, "MIN": -2.0, "MAX": 2.0},
    {"NAME": "cutEvery", "LABEL": "New cut every", "TYPE": "long", "VALUES": [0,1,2,3], "LABELS": ["1 beat","2 beats","4 beats","8 beats"], "DEFAULT": 2},
    {"NAME": "patternScale", "LABEL": "Pattern scale", "TYPE": "float", "DEFAULT": 1.5, "MIN": 0.5, "MAX": 5.0},
    {"NAME": "coverage", "LABEL": "Flat polygon coverage", "TYPE": "float", "DEFAULT": 0.5, "MIN": 0.0, "MAX": 1.0},
    {"NAME": "scrollSpeed", "LABEL": "Pattern scroll", "TYPE": "float", "DEFAULT": 0.3, "MIN": 0.0, "MAX": 3.0},
    {"NAME": "rotSpeed", "LABEL": "Room rotation", "TYPE": "float", "DEFAULT": 0.3, "MIN": 0.0, "MAX": 2.0},
    {"NAME": "stepped", "LABEL": "Stepped motion", "TYPE": "bool", "DEFAULT": false},
    {"NAME": "ballSize", "LABEL": "Ball size", "TYPE": "float", "DEFAULT": 0.9, "MIN": 0.4, "MAX": 1.4},
    {"NAME": "mirror", "LABEL": "Mirror (0 = glass)", "TYPE": "float", "DEFAULT": 0.2, "MIN": 0.0, "MAX": 1.0},
    {"NAME": "ringLines", "LABEL": "Ball ring lines", "TYPE": "float", "DEFAULT": 0.7, "MIN": 0.0, "MAX": 1.0},
    {"NAME": "stutter", "LABEL": "Stutter chance", "TYPE": "float", "DEFAULT": 0.15, "MIN": 0.0, "MAX": 1.0},
    {"NAME": "flash", "LABEL": "Beat flash", "TYPE": "float", "DEFAULT": 0.3, "MIN": 0.0, "MAX": 1.0},
    {"NAME": "invertOff", "LABEL": "Offbeat invert", "TYPE": "float", "DEFAULT": 0.3, "MIN": 0.0, "MAX": 1.0},
    {"NAME": "kick", "LABEL": "Kick (map to audio)", "TYPE": "float", "DEFAULT": 0.0, "MIN": 0.0, "MAX": 1.0},
    {"NAME": "hueShift", "LABEL": "Hue shift", "TYPE": "float", "DEFAULT": 0.0, "MIN": 0.0, "MAX": 1.0},
    {"NAME": "pixelSize", "LABEL": "Pixel size", "TYPE": "float", "DEFAULT": 2.0, "MIN": 1.0, "MAX": 32.0}
  ]
}*/

/*
 * Copyright (c) Harald Walker / Sonic Walker
 * https://github.com/HaraldWalker/sonicwalker-shaders
 *
 * Licensed under CC BY-NC-SA 4.0
 * https://creativecommons.org/licenses/by-nc-sa/4.0/
 *
 * You may use the visual output of this shader freely.
 * If you profit from it, consider supporting the artist:
 * https://www.sonicwalker.com
 */
 
const vec3 NAVY = vec3(0.02, 0.0, 0.22);
const vec3 BLUE = vec3(0.0, 0.0, 0.72);
const vec3 MAG  = vec3(0.92, 0.12, 0.97);
const vec3 LIME = vec3(0.55, 0.95, 0.2);
const vec3 PINK = vec3(0.95, 0.72, 0.9);
const float ROOM = 4.0;

// shared per-frame state
float gA = 0.0;
float gB = 0.0;
float gSeed = 0.0;
float gScale = 1.0;
float gScroll = 0.0;

// ---------- helpers ----------
float hash11(float p) {
  p = fract(p * 0.1031);
  p *= p + 33.33;
  p *= p + p;
  return fract(p);
}

float hash21(vec2 p) {
  vec3 p3 = fract(vec3(p.xyx) * 0.1031);
  p3 += dot(p3, p3.yzx + 33.33);
  return fract((p3.x + p3.y) * p3.z);
}

vec2 rot2(vec2 p, float a) {
  float c = cos(a), s = sin(a);
  return vec2(c * p.x - s * p.y, s * p.x + c * p.y);
}

vec3 rotY(vec3 v, float a) {
  float c = cos(a), s = sin(a);
  return vec3(c*v.x + s*v.z, v.y, -s*v.x + c*v.z);
}

vec3 rotX(vec3 v, float a) {
  float c = cos(a), s = sin(a);
  return vec3(v.x, c*v.y - s*v.z, s*v.y + c*v.z);
}

float segDist(vec2 p, vec2 a, vec2 b) {
  vec2 pa = p - a, ba = b - a;
  float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
  return length(pa - ba * h);
}

vec3 rgb2hsv(vec3 c) {
  vec4 K = vec4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
  vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
  vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
  float d = q.x - min(q.w, q.y);
  float e = 1e-10;
  return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

vec3 hsv2rgb(vec3 c) {
  vec3 p = abs(fract(c.xxx + vec3(1.0, 2.0/3.0, 1.0/3.0)) * 6.0 - 3.0);
  return c.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), c.y);
}

// white "7" glyphs on navy
vec3 chev(vec2 uv, float ang, float sd) {
  vec2 cq = rot2(uv, ang) * gScale * 3.0;
  vec2 cc = floor(cq);
  vec2 cf = fract(cq);
  float bar = step(0.1, cf.x) * step(cf.x, 0.9) * step(0.62, cf.y) * step(cf.y, 0.72);
  float diag = step(segDist(cf, vec2(0.88, 0.67), vec2(0.45, 0.08)), 0.05);
  float on = step(0.25, hash21(cc + sd));
  return mix(NAVY, vec3(1.0), max(bar, diag) * on);
}

// one room face; uv in [-1,1]
vec3 facePattern(vec2 uv, float id) {
  float hS = hash11(gSeed * 3.7 + id * 11.3 + 0.5);
  float ang = floor(hash11(hS * 29.0 + 2.0) * 8.0) / 8.0 * 3.14159;
  float ang2 = floor(hash11(hS * 41.0 + 3.0) * 8.0) / 8.0 * 3.14159;
  float sg = (hash11(hS * 13.0) > 0.5) ? 1.0 : -1.0;
  vec3 base;

  if (hash11(hS * 17.0 + 1.0) > 0.5) {
    // triangle lattice: solid lines, dotted lines, node dots
    vec2 q = rot2(uv, ang2) * gScale * 2.5 + vec2(gScroll * sg * 0.5, 0.0);
    float b = q.y / 0.866;
    float a = q.x - 0.5 * b;
    float la = abs(fract(a + 0.5) - 0.5);
    float lb = abs(fract(b + 0.5) - 0.5);
    float lc = abs(fract(a + b + 0.5) - 0.5);
    float solid = step(min(min(la, lb), lc), 0.045);
    float da = step(abs(fract(a) - 0.5), 0.012) * step(0.5, fract(b * 5.0));
    float db = step(abs(fract(b) - 0.5), 0.012) * step(0.5, fract(a * 5.0));
    float dc = step(abs(fract(a + b) - 0.5), 0.012) * step(0.5, fract(a * 5.0));
    vec2 nd = vec2(a - floor(a + 0.5), b - floor(b + 0.5));
    float node = step(length(nd), 0.08);
    base = BLUE;
    base = mix(base, vec3(0.85, 0.8, 1.0), max(max(da, db), dc));
    base = mix(base, vec3(1.0), max(solid, node));
  } else {
    base = chev(uv, ang2, hS * 50.0);
  }

  // big flat polygons on top (full cells or diagonal halves)
  vec2 bq = rot2(uv, ang) * gScale * 0.8 + vec2(gScroll * sg * 0.3, 0.0);
  vec2 bc = floor(bq);
  vec2 bf = fract(bq);
  float h1 = hash21(bc + hS * 37.0 + gSeed);
  if (h1 < coverage) {
    float h2 = hash21(bc * 1.7 + hS * 11.0);
    float shape = 1.0;
    if (h2 > 0.66) shape = step(bf.y, bf.x);
    else if (h2 > 0.33) shape = step(bf.x + bf.y, 1.0);
    float cs = hash21(bc * 3.1 + hS * 5.0);
    vec3 pc;
    if (cs < 0.3) pc = MAG;
    else if (cs < 0.55) pc = LIME;
    else if (cs < 0.75) pc = chev(uv, ang2, hS * 50.0 + 7.0);
    else if (cs < 0.88) pc = PINK;
    else pc = vec3(step(0.5, fract(bq.y * 3.0)));
    base = mix(base, pc, shape);
  }
  return base;
}

// trace the (rotating) room from inside
vec3 roomColor(vec3 ro, vec3 rd) {
  vec3 lro = rotX(rotY(ro, -gA), -gB);
  vec3 lrd = rotX(rotY(rd, -gA), -gB);
  vec3 inv = 1.0 / (lrd + vec3(1e-6));
  vec3 t1 = (-vec3(ROOM) - lro) * inv;
  vec3 t2 = (vec3(ROOM) - lro) * inv;
  vec3 tmax = max(t1, t2);
  float tF = min(min(tmax.x, tmax.y), tmax.z);
  vec3 m = step(tmax.xyz, tmax.yzx) * step(tmax.xyz, tmax.zxy);
  vec3 hp = lro + lrd * tF;
  float sgn = step(0.0, dot(lrd, m));
  float id = (m.y + 2.0 * m.z) * 2.0 + sgn;
  vec2 uv;
  if (m.x > 0.5) uv = hp.yz;
  else if (m.y > 0.5) uv = hp.xz;
  else uv = hp.xy;
  return facePattern(uv / ROOM, id);
}

void main() {
  vec2 fc = isf_FragNormCoord * RENDERSIZE;
  float px = max(pixelSize, 1.0);
  vec2 sc = (floor(fc / px) + 0.5) * px;
  vec2 p = (sc - 0.5 * RENDERSIZE) / RENDERSIZE.y;

  // timing
  float beatPos = TIME * bpm / 60.0 + phase;
  float f = fract(beatPos);
  float cutLen = pow(2.0, float(cutEvery));
  float seed = floor(beatPos / cutLen);

  float r1 = hash11(seed * 1.31 + 1.0);
  float r2 = hash11(seed * 2.17 + 2.0);
  float r3 = hash11(seed * 3.49 + 3.0);
  float r4 = hash11(seed * 4.73 + 4.0);
  float r5 = hash11(seed * 5.91 + 5.0);
  float r6 = hash11(seed * 7.07 + 6.0);

  float tAnim = mix(beatPos, floor(beatPos), step(hash11(floor(beatPos) * 1.7 + 3.0), stutter));
  float tRot = stepped ? floor(tAnim * 4.0) / 4.0 : tAnim;
  float tScr = stepped ? floor(tAnim * 8.0) / 8.0 : tAnim;
  float dir = (r4 > 0.5) ? 1.0 : -1.0;

  gSeed = seed;
  gScale = patternScale;
  gScroll = tScr * scrollSpeed;
  gA = tRot * rotSpeed * dir + r5 * 6.2831;
  gB = gA * 0.6 + r6 * 6.2831;

  // camera (stepped angles per cut), always inside the room
  float yaw = (floor(r1 * 8.0) / 8.0 - 0.5) * 6.2831;
  float pitch = (floor(r2 * 5.0) / 5.0 - 0.4) * 1.2;
  float camD = 2.4 + r3 * 1.2;
  vec3 ro = rotY(rotX(vec3(0.0, 0.0, -camD), pitch), yaw);
  vec3 rd = normalize(vec3(p, 0.8));
  rd = rotY(rotX(rd, pitch), yaw);

  // ball
  float R = ballSize;
  float bA = tRot * 0.4 * dir + r6 * 6.2831;
  float bB = bA * 0.7;
  vec3 L1 = normalize(vec3(-0.5, 0.7, -0.6));
  vec3 L2 = normalize(vec3(0.6, -0.3, -0.7));

  vec3 col;
  float bq = dot(ro, rd);
  float cq = dot(ro, ro) - R * R;
  float disc = bq * bq - cq;
  float ts = -bq - sqrt(max(disc, 0.0));

  if (disc > 0.0 && ts > 0.0) {
    vec3 hp = ro + rd * ts;
    vec3 n = hp / R;
    vec3 refl = reflect(rd, n);
    vec3 cr = roomColor(hp, refl);

    // glass: refract in, find exit point, refract out
    vec3 r1v = refract(rd, n, 1.0 / 1.45);
    vec3 tr;
    if (dot(r1v, r1v) < 0.001) {
      tr = cr;
    } else {
      float te = -2.0 * dot(hp, r1v);
      vec3 ex = hp + r1v * te;
      vec3 r2v = refract(r1v, -ex / R, 1.45);
      if (dot(r2v, r2v) < 0.001) r2v = reflect(r1v, -ex / R);
      tr = roomColor(ex, r2v);
    }

    float ndv = max(dot(-rd, n), 0.0);
    float fr = 0.04 + 0.96 * pow(1.0 - ndv, 5.0);
    col = mix(tr * 0.92, cr, clamp(fr + mirror, 0.0, 1.0));

    // white ring lines on the ball
    vec3 ln = rotX(rotY(n, -bA), -bB);
    float ring = 0.0;
    ring = max(ring, step(abs(dot(ln, vec3(0.0, 1.0, 0.0))), 0.025));
    ring = max(ring, step(abs(dot(ln, vec3(0.866, 0.0, 0.5))), 0.025));
    ring = max(ring, step(abs(dot(ln, vec3(-0.866, 0.0, 0.5)) - 0.45), 0.02));
    col = mix(col, vec3(1.0), ring * ringLines);

    // sparkle highlights and rim
    float sp = pow(max(dot(refl, L1), 0.0), 150.0) + pow(max(dot(refl, L2), 0.0), 150.0);
    col += vec3(sp);
    col = mix(col, vec3(1.0), pow(1.0 - ndv, 6.0) * 0.6);
  } else {
    col = roomColor(ro, rd);
  }

  // offbeat invert, beat flash, external kick
  float odd = mod(floor(beatPos * 2.0), 2.0);
  float envOff = (1.0 - smoothstep(0.0, 0.12, fract(beatPos * 2.0))) * odd;
  col = mix(col, vec3(1.0) - col, invertOff * envOff);
  float envKick = 1.0 - smoothstep(0.0, 0.2, f);
  col = mix(col, vec3(1.0), clamp(flash * envKick + kick, 0.0, 1.0));

  if (hueShift > 0.001) {
    vec3 hsv = rgb2hsv(clamp(col, 0.0, 1.0));
    hsv.x = fract(hsv.x + hueShift);
    col = hsv2rgb(hsv);
  }

  gl_FragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}
