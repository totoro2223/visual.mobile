/*{
  "DESCRIPTION": "ISF techno industrial con grid maquínico, ritmo interno BPM, strobes secos, links en movimiento y energía de club.",
  "CREDIT": "Rebel Glitched Beast",
  "ISFVSN": "2.0",
  "CATEGORIES": [ "Generator", "Techno", "Industrial", "Machine", "VJ" ],
  "INPUTS": [
    { "NAME": "tempo",             "TYPE": "float", "DEFAULT": 140.0, "MIN": 90.0,  "MAX": 180.0 },
    { "NAME": "masterSpeed",       "TYPE": "float", "DEFAULT": 1.0,   "MIN": 0.0,   "MAX": 3.0 },

    { "NAME": "gridScale",         "TYPE": "float", "DEFAULT": 10.0,  "MIN": 3.0,   "MAX": 30.0 },
    { "NAME": "gridHardness",      "TYPE": "float", "DEFAULT": 0.88,  "MIN": 0.0,   "MAX": 1.0 },
    { "NAME": "rotation",          "TYPE": "float", "DEFAULT": 0.0,   "MIN": -1.0,  "MAX": 1.0 },

    { "NAME": "domainWarp",        "TYPE": "float", "DEFAULT": 0.06,  "MIN": 0.0,   "MAX": 0.5 },
    { "NAME": "warpQuantize",      "TYPE": "float", "DEFAULT": 0.85,  "MIN": 0.0,   "MAX": 1.0 },

    { "NAME": "driftSpeed",        "TYPE": "float", "DEFAULT": 0.45,  "MIN": 0.0,   "MAX": 4.0 },
    { "NAME": "moveUp",            "TYPE": "float", "DEFAULT": 0.0,   "MIN": 0.0,   "MAX": 1.0 },
    { "NAME": "moveDown",          "TYPE": "float", "DEFAULT": 0.0,   "MIN": 0.0,   "MAX": 1.0 },
    { "NAME": "moveLeft",          "TYPE": "float", "DEFAULT": 0.0,   "MIN": 0.0,   "MAX": 1.0 },
    { "NAME": "moveRight",         "TYPE": "float", "DEFAULT": 1.0,   "MIN": 0.0,   "MAX": 1.0 },
    { "NAME": "directionMode",     "TYPE": "long",  "DEFAULT": 0,     "VALUES": [0,1], "LABELS": ["Manual","Auto State"] },
    { "NAME": "stateRate",         "TYPE": "float", "DEFAULT": 0.5,   "MIN": 0.1,   "MAX": 4.0 },

    { "NAME": "busWidth",          "TYPE": "float", "DEFAULT": 0.050, "MIN": 0.004, "MAX": 0.20 },
    { "NAME": "busSoftness",       "TYPE": "float", "DEFAULT": 0.035, "MIN": 0.001, "MAX": 0.20 },
    { "NAME": "crossWidth",        "TYPE": "float", "DEFAULT": 0.040, "MIN": 0.004, "MAX": 0.20 },
    { "NAME": "crossSoftness",     "TYPE": "float", "DEFAULT": 0.030, "MIN": 0.001, "MAX": 0.20 },

    { "NAME": "nodeSize",          "TYPE": "float", "DEFAULT": 0.065, "MIN": 0.01,  "MAX": 0.30 },
    { "NAME": "nodeDensity",       "TYPE": "float", "DEFAULT": 0.82,  "MIN": 0.0,   "MAX": 1.0 },
    { "NAME": "nodeGlow",          "TYPE": "float", "DEFAULT": 0.90,  "MIN": 0.0,   "MAX": 3.0 },

    { "NAME": "linkAmount",        "TYPE": "float", "DEFAULT": 1.00,  "MIN": 0.0,   "MAX": 2.5 },
    { "NAME": "traceMotion",       "TYPE": "float", "DEFAULT": 1.30,  "MIN": 0.0,   "MAX": 4.0 },
    { "NAME": "traceDensity",      "TYPE": "float", "DEFAULT": 0.75,  "MIN": 0.0,   "MAX": 2.0 },
    { "NAME": "traceSharpness",    "TYPE": "float", "DEFAULT": 1.70,  "MIN": 0.2,   "MAX": 3.0 },
    { "NAME": "traceGlow",         "TYPE": "float", "DEFAULT": 1.10,  "MIN": 0.0,   "MAX": 3.0 },

    { "NAME": "pulseAmount",       "TYPE": "float", "DEFAULT": 1.20,  "MIN": 0.0,   "MAX": 3.0 },
    { "NAME": "pulseSpeed",        "TYPE": "float", "DEFAULT": 1.20,  "MIN": 0.0,   "MAX": 4.0 },
    { "NAME": "pulseWidth",        "TYPE": "float", "DEFAULT": 0.055, "MIN": 0.005, "MAX": 0.25 },

    { "NAME": "rhythmInfluence",   "TYPE": "float", "DEFAULT": 0.85,  "MIN": 0.0,   "MAX": 2.0 },
    { "NAME": "beatDecay",         "TYPE": "float", "DEFAULT": 0.12,  "MIN": 0.0,   "MAX": 0.5 },
    { "NAME": "strobeDensity",     "TYPE": "float", "DEFAULT": 0.70,  "MIN": 0.0,   "MAX": 1.0 },

    { "NAME": "logicAmount",       "TYPE": "float", "DEFAULT": 0.70,  "MIN": 0.0,   "MAX": 2.0 },
    { "NAME": "logicScale",        "TYPE": "float", "DEFAULT": 1.10,  "MIN": 0.2,   "MAX": 3.0 },
    { "NAME": "microNoise",        "TYPE": "float", "DEFAULT": 0.22,  "MIN": 0.0,   "MAX": 2.0 },

    { "NAME": "glitchAmount",      "TYPE": "float", "DEFAULT": 0.35,  "MIN": 0.0,   "MAX": 1.5 },
    { "NAME": "glitchRate",        "TYPE": "float", "DEFAULT": 1.0,   "MIN": 0.1,   "MAX": 8.0 },
    { "NAME": "parallax",          "TYPE": "float", "DEFAULT": 0.12,  "MIN": 0.0,   "MAX": 1.5 },

    { "NAME": "scanlines",         "TYPE": "float", "DEFAULT": 0.35,  "MIN": 0.0,   "MAX": 1.0 },
    { "NAME": "crush",             "TYPE": "float", "DEFAULT": 0.18,  "MIN": 0.0,   "MAX": 1.0 },
    { "NAME": "brightness",        "TYPE": "float", "DEFAULT": 1.0,   "MIN": 0.0,   "MAX": 2.0 },
    { "NAME": "contrast",          "TYPE": "float", "DEFAULT": 1.35,  "MIN": 0.5,   "MAX": 3.0 },
    { "NAME": "saturation",        "TYPE": "float", "DEFAULT": 1.10,  "MIN": 0.0,   "MAX": 2.0 },
    { "NAME": "grain",             "TYPE": "float", "DEFAULT": 0.010, "MIN": 0.0,   "MAX": 0.08 },
    { "NAME": "vignette",          "TYPE": "float", "DEFAULT": 0.10,  "MIN": 0.0,   "MAX": 1.0 },
    { "NAME": "invert",            "TYPE": "bool",  "DEFAULT": false },

    { "NAME": "paletteMode",       "TYPE": "long",  "DEFAULT": 0, "VALUES": [0,1,2], "LABELS": ["Red/Black","Amber/Blue","Monochrome"] }
  ]
}*/

