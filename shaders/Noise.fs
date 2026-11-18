/*{
    "DESCRIPTION": "Low resolution drifting starfield with per-cycle random timing, square stars aligned to grid",
    "ISFVSN": "2",
    "CATEGORIES": [ "Generator" ],
    "INPUTS": [
        {
            "NAME": "scale",
            "TYPE": "float",
            "DEFAULT": 80.0,
            "MIN": 10.0,
            "MAX": 400.0
        },
        {
            "NAME": "density",
            "TYPE": "float",
            "DEFAULT": 0.15,
            "MIN": 0.01,
            "MAX": 1.0
        },
        {
            "NAME": "speed",
            "TYPE": "float",
            "DEFAULT": 0.2,
            "MIN": 0.01,
            "MAX": 5.0
        },
        {
            "NAME": "moveSpeed",
            "TYPE": "float",
            "DEFAULT": 0.01,
            "MIN": 0.0,
            "MAX": 0.2
        },
        {
            "NAME": "moveAngle",
            "TYPE": "float",
            "DEFAULT": 0.0,
            "MIN": 0.0,
            "MAX": 6.28318
        },
        {
            "NAME": "starSize",
            "TYPE": "float",
            "DEFAULT": 0.4,
            "MIN": 0.05,
            "MAX": 1.0
        }
    ]
}*/

float hash(vec2 p)
{
    p = fract(p * vec2(123.34,456.21));
    p += dot(p,p+45.32);
    return fract(p.x*p.y);
}

float hash3(vec3 p)
{
    p = fract(p*0.1031);
    p += dot(p,p.yzx+33.33);
    return fract((p.x+p.y)*p.z);
}

void main()
{
    vec2 uv = gl_FragCoord.xy / RENDERSIZE.xy;

    vec2 moveDir = vec2(cos(moveAngle), sin(moveAngle));
    uv += moveDir * TIME * moveSpeed;

    vec2 grid = floor(uv * scale);

    float seed = hash(grid);
    float star = step(1.0 - density, seed);

    float cycleTime = TIME * speed;
    float cycle = floor(cycleTime);

    float phase = seed * 6.28318;

    float localSpeed = mix(
        0.5,
        1.5,
        hash3(vec3(grid,cycle))
    );

    float t = fract(cycleTime * localSpeed);
    float fade = sin(t * 6.28318 + phase) * 0.5 + 0.5;

    vec2 cellUV = fract(uv * scale) - 0.5;
    float square = step(abs(cellUV.x)/0.5, starSize) * step(abs(cellUV.y)/0.5, starSize);

    float brightness = star * fade * square;

    gl_FragColor = vec4(vec3(brightness),1.0);
}