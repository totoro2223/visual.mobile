/*
{
  "CREDIT": "ReadyMapper",
  "DESCRIPTION": "Wave3 v3 — Proff audioreaktiv 3D wireframe terreng. Ingen fwidth/derivatives. Anti-moire via RENDERSIZE pikselestimering og perspektiv-LOD. Filmic tonemapping, Gaussisk vignett, organisk fBm terreng med Newton-raffinert ray-intersection.",
  "CATEGORIES": [
    "Generator",
    "ReadyMapper"
  ],
  "INPUTS": [
    {
      "NAME": "speed",
      "TYPE": "float",
      "DEFAULT": 0.35,
      "MIN": 0.0,
      "MAX": 3.0
    },
    {
      "NAME": "intensity",
      "TYPE": "float",
      "DEFAULT": 0.75,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "NAME": "pulse",
      "TYPE": "float",
      "DEFAULT": 0.0,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "NAME": "amplitude",
      "TYPE": "float",
      "DEFAULT": 0.6,
      "MIN": 0.0,
      "MAX": 2.0
    },
    {
      "NAME": "ridgeWidth",
      "TYPE": "float",
      "DEFAULT": 0.8,
      "MIN": 0.1,
      "MAX": 4.0
    },
    {
      "NAME": "gridH",
      "TYPE": "float",
      "DEFAULT": 12.0,
      "MIN": 2.0,
      "MAX": 36.0
    },
    {
      "NAME": "gridV",
      "TYPE": "float",
      "DEFAULT": 6.0,
      "MIN": 1.0,
      "MAX": 18.0
    },
    {
      "NAME": "lineWeight",
      "TYPE": "float",
      "DEFAULT": 2.2,
      "MIN": 0.5,
      "MAX": 6.0
    },
    {
      "NAME": "scrollSpeed",
      "TYPE": "float",
      "DEFAULT": 0.55,
      "MIN": 0.0,
      "MAX": 4.0
    },
    {
      "NAME": "barHeight",
      "TYPE": "float",
      "DEFAULT": 0.7,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "NAME": "fogAmount",
      "TYPE": "float",
      "DEFAULT": 0.45,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "NAME": "camTilt",
      "TYPE": "float",
      "DEFAULT": 0.5,
      "MIN": 0.1,
      "MAX": 1.2
    },
    {
      "NAME": "glowAmount",
      "TYPE": "float",
      "DEFAULT": 0.65,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "NAME": "colorBase",
      "TYPE": "color",
      "DEFAULT": [ 0.88, 0.94, 1.00, 1.0 ]
    },
    {
      "NAME": "colorMid",
      "TYPE": "color",
      "DEFAULT": [ 1.00, 0.38, 0.04, 1.0 ]
    },
    {
      "NAME": "colorTop",
      "TYPE": "color",
      "DEFAULT": [ 0.90, 0.04, 0.08, 1.0 ]
    },
    {
      "NAME": "colorFog",
      "TYPE": "color",
      "DEFAULT": [ 0.03, 0.03, 0.05, 1.0 ]
    }
  ]
}
*/

// ================================================================
// THE SHORE v2  —  ANTI-MOIRE UTEN fwidth()
//
// PIKSEL-AA STRATEGI:
//   pixW = 1.0 / RENDERSIZE.x  (én piksel i UV-rom)
//   pixH = 1.0 / RENDERSIZE.y
//
//   Perspektiv-skalering ved ray hit-avstand tSurf:
//   worldPxZ = pixH * tSurf / abs(rd.y)   (dybde-akse)
//   worldPxX = pixW * tSurf * aspect       (bredde-akse)
//
//   Linje-halvbredde hw = max(worldPx * gridFreq * lineWeight, 0.004)
//   LOD-fade: smoothstep når worldPx * freq > 0.4 → for tett → ut
//   Glow: exp(-dist/hw) — ingen periodisitet, null moire
// ================================================================

float aaLine(float p, float hw) {
    return smoothstep(hw, hw * 0.05, min(p, 1.0 - p));
}

