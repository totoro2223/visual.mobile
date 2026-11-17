/*{
    "DESCRIPTION": "Neon Cityscape Skyline - Procedural wireframe city inspired by neon line art",
    "CREDIT": "Generated for VJ / ISF",
    "ISFVSN": "2.0",
    "CATEGORIES": ["Generator", "City", "Neon"],
    "INPUTS": [
        {
            "NAME": "progress",
            "TYPE": "float",
            "DEFAULT": 0.0,
            "MIN": 0.0,
            "MAX": 1.0,
            "LABEL": "Time / Animation Progress"
        },
        {
            "NAME": "buildingCount",
            "TYPE": "float",
            "DEFAULT": 28.0,
            "MIN": 8.0,
            "MAX": 60.0,
            "LABEL": "Building Count"
        },
        {
            "NAME": "skylineScale",
            "TYPE": "float",
            "DEFAULT": 1.0,
            "MIN": 0.4,
            "MAX": 1.8,
            "LABEL": "Overall Scale"
        },
        {
            "NAME": "heightVariation",
            "TYPE": "float",
            "DEFAULT": 0.75,
            "MIN": 0.2,
            "MAX": 1.2,
            "LABEL": "Height Variation"
        },
        {
            "NAME": "baseHeight",
            "TYPE": "float",
            "DEFAULT": 0.35,
            "MIN": 0.1,
            "MAX": 0.7,
            "LABEL": "Base Height"
        },
        {
            "NAME": "lineDensity",
            "TYPE": "float",
            "DEFAULT": 1.0,
            "MIN": 0.2,
            "MAX": 2.5,
            "LABEL": "Horizontal Line Density"
        },
        {
            "NAME": "windowDensity",
            "TYPE": "float",
            "DEFAULT": 1.1,
            "MIN": 0.3,
            "MAX": 2.5,
            "LABEL": "Window / Grid Density"
        },
        {
            "NAME": "lineThickness",
            "TYPE": "float",
            "DEFAULT": 1.4,
            "MIN": 0.5,
            "MAX": 4.0,
            "LABEL": "Line Thickness"
        },
        {
            "NAME": "neonGlow",
            "TYPE": "float",
            "DEFAULT": 1.6,
            "MIN": 0.0,
            "MAX": 4.0,
            "LABEL": "Neon Glow Strength"
        },
        {
            "NAME": "colorShift",
            "TYPE": "float",
            "DEFAULT": 0.0,
            "MIN": 0.0,
            "MAX": 1.0,
            "LABEL": "Color Hue Shift"
        },
        {
            "NAME": "color1",
            "TYPE": "color",
            "DEFAULT": [0.0, 1.0, 1.0, 1.0],
            "LABEL": "Cyan / Teal"
        },
        {
            "NAME": "color2",
            "TYPE": "color",
            "DEFAULT": [1.0, 0.0, 0.8, 1.0],
            "LABEL": "Magenta / Pink"
        },
        {
            "NAME": "color3",
            "TYPE": "color",
            "DEFAULT": [1.0, 0.2, 0.1, 1.0],
            "LABEL": "Red / Orange-Red"
        },
        {
            "NAME": "color4",
            "TYPE": "color",
            "DEFAULT": [0.2, 1.0, 0.3, 1.0],
            "LABEL": "Lime / Green"
        },
        {
            "NAME": "color5",
            "TYPE": "color",
            "DEFAULT": [1.0, 0.9, 0.1, 1.0],
            "LABEL": "Yellow"
        },
        {
            "NAME": "color6",
            "TYPE": "color",
            "DEFAULT": [0.6, 0.2, 1.0, 1.0],
            "LABEL": "Purple / Violet"
        },
        {
            "NAME": "color7",
            "TYPE": "color",
            "DEFAULT": [1.0, 0.45, 0.05, 1.0],
            "LABEL": "Orange"
        },
        {
            "NAME": "bgColor",
            "TYPE": "color",
            "DEFAULT": [0.0, 0.0, 0.0, 1.0],
            "LABEL": "Background"
        },
        {
            "NAME": "scrollSpeed",
            "TYPE": "float",
            "DEFAULT": 0.08,
            "MIN": -0.5,
            "MAX": 0.5,
            "LABEL": "Horizontal Scroll Speed"
        },
        {
            "NAME": "pulseAmount",
            "TYPE": "float",
            "DEFAULT": 0.15,
            "MIN": 0.0,
            "MAX": 0.6,
            "LABEL": "Height Pulse Amount"
        },
        {
            "NAME": "detailLevel",
            "TYPE": "float",
            "DEFAULT": 1.0,
            "MIN": 0.0,
            "MAX": 1.5,
            "LABEL": "Architectural Detail"
        }
    ]
}*/

#define PI 3.14159265359

// Simple hash
float hash(float n) {
    return fract(sin(n) * 43758.5453123);
}

