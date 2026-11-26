/*
{
  "CATEGORIES": [
    "Custom",
    "Live Shader"
  ],
  "DESCRIPTION": "Shader dasar dengan grid shape animasi. Ditambahkan slider untuk kontrol rotasi, warna, ukuran, dan tampilan bentuk.",
  "INPUTS": [
    {
      "LABEL": "Animation Speed",
      "NAME": "uSpeed",
      "TYPE": "float",
      "DEFAULT": 0.3,
      "MIN": 0.0,
      "MAX": 3.0
    },
    {
      "LABEL": "Grid Scale",
      "NAME": "uScale",
      "TYPE": "float",
      "DEFAULT": 5.0,
      "MIN": 1.0,
      "MAX": 15.0
    },
    {
      "LABEL": "Shape Size",
      "NAME": "uShapeSize",
      "TYPE": "float",
      "DEFAULT": 0.35,
      "MIN": 0.01,
      "MAX": 1.0
    },
    {
      "LABEL": "Shape Thickness",
      "NAME": "uShapeThickness",
      "TYPE": "float",
      "DEFAULT": 0.04,
      "MIN": 0.001,
      "MAX": 0.2
    },
    {
      "LABEL": "Red",
      "NAME": "uRed",
      "TYPE": "float",
      "DEFAULT": 1.0,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "LABEL": "Green",
      "NAME": "uGreen",
      "TYPE": "float",
      "DEFAULT": 1.0,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "LABEL": "Blue",
      "NAME": "uBlue",
      "TYPE": "float",
      "DEFAULT": 1.0,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "LABEL": "Opacity",
      "NAME": "uOpacity",
      "TYPE": "float",
      "DEFAULT": 1.0,
      "MIN": 0.0,
      "MAX": 1.0
    },
    {
      "LABEL": "Show Triangle",
      "NAME": "uShowTriangle",
      "TYPE": "bool",
      "DEFAULT": true
    },
    {
      "LABEL": "Show Square",
      "NAME": "uShowSquare",
      "TYPE": "bool",
      "DEFAULT": true
    },
    {
      "LABEL": "Show Circle",
      "NAME": "uShowCircle",
      "TYPE": "bool",
      "DEFAULT": false
    }
  ]
}
*/

mat2 rot(float a) {
    return mat2(cos(a), sin(a), -sin(a), cos(a));
}

float triangle(vec2 uv, float size) {
    uv.x = abs(uv.x);
    return step(max(-uv.y, dot(uv, vec2(1., 0.6))), size);
}

float square(vec2 uv, float size) {
    uv = abs(uv);
    return step(max(uv.x, uv.y), size);
}

float circle(vec2 uv, float size) {
    return step(length(uv), size);
}

void main() {
    vec2 uv = 2.0 * (gl_FragCoord.xy / RENDERSIZE.xy) - 1.0;
    uv.x *= RENDERSIZE.x / RENDERSIZE.y;

    uv *= uScale; // grid scale
    vec2 guv = fract(uv) - 0.5;
    vec2 id = floor(uv);
    guv *= rot(TIME * (length(id) + 0.8) * uSpeed);

    float shape = 0.0;

    if (uShowTriangle) {
        float t = triangle(guv, uShapeSize) - triangle(guv, uShapeSize - uShapeThickness);
        shape += clamp(t, 0.0, 1.0);
    }

    if (uShowSquare) {
        float s = square(guv, uShapeSize) - square(guv, uShapeSize - uShapeThickness);
        shape += clamp(s, 0.0, 1.0);
    }

    if (uShowCircle) {
        float c = circle(guv, uShapeSize) - circle(guv, uShapeSize - uShapeThickness);
        shape += clamp(c, 0.0, 1.0);
    }

    vec3 col = shape * vec3(uRed, uGreen, uBlue);
    gl_FragColor = vec4(col, uOpacity);
}
