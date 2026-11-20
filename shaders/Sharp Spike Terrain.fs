/*{
    "DESCRIPTION": "Sharp Spike Terrain — контраст только для волн",
    "CREDIT": "Adapted for VDMX6 / ISF",
    "ISFVSN": "2.0",
    "CATEGORIES": [
        "Generator"
    ],
    "INPUTS": [
        {
            "NAME": "animSpeed",
            "LABEL": "Speed",
            "TYPE": "float",
            "DEFAULT": 1.0,
            "MIN": 0.0,
            "MAX": 4.0
        },
        {
            "NAME": "waveHeight",
            "LABEL": "Wave Height",
            "TYPE": "float",
            "DEFAULT": 0.4,
            "MIN": 0.5,
            "MAX": 6.0
        },
        {
            "NAME": "camBaseHeight",
            "LABEL": "Camera Height",
            "TYPE": "float",
            "DEFAULT": 1.2,
            "MIN": 0.1,
            "MAX": 5.0
        },
        {
            "NAME": "camDistance",
            "LABEL": "Camera Distance",
            "TYPE": "float",
            "DEFAULT": 3.5,
            "MIN": 1.0,
            "MAX": 8.0
        },
        {
            "NAME": "camOrbitSpeed",
            "LABEL": "Camera Orbit Speed",
            "TYPE": "float",
            "DEFAULT": 0.0,
            "MIN": -1.0,
            "MAX": 1.0
        },
        {
            "NAME": "contrast",
            "LABEL": "Contrast (Waves Only)",
            "TYPE": "float",
            "DEFAULT": 0.85,
            "MIN": 0.75,
            "MAX": 0.95
        }
    ]
}*/

// ============================================================
// Sharp Spike Terrain — ISF-версия для VDMX6
// ============================================================

vec3 mod289(vec3 x){ return x - floor(x*(1.0/289.0))*289.0; }
vec2 mod289(vec2 x){ return x - floor(x*(1.0/289.0))*289.0; }
vec3 permute(vec3 x){ return mod289(((x*34.0)+1.0)*x); }

float snoise(vec2 v){
    const vec4 C = vec4(0.211324865405187, 0.366025403784439,
                       -0.577350269189626, 0.024390243902439);
    vec2 i  = floor(v + dot(v, C.yy));
    vec2 x0 = v - i + dot(i, C.xx);
    vec2 i1 = (x0.x > x0.y) ? vec2(1.0,0.0) : vec2(0.0,1.0);
    vec4 x12 = x0.xyxy + C.xxzz;
    x12.xy -= i1;
    i = mod289(i);
    vec3 p = permute(permute(i.y + vec3(0.0,i1.y,1.0))
                           + i.x + vec3(0.0,i1.x,1.0));
    vec3 m = max(0.5 - vec3(dot(x0,x0),
                            dot(x12.xy,x12.xy),
                            dot(x12.zw,x12.zw)), 0.0);
    m = m*m; m = m*m;
    vec3 x  = 2.0*fract(p*C.www) - 1.0;
    vec3 h  = abs(x) - 0.5;
    vec3 ox = floor(x + 0.5);
    vec3 a0 = x - ox;
    m *= 1.79284291400159 - 0.85373472095314*(a0*a0 + h*h);
    vec3 g;
    g.x  = a0.x*x0.x  + h.x*x0.y;
    g.yz = a0.yz*x12.xz + h.yz*x12.yw;
    return 130.0*dot(m,g);
}

// ---------- Высота пика ----------
float getBarHeight(vec2 gridPos){
    float n = 0.0;
    n += snoise(gridPos*0.6 + TIME*0.15*animSpeed)  * 1.0;
    n += snoise(gridPos*1.5 - TIME*0.10*animSpeed)  * 0.5;
    n += snoise(gridPos*4.0 + TIME*0.05*animSpeed)  * 0.25;
    return pow(clamp(n * 0.5 + 0.5, 0.0, 1.0), 2.0);
}

// ---------- Scene map: поле пиков ----------
float map(vec3 p){
    float spacing = 0.07;                       
    vec2  gid     = floor(p.xz/spacing + 0.5);  
    vec2  gcenter = gid * spacing;              

    float h   = getBarHeight(gcenter) * waveHeight + 0.1; 
    float hw  = spacing * 0.45;                 

    vec3 local = p - vec3(gcenter.x, 0.0, gcenter.y);

    float y = clamp(local.y / h, 0.0, 1.0);
    float current_hw = hw * (1.0 - y);

    vec3 q = vec3(abs(local.x), local.y - h*0.5, abs(local.z)) - vec3(current_hw, h*0.5, current_hw);
    
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

// ---------- Нормаль ----------
vec3 getNormal(vec3 p){
    vec2 e = vec2(0.002, 0.0);
    return normalize(vec3(
        map(p+e.xyy) - map(p-e.xyy),
        map(p+e.yxy) - map(p-e.yxy),
        map(p+e.yyx) - map(p-e.yyx)
    ));
}

// ---------- Основная функция ----------
void main(){
    vec2 uv = (gl_FragCoord.xy - 0.5*RENDERSIZE) / RENDERSIZE.y;

    // --- Камера ---
    float camAngle  = TIME * camOrbitSpeed;
    float camRadius = camDistance;
    float camHeight = camBaseHeight + sin(TIME*0.3)*0.1;

    vec3 ro = vec3(sin(camAngle)*camRadius,
                   camHeight,
                   cos(camAngle)*camRadius);
    vec3 target  = vec3(0.0, waveHeight*0.2, 0.0);
    vec3 forward = normalize(target - ro);
    vec3 right   = normalize(cross(vec3(0.0,1.0,0.0), forward));
    vec3 up      = cross(forward, right);

    float fov = 1.2;
    vec3 rd = normalize(forward*fov + right*uv.x + up*uv.y);

    // --- Raymarching ---
    float t = 0.0;
    const float MAX_DIST = 10.0;
    const int   MAX_STEPS = 200;
    vec3 col = vec3(0.02); // Цвет фона (не будет затронут контрастом)      
    bool hit = false;

    for(int i = 0; i < MAX_STEPS; i++){
        vec3 pos = ro + rd*t;
        float d  = map(pos);

        if(d < 0.001){
            hit = true;
            vec3 n = getNormal(pos);

            vec3  lightDir = normalize(vec3(0.3, 0.9, 0.2)); 
            float diff     = max(dot(n, lightDir), 0.0);
            diff = pow(diff, 1.5); 
            
            float spec = pow(max(dot(reflect(-lightDir,n), -rd),0.0), 64.0);
            
            float ao = clamp(pos.y / (waveHeight + 0.1), 0.05, 1.0); 
            
            float fog = exp(-t * 0.15);

            float brightness = (0.02 + diff*0.9 + spec*0.4) * fog * ao;

            // --- ПРИМЕНЕНИЕ КОНТРАСТА ТОЛЬКО ДЛЯ ПИКОВ ---
            brightness = (brightness - 0.5) * contrast + 0.5;
            brightness = clamp(brightness, 0.0, 1.0);

            col = vec3(brightness);
            break;
        }

        t += max(d*0.7, 0.002); 
        if(t > MAX_DIST) break;
    }

    // Виньетирование (применяется ко всему кадру, включая фон)
    vec2 vigUV = gl_FragCoord.xy / RENDERSIZE;
    float vig = 1.0 - dot(vigUV-0.5, vigUV-0.5)*0.8;
    col *= vig;

    gl_FragColor = vec4(col, 1.0);
}