float lodFade(float worldPx, float freq) {
    return clamp(1.0 - smoothstep(0.35, 1.1, worldPx * freq), 0.0, 1.0);
}

// --- Terreng ---
float v21(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float h00 = fract(sin(dot(i,             vec2(127.1, 311.7))) * 43758.5);
    float h10 = fract(sin(dot(i + vec2(1,0), vec2(127.1, 311.7))) * 43758.5);
    float h01 = fract(sin(dot(i + vec2(0,1), vec2(127.1, 311.7))) * 43758.5);
    float h11 = fract(sin(dot(i + vec2(1,1), vec2(127.1, 311.7))) * 43758.5);
    return mix(mix(h00, h10, f.x), mix(h01, h11, f.x), f.y);
}

float fbm(vec2 p) {
    float v = 0.0;
    v += v21(p) * 0.5000; p = p * 2.03 + 1.7;
    v += v21(p) * 0.2500; p = p * 1.97 + 2.3;
    v += v21(p) * 0.1250; p = p * 2.01 + 1.1;
    v += v21(p) * 0.0625;
    return v;
}

float terrainH(float wx, float wz) {
    float ridge = exp(-wx * wx * ridgeWidth);
    float base  = fbm(vec2(wx * 0.55, wz * 0.50));
    float fine  = fbm(vec2(wx * 1.80, wz * 1.60)) * 0.22;
    return ridge * amplitude * (base * 0.78 + fine);
}

