/*{
    "DESCRIPTION": "Optimized GLSL raymarch shader with Perlin noise replacing the height texture.",
    "CREDIT": "Original GLSL shader by the user, optimized and adapted to ISF.",
    "ISFVSN": "2",
    "INPUTS": [
    
        {
            "NAME": "xPos",
            "TYPE": "float",
            "DEFAULT": 0.0,
            "MIN": -10.0,
            "MAX": 10.0
        },
        {
            "NAME": "yPos",
            "TYPE": "float",
            "DEFAULT": 0.84,
            "MIN": 0.1,
            "MAX": 1.0
        },
        {
            "NAME": "zPos",
            "TYPE": "float",
            "DEFAULT": 0.25
        },
        {
            "NAME": "cameraX",
            "TYPE": "float",
            "MIN": -3.14,
            "MAX": 3.14,
            "DEFAULT": 1.3
        },
        {
            "NAME": "cameraY",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.57,
            "DEFAULT": 1.2
        },
        {
            "NAME": "cameraZ",
            "TYPE": "float",
            "MIN": -3.14,
            "MAX": 3.14,
            "DEFAULT": 0.0
        },
        {
            "NAME": "iTime",
            "TYPE": "float",
            "DEFAULT": 0.0,
            "DESCRIPTION": "Time uniform for animations."
        },
        {
            "NAME": "extrude",
            "TYPE": "float",
            "DEFAULT": 1.0,
            "MIN": 0.1,
            "MAX": 1.0,
            "DESCRIPTION": "Radius of the grid lines."
        },
        {
            "NAME": "size",
            "TYPE": "float",
            "DEFAULT": 2.0,
            "MIN": 2.0,
            "MAX": 10.0,
            "DESCRIPTION": "Defines the density of quads per unit."
        },
        {
            "NAME": "line",
            "TYPE": "float",
            "DEFAULT": 2.8,
            "MIN": 0.1,
            "MAX": 2.8,
            "DESCRIPTION": "Radius of the grid lines."
        },
        {
            "NAME": "lineColor",
            "TYPE": "color",
            "DEFAULT": [1.0, 0.0, 0.1, 1.0],
            "DESCRIPTION": "Color of the haze effect."
        }
    ]
}*/

// Raymarch settings
#define MIN_DIST 0.001
#define MAX_DIST 20.0
#define MAX_STEPS 24
#define STEP_MULT 1.0
#define NORMAL_OFFS 0.002
#define FOCAL_LENGTH 0.8

// Derived settings
#define QUAD_SIZE (1.0 / size)

float pi = atan(1.0) * 4.0;
float tau = atan(1.0) * 8.0;

struct MarchResult {
    vec3 position;
    vec3 normal;
    float dist;
    float steps;
};

// Simple 2D Perlin noise function
float Noise(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float SmoothNoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);

    // Bilinear interpolation of noise
    float a = Noise(i);
    float b = Noise(i + vec2(1.0, 0.0));
    float c = Noise(i + vec2(0.0, 1.0));
    float d = Noise(i + vec2(1.0, 1.0));

    vec2 u = f * f * (3.0 - 2.0 * f);

    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

// Fractional Brownian motion to create complex Perlin noise
float PerlinNoise(vec2 p) {
    float total = 0.0;
    float frequency = 1.0;
    float amplitude = 0.5;

    for (int i = 0; i < 1; i++) { // Reduced from 4 to 3 octaves
        total += SmoothNoise(p * frequency) * amplitude;
        frequency *= 2.0;
        amplitude *= 0.5;
    }

    return total;
}
// Returns the height at a given position using Perlin noise
float Height(vec2 p) {
    p *= QUAD_SIZE;
    float h = 0.0;
    h += sin(TIME * 0.4 * length(p) + 123. * length(p)) * 0.1 * extrude + sin(length(p) * 2.0 + iTime) * cos(TIME * 0.3) * extrude * 0.3;
    return h;
}

// Returns a rotation matrix for the given angles around the X, Y, Z axes
mat3 Rotate(vec3 angles) {
    vec3 c = cos(angles);
    vec3 s = sin(angles);

    mat3 rotX = mat3(1.0, 0.0, 0.0, 0.0, c.x, s.x, 0.0, -s.x, c.x);
    mat3 rotY = mat3(c.y, 0.0, -s.y, 0.0, 1.0, 0.0, s.y, 0.0, c.y);
    mat3 rotZ = mat3(c.z, s.z, 0.0, -s.z, c.z, 0.0, 0.0, 0.0, 1.0);

    return rotX * rotY * rotZ;
}

// Distance field operators/functions by iq
float opU(float d1, float d2) {
    return min(d1, d2);
}