#define PI 3.14159265359

mat2 rot(float a){
    float s = sin(a);
    float c = cos(a);
    return mat2(c,-s,s,c);
}

float hash11(float p){
    p = fract(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return fract(p);
}

float hash21(vec2 p){
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

vec2 hash22(vec2 p){
    float n = hash21(p);
    return vec2(n, hash11(n + 17.17));
}

vec3 satAdjust(vec3 c, float s){
    float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
    return mix(vec3(l), c, s);
}

vec3 contrastAdjust(vec3 c, float k){
    return (c - 0.5) * k + 0.5;
}

float hardLine(float x, float w, float s, float hardness){
    float ss = mix(s, s * 0.18, hardness);
    return 1.0 - smoothstep(w, w + ss, abs(x));
}

float segmentMask(vec2 p, vec2 a, vec2 b, float w, float s, float hardness){
    vec2 pa = p - a;
    vec2 ba = b - a;
    float h = clamp(dot(pa, ba) / max(dot(ba, ba), 0.00001), 0.0, 1.0);
    float d = length(pa - ba * h);
    float ss = mix(s, s * 0.20, hardness);
    return 1.0 - smoothstep(w, w + ss, d);
}

float strobeBand(float pos, float phase, float width){
    float t = fract(phase);
    float d = abs(pos - t);
    d = min(d, 1.0 - d);
    return step(d, width * 0.5);
}

float beatPulse(float phase, float center, float width){
    float d = abs(phase - center);
    d = min(d, 1.0 - d);
    return 1.0 - smoothstep(width, width + 0.003, d);
}

float beatEnvelope(float timeSec, float bpm, float decayAmt){
    float beat = fract(timeSec * bpm / 60.0);
    float kick = beatPulse(beat, 0.00, 0.018);
    float clap = beatPulse(beat, 0.50, 0.022);
    float hat1 = beatPulse(beat, 0.25, 0.010);
    float hat2 = beatPulse(beat, 0.75, 0.010);

    float env = kick * 1.0 + clap * 0.8 + (hat1 + hat2) * 0.35;

    float tail = exp(-beat / max(0.001, decayAmt));
    env = max(env, kick * tail * 0.7);

    return clamp(env, 0.0, 1.6);
}

vec2 getMoveDir(float t){
    if(directionMode == 1){
        float state = mod(floor(t * stateRate), 4.0);
        if(state < 0.5) return vec2(1.0, 0.0);
        if(state < 1.5) return vec2(0.0, 1.0);
        if(state < 2.5) return vec2(-1.0, 0.0);
        return vec2(0.0, -1.0);
    }

    vec2 dir = vec2(moveRight - moveLeft, moveUp - moveDown);
    float l = length(dir);
    if(l > 0.0001) dir /= l;
    return dir;
}

vec2 domain(vec2 uv, float t, float beatEnv){
    vec2 p = uv * 2.0 - 1.0;
    p.x *= RENDERSIZE.x / RENDERSIZE.y;

    vec2 dir = getMoveDir(t);
    p += dir * driftSpeed * t;

    p *= rot(rotation * 0.55);

    float warpStep = step(0.5, fract(t * 2.0));
    float hardWarp = domainWarp * mix(1.0, warpStep, warpQuantize);
    hardWarp *= 1.0 + beatEnv * 0.35 * rhythmInfluence;

    p += hardWarp * 0.10 * vec2(
        sin(p.y * 8.0),
        cos(p.x * 8.0)
    );

    return p;
}

vec3 paletteBG(){
    if(paletteMode == 0) return vec3(0.02, 0.00, 0.01);
    if(paletteMode == 1) return vec3(0.02, 0.02, 0.05);
    return vec3(0.0);
}

vec3 paletteLine(){
    if(paletteMode == 0) return vec3(1.00, 0.08, 0.05);
    if(paletteMode == 1) return vec3(1.00, 0.50, 0.00);
    return vec3(1.0);
}

vec3 paletteNode(){
    if(paletteMode == 0) return vec3(1.0, 0.92, 0.86);
    if(paletteMode == 1) return vec3(0.16, 0.55, 1.0);
    return vec3(1.0);
}

vec3 palettePulse(){
    if(paletteMode == 0) return vec3(1.0, 0.98, 0.90);
    if(paletteMode == 1) return vec3(0.30, 0.75, 1.0);
    return vec3(0.80);
}

void main(){
    vec2 uv = isf_FragNormCoord.xy;
    float t = TIME * masterSpeed;
    float beatEnv = beatEnvelope(TIME, tempo, beatDecay);
    float rhythmBoost = 1.0 + beatEnv * rhythmInfluence * 1.8;

    vec3 bgColor    = paletteBG();
    vec3 lineColor  = paletteLine();
    vec3 nodeColor  = paletteNode();
    vec3 pulseColor = palettePulse();

    vec2 p = domain(uv, t, beatEnv);
    vec2 gp = p * gridScale;

    vec2 cell = floor(gp);
    vec2 f = fract(gp) - 0.5;

    vec2 rnd  = hash22(cell);
    vec2 rndR = hash22(cell + vec2( 1.0,  0.0));
    vec2 rndL = hash22(cell + vec2(-1.0,  0.0));
    vec2 rndU = hash22(cell + vec2( 0.0,  1.0));
    vec2 rndD = hash22(cell + vec2( 0.0, -1.0));

    float active = step(1.0 - nodeDensity, rnd.x);

    float nodeAnim = step(0.55, 0.5 + 0.5 * sin(t * (1.2 + rnd.y * 2.0) + rnd.x * 19.0));
    float node = (1.0 - smoothstep(nodeSize, nodeSize + 0.035, length(f))) * active;
    node *= mix(0.70, 1.0, nodeAnim);

    float vBus = hardLine(f.x, busWidth, busSoftness, gridHardness);
    float hBus = hardLine(f.y, busWidth, busSoftness, gridHardness);

    float vCross = hardLine(f.x, crossWidth, crossSoftness, gridHardness) * (1.0 - smoothstep(0.08, 0.28, abs(f.y)));
    float hCross = hardLine(f.y, crossWidth, crossSoftness, gridHardness) * (1.0 - smoothstep(0.08, 0.28, abs(f.x)));

    float bus = max(vBus, hBus);
    float junction = max(vCross, hCross);

    float rightOn = step(0.50, rndR.x) * step(0.50, rnd.x);
    float leftOn  = step(0.50, rndL.x) * step(0.50, rnd.x);
    float upOn    = step(0.50, rndU.x) * step(0.50, rnd.x);
    float downOn  = step(0.50, rndD.x) * step(0.50, rnd.x);

    float links = 0.0;
    links += segmentMask(f, vec2(0.0), vec2( 0.5, 0.0), busWidth * 0.80, busSoftness, gridHardness) * rightOn;
    links += segmentMask(f, vec2(0.0), vec2(-0.5, 0.0), busWidth * 0.80, busSoftness, gridHardness) * leftOn;
    links += segmentMask(f, vec2(0.0), vec2(0.0,  0.5), busWidth * 0.80, busSoftness, gridHardness) * upOn;
    links += segmentMask(f, vec2(0.0), vec2(0.0, -0.5), busWidth * 0.80, busSoftness, gridHardness) * downOn;

    float logicPattern = 0.0;
    vec2 lf = f * (4.0 + logicScale * 5.0);
    logicPattern += hardLine(fract(lf.x) - 0.5, 0.035, 0.020, 1.0) * 0.5;
    logicPattern += hardLine(fract(lf.y) - 0.5, 0.035, 0.020, 1.0) * 0.5;
    logicPattern *= active;
    logicPattern *= (0.30 + 0.70 * bus);

    float micro = 0.0;
    micro += sin((gp.x + gp.y) * 9.0 + t * 2.2);
    micro += cos((gp.x - gp.y) * 10.0 - t * 1.8);
    micro = micro * 0.25 + 0.5;
    micro = step(0.72, micro);
    micro *= microNoise * active;

    float trace = 0.0;
    float phaseX = t * (0.8 + traceMotion * 1.8) + rnd.x * 7.7 + cell.y * 0.13;
    float phaseY = t * (0.75 + traceMotion * 1.7) + rnd.y * 9.1 + cell.x * 0.11;

    float traceWidth = mix(0.12, 0.025, clamp(traceSharpness / 3.0, 0.0, 1.0));
    traceWidth *= mix(1.0, 0.75, strobeDensity);

    float stripeX = step(0.5, 0.5 + 0.5 * sin((f.y + rnd.x * 0.25) * (18.0 + traceDensity * 20.0) + phaseX * 2.0));
    float stripeY = step(0.5, 0.5 + 0.5 * sin((f.x + rnd.y * 0.25) * (18.0 + traceDensity * 20.0) + phaseY * 2.0));

    float sweepV = strobeBand(fract(f.y + 0.5), phaseY, traceWidth);
    float sweepH = strobeBand(fract(f.x + 0.5), phaseX, traceWidth);

    float energyV = vBus * max(stripeX, sweepV);
    float energyH = hBus * max(stripeY, sweepH);

    float linkSweepR = strobeBand(f.x + 0.5, phaseX + 0.12, traceWidth) * hardLine(f.y, busWidth * 0.9, busSoftness, gridHardness) * rightOn;
    float linkSweepL = strobeBand(0.5 - f.x, phaseX + 0.27, traceWidth) * hardLine(f.y, busWidth * 0.9, busSoftness, gridHardness) * leftOn;
    float linkSweepU = strobeBand(f.y + 0.5, phaseY + 0.18, traceWidth) * hardLine(f.x, busWidth * 0.9, busSoftness, gridHardness) * upOn;
    float linkSweepD = strobeBand(0.5 - f.y, phaseY + 0.31, traceWidth) * hardLine(f.x, busWidth * 0.9, busSoftness, gridHardness) * downOn;

    trace += max(energyV, energyH);
    trace += max(max(linkSweepR, linkSweepL), max(linkSweepU, linkSweepD)) * linkAmount;
    trace *= traceGlow;

    float pulse = 0.0;
    float px = fract(t * (1.2 + pulseSpeed * 1.6) + rnd.y);
    float py = fract(t * (1.1 + pulseSpeed * 1.5) + rnd.x);

    float pulseRight = strobeBand((f.x + 0.5), px, pulseWidth) * hardLine(f.y, busWidth * 0.9, busSoftness, gridHardness) * rightOn;
    float pulseLeft  = strobeBand((0.5 - f.x), px, pulseWidth) * hardLine(f.y, busWidth * 0.9, busSoftness, gridHardness) * leftOn;
    float pulseUp    = strobeBand((f.y + 0.5), py, pulseWidth) * hardLine(f.x, busWidth * 0.9, busSoftness, gridHardness) * upOn;
    float pulseDown  = strobeBand((0.5 - f.y), py, pulseWidth) * hardLine(f.x, busWidth * 0.9, busSoftness, gridHardness) * downOn;

    pulse += pulseRight + pulseLeft + pulseUp + pulseDown;
    pulse *= pulseAmount * rhythmBoost;

    float glitchPhase = fract(TIME * glitchRate);
    float glitchGate = step(0.88, glitchPhase);
    float glitchKick = max(glitchGate, beatEnv * 0.9);
    float glitchShift = glitchAmount * glitchKick * 0.045 * sin(TIME * 65.0 + cell.y * 1.7);

    vec2 p2 = domain(uv + vec2(glitchShift, 0.0), t + glitchKick * 0.03, beatEnv);
    vec2 gp2 = p2 * gridScale * (1.0 + parallax * 0.10);
    vec2 f2 = fract(gp2) - 0.5;

    float backGrid = max(
        hardLine(f2.x, busWidth * 0.55, busSoftness * 1.2, 1.0),
        hardLine(f2.y, busWidth * 0.55, busSoftness * 1.2, 1.0)
    );

    float structure = 0.0;
    structure += bus * 0.24;
    structure += junction * 0.18;
    structure += links * 0.45 * linkAmount;
    structure += trace * 0.62;
    structure += node * 0.70 * nodeGlow;
    structure += logicPattern * 0.22 * logicAmount;
    structure += micro * 0.18;
    structure += backGrid * 0.14 * parallax;
    structure += pulse * 0.50;
    structure = clamp(structure, 0.0, 1.5);

    vec3 col = bgColor;
    col += lineColor * bus * 0.28;
    col += lineColor * links * 0.30 * linkAmount;
    col += mix(lineColor, pulseColor, 0.25) * trace * 0.55;
    col += nodeColor * node * 0.62 * nodeGlow;
    col += pulseColor * pulse * 0.60;
    col += mix(lineColor, nodeColor, 0.25) * logicPattern * 0.18 * logicAmount;
    col += lineColor * backGrid * 0.10 * parallax;

    float hardStrobe = step(1.0 - strobeDensity * 0.25, beatEnv * 0.65 + trace * 0.25);
    col += pulseColor * hardStrobe * 0.16 * rhythmInfluence;

    col = mix(bgColor, col, clamp(structure, 0.0, 1.0));

    float scanHard = step(0.5, fract(uv.y * RENDERSIZE.y * 0.5));
    col *= mix(1.0, 0.30 + 0.35 * scanHard, scanlines);

    if(crush > 0.0){
        float levels = mix(256.0, 12.0, crush);
        col = floor(col * levels) / levels;
    }

    col *= brightness;
    col = contrastAdjust(col, contrast);
    col = satAdjust(col, saturation);

    vec2 asp = vec2(RENDERSIZE.x / RENDERSIZE.y, 1.0);
    float edgeFade = 1.0 - vignette * smoothstep(0.60, 1.40, length((uv * 2.0 - 1.0) * asp));
    col *= edgeFade;

    float g = (hash21(gl_FragCoord.xy + TIME * 13.1) - 0.5) * grain;
    col += vec3(g);

    if(invert){
        col = 1.0 - col;
    }

    gl_FragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}