void main() {
    vec2  uv     = isf_FragNormCoord;
    float aspect = RENDERSIZE.x / RENDERSIZE.y;
    float t      = TIME * speed;

    float pixW = 1.0 / RENDERSIZE.x;
    float pixH = 1.0 / RENDERSIZE.y;

    // --- Kamera --- lav vinkel fremover, ikke fugleperspektiv
    float camH = 0.28 + camTilt * 0.18 + pulse * 0.04;
    vec3  ro   = vec3(0.0, camH, -2.2);
    vec2  ndc  = (uv - 0.5) * vec2(aspect, -1.0);
    float tilt = 0.30 + camTilt * 0.12;
    vec3  rd   = normalize(vec3(ndc.x * 0.9, ndc.y * 0.75 - tilt, 1.0));

    // --- Ray-plane interseksjon ---
    float rdySafe  = min(rd.y, -0.0006);
    float onGround = step(rd.y, -0.0006);
    float tFlat    = max(-ro.y / rdySafe, 0.001);
    vec3  p0       = ro + tFlat * rd;

    // --- Newton-steg ---
    float h0    = terrainH(p0.x, p0.z + t * scrollSpeed);
    float tSurf = max(-(ro.y - h0) / rdySafe, 0.001);
    vec3  pS    = ro + tSurf * rd;
    float wz    = pS.z + t * scrollSpeed;
    float wx    = pS.x;
    float hS    = terrainH(wx, wz);

    // --- Perspektiv pikselstørrelse i verdensrom ---
    float perpS  = tSurf / max(abs(rdySafe), 0.001);
    float wpxZ   = pixH * perpS * 1.6;
    float wpxX   = pixW * perpS * aspect;

    // --- Fog ---
    float fogF = exp(-max(tSurf - 0.4, 0.0) * fogAmount * 1.8) * onGround;

    // --- Høyde-farge ---
    float hNorm    = clamp(hS / max(amplitude * 0.9, 0.001), 0.0, 1.0);
    vec3  surfColor = mix(colorBase.rgb, colorMid.rgb, smoothstep(0.10, 0.55, hNorm));
    surfColor       = mix(surfColor, colorTop.rgb, smoothstep(0.50, 1.00, hNorm));

    // --- Grid faser ---
    float phZ = fract(wz * gridH);
    float phX = fract(wx * gridV);

    float hwZ = max(wpxZ * gridH * lineWeight, 0.004);
    float hwX = max(wpxX * gridV * lineWeight, 0.004);

    float lodZ = lodFade(wpxZ, gridH);
    float lodX = lodFade(wpxX, gridV);

    float lineZ = aaLine(phZ, hwZ) * lodZ;
    float lineX = aaLine(phX, hwX) * lodX;
    float wire  = max(lineZ, lineX) * fogF;

    // --- Glow (eksponentiell, null periodisitet) ---
    float glowZ = exp(-min(phZ, 1.0-phZ) / max(hwZ * 3.5, 0.001)) * lodZ * 0.12 * glowAmount;
    float glowX = exp(-min(phX, 1.0-phX) / max(hwX * 3.5, 0.001)) * lodX * 0.15 * glowAmount;

    vec3 terrainCol  = surfColor * wire * (0.4 + intensity * 0.6);
    terrainCol      += surfColor * (glowZ + glowX) * fogF * intensity;

    // ============================================================
    // VERTIKALE SØYLER
    // ============================================================
    float horizY = clamp(0.5 + tilt / 0.75 * 0.35, 0.42, 0.72);
    float bDepth = 1.1 + pulse * 0.12;
    float bWX    = rd.x / rd.z * bDepth;
    float bWZ    = (-ro.z + bDepth) + t * scrollSpeed;
    float bH     = terrainH(bWX, bWZ) * barHeight;
    float bScrH  = bH * 0.62;
    float barTop = horizY - bScrH;
    float inBar  = step(barTop, uv.y) * step(uv.y, horizY + 0.005);

    float phXb  = fract(bWX * gridV);
    float hwXb  = max(pixW * bDepth * aspect * gridV * lineWeight * 1.4, 0.005);
    float lodXb = lodFade(pixW * bDepth * aspect, gridV);
    float barLine = aaLine(phXb, hwXb) * lodXb * inBar;

    float barT   = clamp((horizY - uv.y) / max(bScrH, 0.001), 0.0, 1.0);
    vec3  barHue = mix(colorBase.rgb, colorMid.rgb, barT * 0.8);
    barHue       = mix(barHue, colorTop.rgb, barT * barT);
    float barLum = exp(-barT * 1.8 * (1.0 - intensity * 0.5)) + 0.08;

    vec3  barsCol = barHue * barLine * barLum * (0.5 + intensity * 0.5);
    float bGlow   = exp(-min(phXb, 1.0-phXb) / max(hwXb * 4.0, 0.001))
                  * lodXb * inBar * 0.18 * glowAmount;
    barsCol += barHue * bGlow * barLum * intensity;

    // Horisont kjerne-glow
    float hGlow = exp(-abs(uv.y - horizY) * 22.0)
                * exp(-abs(uv.x - 0.5) * aspect * 3.5)
                * 0.28 * intensity * (0.6 + pulse * 0.4);
    barsCol += mix(colorBase.rgb * 0.7, colorMid.rgb, 0.4) * hGlow;

    // ============================================================
    // KOMPOSISJON
    // ============================================================
    vec3 col = terrainCol + barsCol;

    col += mix(colorMid.rgb, colorBase.rgb, 0.7)
         * exp(-abs(uv.y - horizY) * 12.0) * fogAmount * 0.05;

    col = mix(colorFog.rgb * 0.4, col, clamp(fogF * 1.15, 0.0, 1.0));

    float vx   = (uv.x - 0.5) * 2.0;
    float vy   = (uv.y - 0.5) * 2.0;
    col       *= 1.0 - smoothstep(0.55, 1.35, sqrt(vx*vx*0.7 + vy*vy));

    col += colorBase.rgb * exp(-(vx*vx + vy*vy) * 2.8) * pulse * intensity * 0.35;

    col = clamp(col, 0.0, 1.0);

    // Filmic ACES-approksimert tonemapping
    col = col * (2.51 * col + 0.03) / (col * (2.43 * col + 0.59) + 0.14);
    col = clamp(col, 0.0, 1.0);

    gl_FragColor = vec4(col, 1.0);
}