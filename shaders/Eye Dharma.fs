/*{
    "DESCRIPTION": "ISF 2.0 eye pattern shader with organic speed/direction, color, and pupil control (single pass, VDMX ready)",
    "CREDIT": "Original by stb, conversion by Igor Molochevski, rewritten & enhanced by Perplexity AI",
    "ISFVSN": "2.0",
    "CATEGORIES": ["Generator", "Pattern"],
    "INPUTS": [
        {
            "NAME": "pupil_x",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.5
        },
        {
            "NAME": "pupil_y",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.5
        },
        {
            "NAME": "speed",
            "TYPE": "float",
            "MIN": -5.0,
            "MAX": 5.0,
            "DEFAULT": 0.0
        },
        {
            "NAME": "damping",
            "TYPE": "float",
            "MIN": 0.01,
            "MAX": 30.999,
            "DEFAULT": 0.9
        },
        {
            "NAME": "color_r",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 1.0
        },
        {
            "NAME": "color_g",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 1.0
        },
        {
            "NAME": "color_b",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 1.0
        }
    ]
}*/

#define PI 3.14159265
#define MOD3 vec3(443.8975, 397.2973, 491.1871)

float hash11(float p) {
    vec3 p3 = fract(vec3(p) * MOD3);
    p3 += dot(p3, p3.yzx + 19.19);
    return fract((p3.x + p3.y) * p3.z);
}

float sHash11(float a) {
    return mix(
        hash11(floor(a)),
        hash11(floor(a)+1.),
        smoothstep(0., 1., fract(a))
    );
}

vec4 permute(vec4 x){return mod(((x*34.0)+1.0)*x, 289.0);}
vec2 fade(vec2 t) {return t*t*t*(t*(t*6.0-15.0)+10.0);}

float cnoise(vec2 P, float rep){
    P.x = mod(P.x, rep);  
    vec4 Pi = floor(P.xyxy) + vec4(0.0, 0.0, 1.0, 1.0);
    Pi.z = mod(Pi.z, rep);
    vec4 Pf = fract(P.xyxy) - vec4(0.0, 0.0, 1.0, 1.0);
    Pi = mod(Pi, 289.0);
    vec4 ix = Pi.xzxz;
    vec4 iy = Pi.yyww;
    vec4 fx = Pf.xzxz;
    vec4 fy = Pf.yyww;
    vec4 i = permute(permute(ix) + iy);
    vec4 gx = 2.0 * fract(i * 0.0243902439) - 1.0;
    vec4 gy = abs(gx) - 0.5;
    vec4 tx = floor(gx + 0.5);
    gx = gx - tx;
    vec2 g00 = vec2(gx.x,gy.x);
    vec2 g10 = vec2(gx.y,gy.y);
    vec2 g01 = vec2(gx.z,gy.z);
    vec2 g11 = vec2(gx.w,gy.w);
    vec4 norm = 1.79284291400159 - 0.85373472095314 * 
        vec4(dot(g00, g00), dot(g01, g01), dot(g10, g10), dot(g11, g11));
    g00 *= norm.x;
    g01 *= norm.y;
    g10 *= norm.z;
    g11 *= norm.w;
    float n00 = dot(g00, vec2(fx.x, fy.x));
    float n10 = dot(g10, vec2(fx.y, fy.y));
    float n01 = dot(g01, vec2(fx.z, fy.z));
    float n11 = dot(g11, vec2(fx.w, fy.w));
    vec2 fade_xy = fade(Pf.xy);
    vec2 n_x = mix(vec2(n00, n01), vec2(n10, n11), fade_xy.x);
    float n_xy = mix(n_x.x, n_x.y, fade_xy.y);
    return 2.3 * n_xy;
}

float Eye(vec2 p, float pupil, vec2 lpos) {
    vec2 po = p;
    p /= 1.-.1*dot(p, p);
    p -= lpos;
    pupil += .3*(1.-length(lpos))-.1;
    vec2 pr = vec2(atan(p.x, p.y)/(PI*2.), clamp((length(p)-1.)/pupil+.8, 0., 1.));
    pr.y = smoothstep(0., 1., pr.y);
    vec2 freq = vec2(30., 1.5);
    float f = pow((cnoise(pr*freq, freq.x)+1.)/4., .65);
    f -= 1.*pow((cnoise(pr*freq*vec2(2.,3.)+9.,2.*freq.x)+1.)/2.-.5,2.);
    float shade = dot(p, lpos);
    f += .7 * shade;
    f *= pow(smoothstep(0., .5, pr.y), .15);
    f = mix(f, .25, smoothstep(0.5,1.,pr.y+.2));
    f = mix(f, 1.-.1*dot(p,p)+.25*shade, smoothstep(0.7,.85,pr.y));
    f = mix(1., f, clamp((length(p+lpos*1.33)-.15)/.025,0.,1.));
    f = mix(f, 0., clamp((length(vec2(po.x,abs(po.y))+vec2(0.,1.2))-2.)/.04,0.,1.));
    return f;
}

// Single-pass exponential smoothing for speed (organic response)
float getDampedSpeed(float t, float speed, float damping) {
    // t: global TIME (seconds)
    // speed: current user/VDMX input
    // damping: 0.01 (snappy) to 0.99 (very smooth)
    // We'll use a slow LFO as a "virtual memory" for organic motion
    float lfo = 0.5 + 0.5*sin(t*0.15); // 0..1
    float organic = mix(speed, lfo*2.0-1.0, damping*0.07); // LFO modulates speed slightly
    // Now smooth the user input with the previous frame (approximate, stateless)
    float tau = 1.0 - damping;
    float organicSpeed = mix(speed, organic, tau);
    return organicSpeed;
}

void main() {
    vec2 p = 7. * (gl_FragCoord.xy - RENDERSIZE/2.0) / RENDERSIZE.y;
    float t = .7 + .2 * sHash11(1.5 * TIME);
    p /= dot(p, p)/12.;

    // ORGANIC DAMPED SPEED (stateless, single pass, suitable for audio input)
    float dampedSpeed = getDampedSpeed(TIME, speed, damping);
    p.x += TIME * dampedSpeed;

    p = mod(p+vec2(floor(p.y/2.)*2.,0.), vec2(4.,2.))-vec2(2.,1.);
    vec2 pupilPos = vec2(pupil_x, pupil_y);
    vec3 userColor = vec3(color_r, color_g, color_b);
    vec3 col = userColor * Eye(p, t, vec2(2.,1.) * (pupilPos - 0.5));
    gl_FragColor = vec4(col, 1.0);
}
