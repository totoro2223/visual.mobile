/*{
    "ISFVSN": "2.0",
    "LABEL": "Maze Lattice",
    "DESCRIPTION": "A geometric maze-like lattice with hexagonal Truchet patterns and edge shading, converted from Shadertoy for VDMX6 (ISF/Metal).",
    "CATEGORIES": ["Generator"],
    "VSN": "1.0",
    "INPUTS": []
}*/

// Maximum ray distance.
#define FAR 40.

float map(in vec3 p) {
   vec3 ip = floor(p) + .5;
   p -= ip;
   vec3 q = abs(p); 
   q = step(q.yzx, q.xyz)*step(q.zxy, q.xyz)*sign(p);
   q.x = fract(sin(mod(dot(ip + q*.5, vec3(111.67, 147.31, 27.53)), 6.2831))*43758.5453);
   p.xy = abs(q.x>.333 ? q.x>.666 ? p.xz : p.yz : p.xy);
   return max(p.x, p.y) - .2;   
}

float trace(vec3 ro, vec3 rd){
    float t = 0.0;
    for(int i=0; i<72; i++){
        float d = map(ro + rd*t);
        if (abs(d)<.002*(t*.05 + 1.) || t>FAR) break;
        t += d;
    } 
    return min(t, FAR);
}

float refTrace(vec3 ro, vec3 rd){
    float t = 0.0;
    for(int i=0; i< 16; i++){
        float d = map(ro + rd*t);
        if (abs(d) < 0.005*(t*.25 + 1.) || t>FAR) break;
        t += d;
    } 
    return t;
}

vec3 normal(in vec3 p, inout float edge) { 
    vec2 e = vec2(.034, 0);
    float d1 = map(p + e.xyy), d2 = map(p - e.xyy);
    float d3 = map(p + e.yxy), d4 = map(p - e.yxy);
    float d5 = map(p + e.yyx), d6 = map(p - e.yyx);
    float d = map(p)*2.;
    edge = abs(d1 + d2 - d) + abs(d3 + d4 - d) + abs(d5 + d6 - d);
    edge = smoothstep(0., 1., sqrt(edge/e.x*8.));
    float crv = (d1 + d2 + d3 + d4 + d5 + d6 - d*3.)/e.x;
    if (crv<0.) edge = 0.;
    e = vec2(.005, 0);
    d1 = map(p + e.xyy), d2 = map(p - e.xyy);
    d3 = map(p + e.yxy), d4 = map(p - e.yxy);
    d5 = map(p + e.yyx), d6 = map(p - e.yyx); 
    return normalize(vec3(d1 - d2, d3 - d4, d5 - d6));
}

float calcAO( in vec3 p, in vec3 n ) {
    float ao = 0.0, l;
    const float nbIte = 12.0;
    const float falloff = 1.;
    const float maxDist = 1.;
    for( float i=1.; i< nbIte+.5; i++ ){
        l = (i + fract(cos(i)*45758.5453))*.5/nbIte*maxDist;
        ao += (l - map( p + n*l ))/ pow(1. + l, falloff);
    }
    return clamp( 1. - ao*2./nbIte, 0., 1.);
}

float softShadow(vec3 ro, vec3 lp, float k){
    const int maxIterationsShad = 24; 
    vec3 rd = (lp-ro);
    float shade = 1.0;
    float dist = 0.0;    
    float end = max(length(rd), 0.001);
    rd /= end;
    for (int i=0; i<maxIterationsShad; i++){
        float h = map(ro + rd*dist);
        shade = min(shade, k*h/dist);
        dist += clamp(h, 0.02, 0.25);
        if (h<0.0 || dist > end) break; 
    }
    return min(max(shade, 0.) + 0.3, 1.0); 
}

float hexTruchet(in vec2 p) { 
    p *= 6.;
    vec2 h = vec2(p.x + p.y*.577350269, p.y*1.154700538);
    vec2 f = fract(h); h -= f;
    float c = fract((h.x + h.y)/3.);
    h =  c<.666 ?   c<.333 ?  h  :  h + 1.  :  h  + step(f.yx, f); 
    p -= vec2(h.x - h.y*.5, h.y*.8660254);
    c = fract(cos(mod(dot(h, vec2(41.13, 289.57)), 6.2831))*43758.5453);
    p -= p*step(c, .5)*2.;
    p -= vec2(-1, 0);
    c = dot(p, p);
    p -= vec2(1.5, .8660254);
    c = min(c, dot(p, p));
    p -= vec2(0, -1.73205);
    c = min(c, dot(p, p));
    return sqrt(c);
}

float bumpFunc(vec3 p, vec3 n){
    n = abs(n);
    p.xy = n.x>.5? p.yz : n.y>.5? p.xz : p.xy; 
    vec2 sc = cos(mod(hexTruchet(p.xy)*6.2831*vec2(2, 4), 6.2831));
    return clamp(dot(sc, vec2(.6)) + .5, 0., 1.);
}

vec3 bumpMap(in vec3 p, in vec3 n, float bumpfactor){
    const vec2 e = vec2(.002, 0);
    float ref = bumpFunc(p, n);  
    vec3 grad = (vec3(bumpFunc(p - e.xyy, n),
                      bumpFunc(p - e.yxy, n),
                      bumpFunc(p - e.yyx, n) ) - ref)/e.x;                     
    grad -= n*dot(n, grad);          
    return normalize(n + grad*bumpfactor);
}