float hash2(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

// Smooth value noise
float noise(float x) {
    float i = floor(x);
    float f = fract(x);
    float u = f * f * (3.0 - 2.0 * f);
    return mix(hash(i), hash(i + 1.0), u);
}

// Get palette color by index + hue shift
vec3 getPalette(float idx, float shift) {
    float t = fract(idx * 0.142 + shift);
    
    if (t < 0.142) return color1.rgb;
    if (t < 0.285) return color2.rgb;
    if (t < 0.428) return color3.rgb;
    if (t < 0.571) return color4.rgb;
    if (t < 0.714) return color5.rgb;
    if (t < 0.857) return color6.rgb;
    return color7.rgb;
}

// Building height profile (procedural skyline)
float buildingHeight(float x, float seed) {
    float h = baseHeight;
    
    // Multiple layers of noise for interesting skyline
    h += noise(x * 0.7 + seed) * 0.35 * heightVariation;
    h += noise(x * 1.9 + seed * 1.3) * 0.22 * heightVariation;
    h += noise(x * 4.1 + seed * 2.1) * 0.12 * heightVariation;
    
    // Occasional tall towers
    float tall = smoothstep(0.82, 0.95, noise(x * 0.35 + seed * 0.7));
    h += tall * 0.45 * heightVariation;
    
    // Occasional medium peaks
    float mid = smoothstep(0.65, 0.8, noise(x * 0.9 + seed * 1.5));
    h += mid * 0.18 * heightVariation;
    
    return clamp(h, 0.05, 0.95);
}

// Soft line (for neon glow)
float softLine(float d, float thickness) {
    return smoothstep(thickness, 0.0, abs(d));
}

void main() {
    vec2 uv = isf_FragNormCoord;
    
    // Force 16:9 feeling (letterbox if needed, but we work in normalized)
    float aspect = RENDERSIZE.x / RENDERSIZE.y;
    vec2 p = uv;
    p.x *= aspect;          // work in wider space
    p.x /= 1.777777;        // normalize back toward 16:9
    
    // Horizontal scroll
    float scroll = progress * scrollSpeed * 10.0;
    p.x += scroll;
    
    // Scale
    p.y = (p.y - 0.0) / skylineScale;
    
    // Pulse
    float pulse = 1.0 + sin(progress * PI * 2.0) * pulseAmount * 0.5;
    
    vec3 col = bgColor.rgb;
    float alpha = 0.0;
    
    float buildingWidth = 1.8 / buildingCount;
    float startX = -0.15;
    
    // Draw many buildings
    for (float i = 0.0; i < 60.0; i++) {
        if (i >= buildingCount) break;
        
        float bx = startX + i * buildingWidth * 1.05 + hash(i * 13.7) * 0.02;
        float bw = buildingWidth * (0.55 + hash(i * 7.3) * 0.7);
        
        // Building local x
        float localX = (p.x - bx) / bw;
        
        if (localX > -0.05 && localX < 1.05) {
            float seed = i * 19.17 + 3.1;
            float h = buildingHeight(bx * 3.0 + seed, seed) * pulse;
            
            // Slight random width variation already applied
            float left = 0.0;
            float right = 1.0;
            
            // Building body mask
            float inBuilding = step(0.0, localX) * step(localX, 1.0) * step(p.y, h);
            
            if (inBuilding > 0.5) {
                // Color for this building
                vec3 bCol = getPalette(i + hash(i), colorShift);
                
                // Vertical edges (left & right outline)
                float edgeL = softLine(localX, 0.008 * lineThickness);
                float edgeR = softLine(localX - 1.0, 0.008 * lineThickness);
                
                // Top edge
                float topEdge = softLine(p.y - h, 0.006 * lineThickness);
                
                // Horizontal window lines
                float winSpace = 0.018 / (lineDensity * 0.7 + 0.3);
                float winY = mod(p.y, winSpace);
                float hLine = softLine(winY - winSpace * 0.5, 0.0035 * lineThickness);
                
                // Vertical window columns (grid)
                float colSpace = 0.07 / (windowDensity * 0.8 + 0.2);
                float winX = mod(localX, colSpace);
                float vLine = softLine(winX - colSpace * 0.5, 0.003 * lineThickness) * detailLevel;
                
                // Some buildings have denser or sparser grids
                float style = hash(i * 5.1);
                if (style > 0.7) {
                    // Solid-ish horizontal heavy
                    hLine *= 1.4;
                    vLine *= 0.4;
                } else if (style < 0.25) {
                    // More vertical emphasis
                    vLine *= 1.6;
                    hLine *= 0.6;
                }
                
                // Dome / spire on some tall buildings
                float isTall = step(0.72, h);
                float dome = 0.0;
                if (isTall > 0.5 && style > 0.55) {
                    float domeH = h + 0.04;
                    float domeR = length(vec2((localX - 0.5) * 1.8, (p.y - h) * 12.0));
                    dome = softLine(domeR - 0.35, 0.04) * step(p.y, domeH);
                }
                
                // Antenna / spire
                float antenna = 0.0;
                if (isTall > 0.5 && hash(i * 11.3) > 0.6) {
                    float antX = softLine(localX - 0.5, 0.006);
                    float antY = step(h, p.y) * step(p.y, h + 0.08 + hash(i) * 0.06);
                    antenna = antX * antY;
                }
                
                // Combine lines
                float lines = max(max(edgeL, edgeR), topEdge);
                lines = max(lines, hLine * 0.85);
                lines = max(lines, vLine * 0.7);
                lines = max(lines, dome);
                lines = max(lines, antenna);
                
                // Neon glow accumulation
                float glow = lines * neonGlow;
                
                // Soft fill for body (very subtle)
                float fill = inBuilding * 0.03 * (1.0 - p.y / h);
                
                vec3 finalCol = bCol * (glow + fill);
                
                // Additive blend
                col += finalCol;
                alpha = max(alpha, lines);
            }
        }
    }
    
    // Subtle ground reflection / base glow
    float ground = smoothstep(0.08, 0.0, p.y) * 0.15;
    col += vec3(0.1, 0.15, 0.25) * ground;
    
    // Final tone
    col = clamp(col, 0.0, 1.5);
    
    // Soft vignette (optional cinematic feel)
    float vig = 1.0 - smoothstep(0.55, 1.35, length(uv - 0.5) * 1.1);
    col *= mix(0.85, 1.0, vig);
    
    gl_FragColor = vec4(col, 1.0);
}