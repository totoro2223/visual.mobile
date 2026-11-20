/*{
  "CREDIT": "Custom 3D Distorted Sphere",
  "CATEGORIES": [
    "generator",
    "3D"
  ],
  "INPUTS": [
    {
      "NAME": "sphereSize",
      "TYPE": "float",
      "DEFAULT": 1.0,
      "MIN": 0.2,
      "MAX": 3.0
    },
    {
      "NAME": "cubeBlend",
      "TYPE": "float",
      "DEFAULT": 0.0,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "NAME": "rotationSpeed",
      "TYPE": "float",
      "DEFAULT": 0.5,
      "MIN": -2.0,
      "MAX": 2.0
    },
    {
      "NAME": "waveAmplitude",
      "TYPE": "float",
      "DEFAULT": 0.0,
      "MIN": 0.0,
      "MAX": 0.5
    },
    {
      "NAME": "waveBPM",
      "TYPE": "float",
      "DEFAULT": 120.0,
      "MIN": 80.0,
      "MAX": 360.0
    },
    {
      "NAME": "lightPosition",
      "TYPE": "point2D",
      "DEFAULT": [
        0.5,
        0.5
      ],
      "MIN": [
        -1.0,
        -1.0
      ],
      "MAX": [
        1.0,
        1.0
      ]
    },
    {
      "NAME": "lightRotationSpeed",
      "TYPE": "float",
      "DEFAULT": 0.0,
      "MIN": -2.0,
      "MAX": 2.0
    },
    {
      "NAME": "pulsePower",
      "TYPE": "float",
      "DEFAULT": 0.0,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "NAME": "pulseBPM",
      "TYPE": "float",
      "DEFAULT": 120.0,
      "MIN": 20.0,
      "MAX": 360.0
    },
    {
      "NAME": "cameraDistance",
      "TYPE": "float",
      "DEFAULT": 4.0,
      "MIN": 2.0,
      "MAX": 8.0
    },
    {
      "NAME": "sphereColor",
      "TYPE": "color",
      "DEFAULT": [
        0.2,
        0.6,
        1.0,
        1.0
      ]
    },
    {
      "NAME": "lightColor",
      "TYPE": "color",
      "DEFAULT": [
        1.0,
        1.0,
        0.8,
        1.0
      ]
    },
    {
      "NAME": "ambient",
      "TYPE": "float",
      "DEFAULT": 0.2,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "NAME": "backgroundColor",
      "TYPE": "color",
      "DEFAULT": [
        0.05,
        0.1,
        0.2,
        1.0
      ]
    }
  ],
  "DESCRIPTION": "3D shape morphing from sphere to cube with BPM-based wave and pulse distortions"
}*/

// Simple noise function
float noise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    
    float n = i.x + i.y * 157.0 + 113.0 * i.z;
    return mix(
        mix(mix(fract(sin(n) * 43758.5453),
                fract(sin(n + 1.0) * 43758.5453), f.x),
            mix(fract(sin(n + 157.0) * 43758.5453),
                fract(sin(n + 158.0) * 43758.5453), f.x), f.y),
        mix(mix(fract(sin(n + 113.0) * 43758.5453),
                fract(sin(n + 114.0) * 43758.5453), f.x),
            mix(fract(sin(n + 270.0) * 43758.5453),
                fract(sin(n + 271.0) * 43758.5453), f.x), f.y), f.z);
}

// Distance function for distorted sphere/cube
float sphereSDF(vec3 p) {
    // Sphere distance
    float sphereDist = length(p) - sphereSize;
    
    // Cube distance
    vec3 d = abs(p) - vec3(sphereSize);
    float cubeDist = length(max(d, 0.0)) + min(max(d.x, max(d.y, d.z)), 0.0);
    
    // Blend between sphere and cube
    float baseDist = mix(sphereDist, cubeDist, cubeBlend);
    
    // Convert BPM to frequency (beats per second)
    float beatsPerSecond = waveBPM / 60.0;
    
    // Apply wave distortion based on BPM
    // Use spatial frequency of 8.0 (equivalent to old waveFrequency default)
    float spatialFreq = 8.0;
    float waveDistortion = sin(p.x * spatialFreq + TIME * beatsPerSecond * 2.0) * 
                          sin(p.y * spatialFreq + TIME * beatsPerSecond * 1.5) * 
                          sin(p.z * spatialFreq + TIME * beatsPerSecond * 2.5) * waveAmplitude;
    
    // Apply pulse distortion based on BPM
    float pulseBeatsPerSecond = pulseBPM / 60.0;
    float pulse = sin(TIME * pulseBeatsPerSecond * 6.28318530718) * 0.5 + 0.5; // 2*PI for full cycle per beat
    float pulseDistortion = pulse * pulsePower;
    
    return baseDist + waveDistortion - pulseDistortion;
}

