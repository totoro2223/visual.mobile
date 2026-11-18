/*{
  "DESCRIPTION": "Muestra un video o una imagen. Elegí el archivo o la URL en la entrada Video.",
  "INPUTS": [
    {"NAME": "inputImage", "TYPE": "image", "LABEL": "Video"},
    {"NAME": "fit", "TYPE": "long", "LABEL": "Ajuste", "VALUES": [0, 1, 2], "LABELS": ["Cubrir", "Caber", "Estirar"], "DEFAULT": 0},
    {"NAME": "zoom", "TYPE": "float", "LABEL": "Zoom", "MIN": 0.1, "MAX": 10.0, "DEFAULT": 1.0},
    {"NAME": "offsetX", "TYPE": "float", "LABEL": "Offset X", "MIN": -1.0, "MAX": 1.0, "DEFAULT": 0.0},
    {"NAME": "offsetY", "TYPE": "float", "LABEL": "Offset Y", "MIN": -1.0, "MAX": 1.0, "DEFAULT": 0.0},
    {"NAME": "tile", "TYPE": "long", "LABEL": "Borde", "VALUES": [0, 1], "LABELS": ["Sale del lienzo (negro)", "Aparece del otro lado (mosaico)"], "DEFAULT": 0}
  ]
}*/

void main() {
    vec2 img = IMG_SIZE(inputImage);
    float ca = RENDERSIZE.x / RENDERSIZE.y, ia = img.x / max(img.y, 1.0);
    // size of the picture as a fraction of the canvas, before zoom
    vec2 s = vec2(1.0);
    if (fit == 0) s = ca > ia ? vec2(1.0, ca / ia) : vec2(ia / ca, 1.0);      // cover: fills, crops the rest
    else if (fit == 1) s = ca > ia ? vec2(ia / ca, 1.0) : vec2(1.0, ca / ia); // fit: whole picture, bars around
    // offset in canvas widths / heights; zoom out leaves room that the border mode fills
    vec2 p = (isf_FragNormCoord - 0.5 - vec2(offsetX, offsetY)) / (s * zoom) + 0.5;
    if (tile == 1) p = fract(p);
    else if (p.x < 0.0 || p.y < 0.0 || p.x > 1.0 || p.y > 1.0) { gl_FragColor = vec4(0.0, 0.0, 0.0, 1.0); return; }
    gl_FragColor = IMG_NORM_PIXEL(inputImage, p);
}
