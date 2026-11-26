/*{
  "CATEGORIES": ["Generator"],
  "DESCRIPTION": "Organic Nebula with Procedural Pattern & Glitch",
  "ISFVSN": "2",
  "INPUTS": [
    { "NAME": "time", "TYPE": "float" },
    { "NAME": "camX", "TYPE": "float", "DEFAULT": 0.0, "MIN": -10.0, "MAX": 10.0 },
    { "NAME": "camY", "TYPE": "float", "DEFAULT": 0.0, "MIN": -10.0, "MAX": 10.0 },
    { "NAME": "camZ", "TYPE": "float", "DEFAULT": -6.0, "MIN": -30.0, "MAX": -1.0 },
    { "NAME": "tileSize", "TYPE": "float", "DEFAULT": 30.0, "MIN": 2.0, "MAX": 50.0 },
    { "NAME": "patternType", "TYPE": "long", "VALUES": [0,1,2], "LABELS": ["Checker", "Stripes", "Diagonal"], "DEFAULT": 1 },
    { "NAME": "blendPattern", "TYPE": "float", "DEFAULT": 1, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "rotatePattern", "TYPE": "float", "DEFAULT": 0.0, "MIN": 0.0, "MAX": 6.28 },
    { "NAME": "nebulaSize", "TYPE": "float", "DEFAULT": 4, "MIN": 0.5, "MAX": 5.0 },
    { "NAME": "glitchAmount", "TYPE": "float", "DEFAULT": 0.0, "MIN": 0.0, "MAX": 1.0 }
  ]
}*/

// Noise sederhana
float rand(vec2 co) {
    return fract(sin(dot(co.xy ,vec2(12.9898,78.233))) * 43758.5453);
}

float sphereField(vec3 p) {
    float r = nebulaSize + 0.3 * sin(time * 2.0);
    float d = length(p) - r;
    d += sin(p.x * 3. + time) * sin(p.y * 3. + time) * sin(p.z * 3. + time) * 0.2;
    return d;
}

vec3 getNormal(vec3 p) {
    float e = 0.001;
    return normalize(vec3(
        sphereField(p + vec3(e, 0, 0)) - sphereField(p - vec3(e, 0, 0)),
        sphereField(p + vec3(0, e, 0)) - sphereField(p - vec3(0, e, 0)),
        sphereField(p + vec3(0, 0, e)) - sphereField(p - vec3(0, 0, e))
    ));
}

vec2 rotate(vec2 uv, float angle) {
    float s = sin(angle), c = cos(angle);
    return mat2(c, -s, s, c) * uv;
}

// Pattern dengan efek glitch
float pattern(vec2 uv) {
    uv = rotate(uv, rotatePattern);
    uv *= tileSize;

    // Tambahkan glitch noise
    uv += glitchAmount * vec2(rand(uv + time), rand(uv * 1.23 + time * 0.7));

    if (patternType == 0) {
        return mod(floor(uv.x) + floor(uv.y), 2.0);
    } else if (patternType == 1) {
        return mod(floor(uv.x), 2.0);
    } else if (patternType == 2) {
        return mod(floor(uv.x + uv.y), 2.0);
    }
    return 0.0;
}

void main() {
    vec2 uv = (gl_FragCoord.xy / RENDERSIZE.xy) * 2.0 - 1.0;
    uv.x *= RENDERSIZE.x / RENDERSIZE.y;

    vec3 ro = vec3(camX, camY, camZ);
    vec3 rd = normalize(vec3(uv, 1.0));

    float t = 0.0;
    float d;
    const float MAX_DIST = 10.0;
    const float SURF_DIST = 0.001;
    const int MAX_STEPS = 128;

    for (int i = 0; i < MAX_STEPS; i++) {
        vec3 p = ro + rd * t;
        d = sphereField(p);
        if (abs(d) < SURF_DIST || t > MAX_DIST) break;
        t += d;
    }

    vec3 color = vec3(0.0);
    if (t < MAX_DIST) {
        vec3 p = ro + rd * t;
        vec3 n = getNormal(p);
        vec3 lightDir = normalize(vec3(0.2, 0.5, -1.0));
        float diff = max(dot(n, lightDir), 0.0);
        float fresnel = pow(1.0 - dot(n, -rd), 3.0);

        vec2 texUV = p.xy;
        float pat = pattern(texUV);
        vec3 patColor = vec3(pat);

        vec3 baseColor = vec3(0.2, 0.3 + 0.3 * sin(time), 0.7);
        vec3 mixed = mix(baseColor, patColor, blendPattern);
        color = mixed * diff + fresnel;
    }

    gl_FragColor = vec4(color, 1.0);
}
