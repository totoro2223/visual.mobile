/*{
    "CATEGORIES": ["Wall Projection"],
    "DESCRIPTION": "Expanding radial waves",
    "INPUTS": [
        {"NAME": "wave_speed", "TYPE": "float", "DEFAULT": 0.5, "MIN": 0.1, "MAX": 2.0},
        {"NAME": "wave_scale", "TYPE": "float", "DEFAULT": 3.0, "MIN": 1.0, "MAX": 10.0},
        {"NAME": "base_color", "TYPE": "color", "DEFAULT": [0.3, 0.3, 0.8, 1.0]}
    ]
}*/

void main() {
    vec2 uv = isf_FragNormCoord - 0.5;
    
    // Calculate distance from center
    float dist = length(uv) * wave_scale;
    
    // Create a pulsating radial wave effect
    float wave = sin(dist - TIME * wave_speed);
    
    // Blend the effect into the base color
    vec3 color = base_color.rgb * (wave * 0.5 + 0.5);
    
    gl_FragColor = vec4(color, 1.0);
}
