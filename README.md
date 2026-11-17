# Visual

Visualizador de shaders ISF (`.fs`) que reacciona al audio.

- `src/Visual.Core`: lógica compartida (parser ISF, modificadores, proyectos, análisis de audio, efectos). Se va a reutilizar en Android.
- `src/Visual.Desktop`: versión Windows (NAudio + Silk.NET/OpenGL + ImGui).
- `shaders/`: shaders de ejemplo; se copian junto al ejecutable.

## Ejecutar

```
dotnet run --project src/Visual.Desktop
```

Verificaciones: `Visual.Desktop.exe --check` (código de salida 0 = ok).

## Teclas

- `F1`: mostrar u ocultar el panel
- `F11`: pantalla completa (sin bordes, en el monitor actual)
- `N` o `→`: siguiente escena

Hacé doble clic en una escena para reproducirla.

## Datos

Los proyectos (un JSON por proyecto) y la configuración se guardan en `%APPDATA%\Visual`. Cada escena guarda solo el nombre del archivo `.fs`, así el proyecto se puede copiar a otra máquina que tenga los mismos shaders.

## Pendiente

- Multipase (`PASSES`): por ahora solo se dibuja el primer pase y se muestra un aviso.
- Inputs `image`: reciben una textura negra.
- Captura de audio de una sola aplicación.
- Versión Android.