float bumpFunc2(vec3 p, vec3 n){
    p = abs(fract(p*3.) - .5);
    float c = max(max(p.x, p.y), p.z);
    return 1. - smoothstep(0., .025, c - .47);
}

vec3 bumpMap2(in vec3 p, in vec3 n, float bumpfactor){
    const vec2 e = vec2(.002, 0);
    float ref = bumpFunc2(p, n);  
    vec3 grad = (vec3(bumpFunc2(p - e.xyy, n),
                      bumpFunc2(p - e.yxy, n),
                      bumpFunc2(p - e.yyx, n) ) - ref)/e.x;                     
    grad -= n*dot(n, grad);          
    return normalize(n + grad*bumpfactor);
}

float n2D(vec2 p) {
    vec2 i = floor(p); p -= i; p *= p*(3. - p*2.);  
    return dot(mat2(fract(sin(mod(vec4(0, 41, 289, 330) + dot(i, vec2(41, 289)), 6.2831))*
               43758.5453))*vec2(1. - p.y, p.y), vec2(1. - p.x, p.x) );
}

vec3 texFaces(in vec3 p, in vec3 n){
    n = abs(n);
    p.xy = n.x>.5? p.yz : n.y>.5? p.xz : p.xy; 
    n = mix(vec3(.3, .1, .02), vec3(.35, .5, .65), n2D(p.xy*8.)*.66 + n2D(p.xy*16.)*.34);
    n *= n2D(p.xy*512.)*1.2 + 1.4;
    vec2 sc = cos(mod(hexTruchet(p.xy)*6.2831*vec2(2, 4), 6.2831));
    n *= clamp(dot(sc, vec2(.6))+.5, 0., 1.)*.95 + .05;
    return min(n, 1.);
}

vec3 texEdges(in vec3 p, in vec3 n){
    float bf = bumpFunc2(p, n);
    n = abs(n);
    p.xy = n.x>.5? p.yz : n.y>.5? p.xz : p.xy; 
    n = mix(vec3(.3, .1, .02), vec3(.35, .5, .65), n2D(p.xy*8.)*.66 + n2D(p.xy*16.)*.34);
    n *= n2D(p.xy*512.)*.85 + .15; 
    n = min((n + .35)*vec3(1.05, 1, .9), 1.);
    n *= bf*.75+.25;
    return n;
}

void main() {
    vec3 rd = vec3(2.*gl_FragCoord.xy - RENDERSIZE.xy, RENDERSIZE.y);
    rd = normalize(vec3(rd.xy, sqrt(max(rd.z*rd.z - dot(rd.xy, rd.xy)*.2, 0.))));
    vec2 m = sin(vec2(1.57079632, 0) + TIME/4.);
    rd.xy = rd.xy*mat2(m.xy, -m.y, m.x);
    rd.xz = rd.xz*mat2(m.xy, -m.y, m.x);
    vec3 ro = vec3(0, 0, TIME);
    vec3 lp = ro + vec3(.2, 1., .3);
    vec3 col = vec3(0);
    float t = trace(ro, rd);
    float edge;
    vec3 sp = ro + rd*t;
    vec3 sn = normal(sp, edge);
    vec3 svn = sn;
    if(edge<.001) sn = bumpMap(sp, sn, .01/(1. + t*.25));
    else sn = bumpMap2(sp, sn, .03/(1. + t*.25));
    vec3 ref = reflect(rd, sn);
    vec3 oCol = texFaces(sp, svn);
    if(edge>.001) oCol = texEdges(sp, svn);
    float sh = softShadow(sp + sn*.005, lp, 16.);
    float ao = calcAO(sp, sn);
    vec3 ld = lp - sp;
    float lDist = max(length(ld), 0.001);
    ld /= lDist;
    float diff = max(dot(ld, sn), 0.);
    float spec = pow(max(dot(reflect(-ld, sn), -rd), 0.), 32.);
    float atten = 1.25/(1.0 + lDist*0.1 + lDist*lDist*.05);
    float rt = refTrace(sp + sn*.005, ref);
    float rEdge;
    vec3 rsp = sp + ref*rt;
    vec3 rsn = normal(rsp, rEdge);
    vec3 rCol = texFaces(rsp, rsn);
    if(rEdge>.001)rCol = texEdges(rsp, rsn);
    float rDiff = max(dot(rsn, normalize(lp-rsp)), 0.);
    float rSpec = pow(max(dot(reflect(-normalize(lp-rsp), rsn), -ref), 0.), 8.);
    float rlDist = length(lp - rsp);
    rCol = (rCol*(rDiff*1. + vec3(.45, .4, .3)) + vec3(1., .6, .2)*rSpec*2.);
    rCol *= 1.25/(1.0 + rlDist*0.1 + rlDist*rlDist*.05);    
    col = oCol*(diff*1. + vec3(.45, .4, .3)) + vec3(1., .6, .2)*spec*2.;
    if(edge<.001) col += rCol*.2;
    else col += rCol*.35;
    col = min(col*atten*sh*ao, 1.);
    vec3 bg = mix(vec3(.5, .7, 1).zyx, vec3(1, .7, .3).zyx, -rd.y*.35 + .35);
    col = mix(col, bg, smoothstep(0., FAR-25., t));
    vec2 uv = gl_FragCoord.xy/RENDERSIZE.xy;
    float vig = pow(16.*uv.x*uv.y*(1.-uv.x)*(1.-uv.y), 0.125);
    col *= vec3(1.2, 1.1, .85)*vig;
    gl_FragColor = vec4(sqrt(clamp(col, 0., 1.)), 1.0);
}