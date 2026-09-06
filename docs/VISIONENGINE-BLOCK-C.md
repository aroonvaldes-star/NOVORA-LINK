# VisionEngine Block C

Block C añade observabilidad, recuperación, control de presión y pruebas de estrés al pipeline headless construido en Blocks A y B.

## Alcance

- `Recovery/`: monitor de salud, política, ámbito de recuperación y manager con cooldown/límite de intentos.
- `Metrics/`: snapshots de video, audio, control, transporte, CPU y memoria sin crear un thread de polling permanente.
- `Performance/`: prioridades, cola acotada, backpressure, clasificación de congestión y recomendación de bitrate.
- `Stress/`: muestreo prolongado del pipeline real, progreso de frames, decode errors, desconexiones, CPU/RAM y recovery attempts.

## Recovery en Block C

Los canales de scrcpy son sockets independientes, pero una vez que un stream termina no existe una renegociación aislada del mismo socket dentro de la misma sesión. Por eso Block C clasifica el fallo por `Video`, `Audio`, `Control` o `Session`, pero la acción integrada de recuperación del harness reconstruye únicamente **la sesión VisionEngine completa**. No reinicia LinkEngine, no mata ADB global y no reinicia NOVORA.

## Renderer

Block C sigue siendo headless. `RendererVideoVE`, Direct3D y superficies visuales no existen todavía. La primera presentación de frames pertenece a Block D.

## Prueba Galaxy A56 5G

```powershell
.\scripts\Test-VisionEngine-BlockC.ps1 -DeviceSerial SERIAL -Seconds 60
```

Durante la prueba debe reproducirse video/audio en el teléfono. El harness exige progreso de frames y cero errores de decode de video antes de declarar PASS.
