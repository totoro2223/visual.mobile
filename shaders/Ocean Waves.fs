/*{
  "CREDIT": "Gemini for VDMX",
  "DESCRIPTION": "Ocean waves with strict sequencer triggering",
  "CATEGORIES": [
    "Generator",
    "Organic",
    "Background"
  ],
  "INPUTS": [
    {
      "NAME": "beat_phase",
      "TYPE": "float",
      "DEFAULT": 0.0,
      "MIN": 0.0,
      "MAX": 1.0,
      "LABEL": "Beat Phase (0-1 Ramp)"
    },
    {
      "NAME": "pulse_trigger",
      "TYPE": "float",
      "DEFAULT": 0.0,
      "MIN": 0.0,
      "MAX": 1.0,
      "LABEL": "Pulse Trigger (Sequencer)"
    },
    {
      "NAME": "wave_speed",
      "TYPE": "float",
      "DEFAULT": 0.2,
      "MIN": 0.0,
      "MAX": 2.0,
      "LABEL": "Wave Flow Speed"
    },
    {
      "NAME": "wave_height",
      "TYPE": "float",
      "DEFAULT": 0.6,
      "MIN": 0.0,
      "MAX": 3.0,
      "LABEL": "Wave Height"
    },
    {
      "NAME": "beam_base_alpha",
      "TYPE": "float",
      "DEFAULT": 0.0,
      "MIN": 0.0,
      "MAX": 1.0,
      "LABEL": "Beam Base (Resting)"
    },
    {
      "NAME": "beam_pulse_amt",
      "TYPE": "float",
      "DEFAULT": 1.0,
      "MIN": 0.0,
      "MAX": 1.0,
      "LABEL": "Beam Pulse (Flash)"
    },
    {
      "NAME": "complexity",
      "TYPE": "float",
      "DEFAULT": 3.0,
      "MIN": 1.0,
      "MAX": 8.0,
      "LABEL": "Wave Complexity"
    },
    {
      "NAME": "color_deep",
      "TYPE": "color",
      "DEFAULT": [
        0.0,
        0.05,
        0.2,
        1.0
      ],
      "LABEL": "Deep Color"
    },
    {
      "NAME": "color_shallow",
      "TYPE": "color",
      "DEFAULT": [
        0.0,
        0.6,
        0.8,
        1.0
      ],
      "LABEL": "Shallow Color"
    },
    {
      "NAME": "beam_color",
      "TYPE": "color",
      "DEFAULT": [
        0.8,
        0.9,
        1.0,
        1.0
      ],
      "LABEL": "Beam Color"
    }
  ]
}*/

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i + vec2(0.0, 0.0)), hash(i + vec2(1.0, 0.0)), f.x),
               mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

float fbm(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    vec2 shift = vec2(100.0);
    mat2 rot = mat2(cos(0.5), sin(0.5), -sin(0.5), cos(0.50));
    for (int i = 0; i < 8; ++i) {
        if (i >= int(complexity)) break;
        v += a * noise(p);
        p = rot * p * 2.0 + shift;
        a *= 0.5;
    }
    return v;
}

void main() {
    vec2 uv = gl_FragCoord.xy / RENDERSIZE.xy;
    vec2 p = uv * 2.0 - 1.0;
    p.x *= RENDERSIZE.x / RENDERSIZE.y;

    // --- LOGIC FIX ---
    // We combine the continuous beat phase with the trigger from your sequencer.
    // If Pulse Trigger is 0, the result is 0 (No Pulse).
    // If Pulse Trigger is 1, we let the Beat Phase sine wave through.
    
    // Create a smooth 0-1-0 curve from the beat phase
    float sine_pulse = sin(beat_phase * 3.14159);
    
    // Gating: Only allow the pulse if the trigger is active (High)
    // We use smoothstep to make the trigger sensitivity adjustable if needed, 
    // but here it acts as a hard gate.
    float final_pulse = sine_pulse * pulse_trigger;

    // --- WAVES ---
    float t = TIME * wave_speed;
    
    // The wave vertical surge
    float surge = smoothstep(0.0, 1.0, final_pulse) * 0.3;
    
    vec2 q = vec2(0.0);
    q.x = fbm(p + 0.00 * t);
    q.y = fbm(p + vec2(1.0));
    
    vec2 r = vec2(0.0);
    r.x = fbm(p + 1.0 * q + vec2(1.7, 9.2) + 0.15 * t);
    r.y = fbm(p + 1.0 * q + vec2(8.3, 2.8) + 0.126 * t - surge);
    
    float f = fbm(p + r * wave_height);
    vec3 ocean = mix(color_deep.rgb, color_shallow.rgb, clamp((f * f) * 4.0, 0.0, 1.0));
    ocean += 0.2 * vec3(1.0) * smoothstep(0.5, 0.9, f);

    // --- BEAMS ---
    float beam_noise = noise(vec2(p.x * 4.0, TIME * 0.5));
    float beam_shape = smoothstep(0.6, 0.9, beam_noise);
    beam_shape *= (1.0 - abs(p.x));
    
    // STRICT SEPARATION:
    // Base: Always on (set to 0 to hide beams between beats)
    // Pulse: Only adds brightness when final_pulse is active
    float total_beam_brightness = beam_base_alpha + (final_pulse * beam_pulse_amt);
    
    vec3 beam_final = beam_color.rgb * beam_shape * total_beam_brightness;
    
    vec3 final_color = ocean + beam_final;
    float vignette = 1.0 - length(uv - 0.5);
    final_color *= smoothstep(0.0, 1.0, vignette);

    gl_FragColor = vec4(final_color, 1.0);
}