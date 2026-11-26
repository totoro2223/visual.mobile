/*{
	"DESCRIPTION": "Wave pattern overlays",
	"CREDIT": "Inspired by https://www.shadertoy.com/view/4dfGDs, in turn inspired by cymatics and chaldni plate patterns",
	"ISFVSN": "2",
	"CATEGORIES": [
		"Generation"
	],
	"INPUTS": [
		{
			"NAME": "waveCount",
			"TYPE": "float",
			"DEFAULT": 8.0,
			"MIN": 2.0,
			"MAX": 16.0
		},
		{
			"NAME": "waveFrequency",
			"TYPE": "float",
			"DEFAULT": 8.0,
			"MIN": 1.0,
			"MAX": 16.0
		},
		{
			"NAME": "waveSpeed",
			"TYPE": "float",
			"DEFAULT": 0.025,
			"MIN": -1.0,
			"MAX": 1.0
		},
		{
			"NAME": "waveRotation",
			"TYPE": "float",
			"DEFAULT": 0.025,
			"MIN": -1.0,
			"MAX": 1.0
		},
		{
			"NAME": "waveDecay",
			"TYPE": "float",
			"DEFAULT": 0.25,
			"MIN": 0.0,
			"MAX": 1.0
		},
		{
			"NAME": "wavePosition",
			"TYPE": "float",
			"DEFAULT": 0.5,
			"MIN": 0.0,
			"MAX": 1.0
		},
		{
			"NAME": "varianceAmount",
			"TYPE": "float",
			"DEFAULT": 0.25,
			"MIN": 0.0,
			"MAX": 1.0
		},
		{
			"NAME": "varianceSpeed",
			"TYPE": "float",
			"DEFAULT": 0.025,
			"MIN": -1.0,
			"MAX": 1.0
		},
		{
			"NAME": "display",
			"TYPE": "long",
			"VALUES": [
				0,
				1,
				2
			],
			"LABELS": [
				"debug",
				"basic",
				"sharp"
			],
			"DEFAULT": 1
		}
	]
}*/

#define COUNT 16

void main()	{
    // vec2 uv = isf_FragNormCoord.xy * 2.0 - 1.0;
    float m = max(RENDERSIZE.x, RENDERSIZE.y);
    vec2 uv = (gl_FragCoord.xy * 2.0 - RENDERSIZE) / m;
    
    float x = 0.0;
    float r = 0.0;
    float a = 0.0;
    float b = 0.5;
	vec2 pos = vec2(0.0);
	float waves = 0.0;
	float lines = 0.0;
	float debug = 16.0;
	for (int i = 0; i < COUNT; ++i) {
	    if (i < int(waveCount)) {
    	    x = float(i) / floor(waveCount);
    	    r = x * (float(COUNT) * 2.39996323);
    	    a = (x + TIME * waveRotation * 0.1) * 6.28318531;
    	    b = sin((r + TIME * varianceSpeed) * 6.28318531);
    	    b *= varianceAmount * wavePosition;
    	    b += wavePosition;
    	    pos = vec2(sin(a), cos(a)) * b;
    	    waves += sin((length(uv-pos) - TIME * waveSpeed + r) * 6.28318531 * waveFrequency) / (1.0 + length(uv-pos) * waveDecay * waveDecay * 8.0);
    	    debug = min(debug, length(uv - pos));
	    }
	}
	debug = mix(1.0 - debug, 1.0 - step(0.005, debug), 0.25);
	
	waves /= float(COUNT);
	waves = smoothstep(-0.5, 0.5, waves);
    
	lines = abs(waves * 2.0 - 1.0);
	lines = max(1.0-lines, 0.0);
	lines *= lines;
	lines *= lines;
    
    if (display == 0) {
        gl_FragColor = vec4(abs(uv)*0.5, debug, 1.0);
    } else if (display == 1) {
	    gl_FragColor = vec4(waves, waves, waves, 1.0);
    } else {
        gl_FragColor = vec4(lines, lines, lines, 1.0);
    }
}
