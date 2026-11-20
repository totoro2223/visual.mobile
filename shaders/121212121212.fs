/*{
    "DESCRIPTION": "Advanced Sharp Data Matrix Wall",
    "CREDIT": "Generated for Media Art",
    "ISFVSN": "2",
    "CATEGORIES": [ "Generator" ],
    "INPUTS": [
        {
            "NAME": "columns",
            "TYPE": "float",
            "MIN": 20.0,
            "MAX": 200.0,
            "DEFAULT": 70.0
        },
        {
            "NAME": "char_spacing",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 0.4,
            "DEFAULT": 0.15
        },
        {
            "NAME": "density",
            "TYPE": "float",
            "MIN": 0.1,
            "MAX": 1.0,
            "DEFAULT": 0.7
        },
        {
            "NAME": "update_speed",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 30.0,
            "DEFAULT": 8.0
        },
        {
            "NAME": "fall_speed",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 10.0,
            "DEFAULT": 2.0
        },
        {
            "NAME": "trail_length",
            "TYPE": "float",
            "MIN": 0.2,
            "MAX": 3.0,
            "DEFAULT": 1.0
        },
        {
            "NAME": "audio_level",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.0
        },
        {
            "NAME": "glitch_intensity",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.15
        },
        {
            "NAME": "color_head",
            "TYPE": "color",
            "DEFAULT": [1.0, 1.0, 1.0, 1.0]
        },
        {
            "NAME": "color_trail",
            "TYPE": "color",
            "DEFAULT": [0.0, 0.8, 0.2, 1.0]
        },
        {
            "NAME": "color_bg",
            "TYPE": "color",
            "DEFAULT": [0.0, 0.0, 0.0, 1.0]
        }
    ]
}*/

// 난수 생성 함수
float rand(vec2 co){
    return fract(sin(dot(co.xy ,vec2(12.9898,78.233))) * 43758.5453);
}

// 가짜 숫자(Glyph) 형태를 선명하게 생성하는 함수
float getSymbol(vec2 p, vec2 id, float seed, float spacing) {
    // spacing을 이용해 글자 사이의 명확한 여백 생성 (뭉개짐 방지)
    if(p.x < spacing || p.x > 1.0 - spacing || p.y < spacing || p.y > 1.0 - spacing) return 0.0;
    
    // 내부를 3x5 픽셀 그리드로 분할하여 선명한 도트 형태 생성
    vec2 subP = floor((p - spacing) / (1.0 - 2.0 * spacing) * vec2(3.0, 5.0));
    
    // 시드 조합으로 특정 도트 활성화 (날카로운 step 함수 사용)
    float r = rand(vec2(seed, subP.x * 13.0 + subP.y * 7.0));
    return step(0.45, r); 
}

void main() {
    vec2 uv = isf_FragNormCoord;

    // --- 오디오 글리치 효과 ---
    // 오디오 진폭에 따라 가로 방향으로 데이터가 찢어지는 효과
    float glitchLine = step(0.9, rand(vec2(TIME * 5.0, floor(uv.y * 40.0))));
    float glitchOffset = glitchLine * (rand(vec2(TIME, uv.y)) - 0.5) * glitch_intensity * audio_level;
    uv.x += glitchOffset;

    // --- 16:9 해상도 보정 및 그리드 분할 ---
    float aspectRatio = 16.0 / 9.0;
    float charProportion = 1.3; // 숫자가 세로로 길쭉한 폰트 느낌 유지
    vec2 gridCount = vec2(columns, columns / aspectRatio * charProportion);
    
    vec2 gridUV = uv * gridCount;
    vec2 id = floor(gridUV);       
    vec2 cellUv = fract(gridUV);   

    // --- 밀도 조절 (density) ---
    // 특정 세로줄(Column)에 데이터를 띄울지 말지 결정하여 여백을 만듦
    float colSeed = rand(vec2(id.x, 0.123));
    float isActiveCol = step(1.0 - density, colSeed);

    // --- 폭포수(Matrix Rain) 꼬리 계산 ---
    // 세로줄마다 떨어지는 속도를 다르게 설정
    float colSpeed = fall_speed * (0.3 + 0.7 * rand(vec2(id.x, 0.8)));
    float dropHeadPos = -TIME * colSpeed * 25.0; // 떨어지는 머리 위치
    
    // 현재 셀(id.y)이 머리 부분에서 위로 얼마나 떨어져 있는지 계산
    float distFromHead = id.y - dropHeadPos;
    float cycle = 40.0 * trail_length; // 꼬리의 총 길이
    
    // 0.0 (꼬리 끝, 어두움) ~ 1.0 (머리, 밝음) 그라데이션 생성
    float trail = 1.0 - fract(distFromHead / cycle);
    
    // 가장 선두에 있는 1~2개 글자만 따로 분리하여 강조 (Head)
    float isHead = step(0.95, trail); 

    // --- 데이터 업데이트 ---
    // 시간에 따라, 오디오 레벨에 따라 글자가 바뀜
    float currentUpdateSpeed = update_speed + (audio_level * 15.0);
    float timeStep = floor(TIME * currentUpdateSpeed + id.y * 0.1);
    
    // 숫자 렌더링
    float charSeed = rand(id + timeStep);
    float shape = getSymbol(cellUv, id, charSeed, char_spacing);

    // --- 최종 렌더링 및 색상 합성 ---
    // 빈 공간은 렌더링하지 않음
    float intensity = trail * shape * isActiveCol;
    
    // 기본적으로 꼬리 색상(color_trail)을 깔아줌
    vec4 finalColor = mix(color_bg, color_trail, intensity);
    
    // 가장 앞장서서 떨어지는 선두 글자는 밝은 색상(color_head)으로 덮어씌움
    finalColor = mix(finalColor, color_head, isHead * shape * isActiveCol);

    gl_FragColor = finalColor;
}