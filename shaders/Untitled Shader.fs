/*{
  "DESCRIPTION": "Pulse train digitale minimale stile Alva Noto",
  "ISFVSN": "2.0",
  "CATEGORIES": ["Generator","Minimal"],
  "INPUTS": [
    { "NAME":"density","TYPE":"float","DEFAULT":60.0,"MIN":10.0,"MAX":200.0 },
    { "NAME":"speed","TYPE":"float","DEFAULT":2.0,"MIN":0.1,"MAX":8.0 },
    { "NAME":"pulseRatio","TYPE":"float","DEFAULT":0.2,"MIN":0.01,"MAX":0.5 },
    { "NAME":"invert","TYPE":"bool","DEFAULT":false }
  ]
}*/

void main() {
    vec2 uv = isf_FragNormCoord;

    float t = fract(TIME * speed);
    float pulse = step(t, pulseRatio);

    float g = fract(uv.x * density);

    float col = step(g, 0.015) * pulse;

    if(invert) col = 1.0 - col;

    gl_FragColor = vec4(vec3(col),1.0);
}
