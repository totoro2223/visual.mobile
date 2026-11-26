/*
{
  "CREDIT": "by mojovideotech, modified",
  "CATEGORIES" : [
    "generator",
    "voronoi"
  ],
  "DESCRIPTION" : "Interactive Chladni Patterns with Speed Controls",
  "INPUTS" : [
    {
      "LABEL": "Mouse X",
      "NAME": "mX",
      "TYPE": "float",
      "DEFAULT": 0.5,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "LABEL": "Mouse Y",
      "NAME": "mY",
      "TYPE": "float",
      "DEFAULT": 0.5,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "LABEL": "Morph Speed",
      "NAME": "morphSpeed",
      "TYPE": "float",
      "DEFAULT": 1.0,
      "MIN": -5.0,
      "MAX": 5.0
    },
    {
      "LABEL": "Scroll X Speed",
      "NAME": "scrollX",
      "TYPE": "float",
      "DEFAULT": 0.0,
      "MIN": -2.0,
      "MAX": 2.0
    },
    {
      "LABEL": "Scroll Y Speed",
      "NAME": "scrollY",
      "TYPE": "float",
      "DEFAULT": 0.0,
      "MIN": -2.0,
      "MAX": 2.0
    },
    {
      "LABEL": "Line Thickness",
      "NAME": "thickness",
      "TYPE": "float",
      "DEFAULT": 0.05,
      "MIN": 0.001,
      "MAX": 0.3
    },
    {
      "LABEL": "Line Color",
      "NAME": "lineColor",
      "TYPE": "color",
      "DEFAULT": [1.0, 1.0, 1.0, 1.0]
    },
    {
      "LABEL": "Background Color",
      "NAME": "bgColor",
      "TYPE": "color",
      "DEFAULT": [0.0, 0.0, 0.0, 1.0]
    }
  ],
  "ISFVSN" : 2.0
}
*/

vec3 iResolution = vec3(RENDERSIZE, 1.);
float iTime = TIME;
vec2 iMouse = vec2(mX*RENDERSIZE.x, mY*RENDERSIZE.y);

const float pi = 3.1415926535897932384;

float a;
float b = 1.0;

// Chladni eigenmodes
float chladni( float m, float n, vec2 uv )
{	
	return cos(n*pi*uv.x/a)*cos(m*pi*uv.y/b);
}

void mainImage( out vec4 fragColor, in vec2 fragCoord )
{
	// Domain [0,1]x[0,1]
	vec2 uv = fragCoord.xy / iResolution.y;
	
    // 1. 라인의 이동 속도 (Scrolling 제어)
    // 시간에 scrollX, scrollY 값을 곱하여 uv 좌표를 이동시킵니다.
    uv.x += iTime * scrollX;
    uv.y += iTime * scrollY;

	a = 1.; 
	
	// Knot numbers
	vec2 mn = 21.0 * iMouse / iResolution.xy; 
	
	// 2. 패턴이 변하는 속도 (Morph Speed 제어)
    // 기존의 speed를 morphSpeed로 이름 변경하여 직관성을 높였습니다.
	float alpha = iTime * morphSpeed;
	mat2 R = mat2( cos(alpha), sin(alpha), -sin(alpha), cos(alpha) );
	vec2 c = R * vec2(1.0,-1.0);	
	
	// Superposition of eigenmodes
	float u = c.x*chladni(mn.x,mn.y,uv) + c.y*chladni(mn.y,mn.x,uv);
	
	// Shift-scale from [-1,+1] to [0,1]		
	u = (0.5+u/2.0);
	
	// 3. 라인의 굵기 (Thickness 제어) 및 안티앨리어싱 적용
	// step 대신 smoothstep을 사용하여 외곽선을 부드럽게 처리했습니다.
	float mask = smoothstep(thickness + 0.01, thickness, abs(u-0.5));
	
	// 4. 색상 합성
	fragColor = mix(bgColor, lineColor, mask);
}

void main(void) {
	mainImage(gl_FragColor, gl_FragCoord.xy);
}