// Calculate surface normal
vec3 getNormal(vec3 p) {
    const float eps = 0.001;
    return normalize(vec3(
        sphereSDF(p + vec3(eps, 0, 0)) - sphereSDF(p - vec3(eps, 0, 0)),
        sphereSDF(p + vec3(0, eps, 0)) - sphereSDF(p - vec3(0, eps, 0)),
        sphereSDF(p + vec3(0, 0, eps)) - sphereSDF(p - vec3(0, 0, eps))
    ));
}

// Raymarching function
vec4 raymarch(vec3 ro, vec3 rd) {
    float t = 0.0;
    const int maxSteps = 64;
    const float maxDist = 20.0;
    const float minDist = 0.001;
    
    for(int i = 0; i < maxSteps; i++) {
        vec3 p = ro + rd * t;
        float d = sphereSDF(p);
        
        if(d < minDist) {
            // Hit surface - calculate lighting
            vec3 normal = getNormal(p);
            
            // Light position with optional rotation
            vec3 baseLight = vec3(lightPosition.x * 4.0, lightPosition.y * 4.0, 3.0);
            
            // Apply rotation if speed > 0
            if(abs(lightRotationSpeed) > 0.01) {
                float angle = TIME * lightRotationSpeed;
                float c = cos(angle);
                float s = sin(angle);
                // Rotate around Y axis
                baseLight = vec3(
                    c * baseLight.x - s * baseLight.z,
                    baseLight.y,
                    s * baseLight.x + c * baseLight.z
                );
            }
            
            vec3 lightDir = normalize(baseLight - p);
            
            // Basic Phong lighting
            float diff = max(dot(normal, lightDir), 0.0);
            vec3 reflectDir = reflect(-lightDir, normal);
            float spec = pow(max(dot(-rd, reflectDir), 0.0), 32.0);
            
            // Combine lighting
            vec3 color = sphereColor.rgb * (ambient + diff) + lightColor.rgb * spec * 0.5;
            
            // Add some rim lighting
            float rim = 1.0 - max(dot(normal, -rd), 0.0);
            color += pow(rim, 3.0) * lightColor.rgb * 0.3;
            
            return vec4(color, 1.0);
        }
        
        if(t > maxDist) break;
        t += d;
    }
    
    // Custom background color with subtle gradient
    float gradient = 0.5 + 0.5 * rd.y;
    vec3 bgColor = backgroundColor.rgb * (0.8 + 0.4 * gradient);
    return vec4(bgColor, 1.0);
}

// Rotation matrix around Y axis
mat3 rotateY(float angle) {
    float c = cos(angle);
    float s = sin(angle);
    return mat3(c, 0, s, 0, 1, 0, -s, 0, c);
}

void main() {
    // Screen coordinates
    vec2 uv = (gl_FragCoord.xy - 0.5 * RENDERSIZE.xy) / RENDERSIZE.y;
    
    // Camera setup
    float rotation = TIME * rotationSpeed;
    vec3 ro = rotateY(rotation) * vec3(0, 0, cameraDistance);
    vec3 target = vec3(0, 0, 0);
    vec3 up = vec3(0, 1, 0);
    
    // Camera basis vectors
    vec3 forward = normalize(target - ro);
    vec3 right = normalize(cross(forward, up));
    vec3 newUp = cross(right, forward);
    
    // Ray direction
    vec3 rd = normalize(uv.x * right + uv.y * newUp + 1.5 * forward);
    
    // Render
    gl_FragColor = raymarch(ro, rd);
}