float opS(float d1, float d2) {
    return max(-d1, d2);
}

float sdSphere(vec3 p, float s) {
    return length(p) - s;
}

// Modified to create a plane from 3 points
float sdPlane(vec3 p, vec3 p0, vec3 p1, vec3 p2) {
    return dot(p - p0, normalize(cross(p0 - p1, p0 - p2)));
}

/*
Distance to a vertical quad comprised of two triangles.

1-----2
| 0  /|
|  /  |
|/  1 |
0-----3
*/
float sdVQuad(vec3 p, float h0, float h1, float h2, float h3) {
    float s = QUAD_SIZE;

    float diag = sdPlane(p, vec3(0, 0, 0), vec3(s, s, 0), vec3(0, 0, s));

    float tri0 = sdPlane(p, vec3(0, 0, -h0), vec3(0, s, -h1), vec3(s, s, -h2)); // Triangle 0 (0,1,2)
    tri0 = opS(-diag, tri0);

    float tri1 = sdPlane(p, vec3(0, 0, -h0), vec3(s, s, -h2), vec3(s, 0, -h3)); // Triangle 1 (0,2,3)
    tri1 = opS(diag, tri1);

    float d = min(tri0, tri1);

    return d;
}

// Distance to the scene
float Scene(vec3 p) {
    float d = MAX_DIST;

    p.xy=mat2(.87,.5,.0,1)*p.xy;
    vec3 pm = vec3(mod(p.xy, vec2(QUAD_SIZE)), p.z);

    vec2 uv = floor(p.xy / QUAD_SIZE);

    float v0 = Height(uv + vec2(0, 0));
    float v1 = Height(uv + vec2(0, 1));
    float v2 = Height(uv + vec2(1, 1));
    float v3 = Height(uv + vec2(1, 0));

    d = sdVQuad(pm - vec3(0.0, 0.0, 0.0), v0, v1, v2, v3);

    d = opU(d, -sdSphere(p, MAX_DIST - 1.0));

    return d;
}

// Surface normal at the current position
vec3 Normal(vec3 p) {
    vec3 off = vec3(NORMAL_OFFS, 0, 0);
    return normalize(
        vec3(
            Scene(p + off.xyz) - Scene(p - off.xyz),
            Scene(p + off.zxy) - Scene(p - off.zxy),
            Scene(p + off.yzx) - Scene(p - off.yzx)
        )
    );
}

// Raymarch the scene with the given ray
MarchResult MarchRay(vec3 orig, vec3 dir) {
    float steps = 0.0;
    float dist = 0.0;

    for (int i = 0; i < MAX_STEPS; i++) {
        float sceneDist = Scene(orig + dir * dist);

        dist += sceneDist * STEP_MULT;

        steps++;

        if (abs(sceneDist) < MIN_DIST) {
            break;
        }
    }

    MarchResult result;

    result.position = orig + dir * dist;
    result.normal = Normal(result.position);
    result.dist = dist;
    result.steps = steps;

    return result;
}

// Scene texturing/shading
vec3 Shade(MarchResult hit, vec3 direction, vec3 camera) {
    vec3 color = vec3(1.0);

    // Triangle grid pattern
    vec2 gridRep = mod(mat2(.87,.5,.0,1)*hit.position.xy, vec2(QUAD_SIZE)) / float(QUAD_SIZE) - 0.5;

    float grid = 0.5 - max(abs(gridRep.x), abs(gridRep.y));
    grid = min(grid, abs(dot(gridRep.xy, normalize(vec2(-1, 1)))));

    float lineSize = size * hit.dist / RENDERSIZE.y / float(QUAD_SIZE);

    color *= smoothstep(lineSize, lineSize * 0.25, grid) * lineColor.rgb;

    // Fog / haze
    float sky = smoothstep(MAX_DIST - 1.0, 0.0, length(hit.position));
    float haze = 1.0 - (hit.steps / float(MAX_STEPS)) * 0.6;

    color = mix(vec3(0.), color, sky * haze);

    return color;
}

void main() {
    vec2 res = RENDERSIZE.xy / RENDERSIZE.y;
    vec2 uv = gl_FragCoord.xy / RENDERSIZE.y;

    vec3 angles = vec3(cameraX, cameraY, cameraZ);

    mat3 rotate = Rotate(angles.yzx);

    vec3 orig = vec3(xPos, (1.0 - yPos) * 5., zPos * -5.) * rotate;

    vec3 dir = normalize(vec3(uv - res / 2.0, FOCAL_LENGTH)) * rotate;

    MarchResult hit = MarchRay(orig, dir);

    vec3 color = Shade(hit, dir, orig);

    gl_FragColor = vec4(color, 1.0);
}
