# Fase 2 — Monitor y rendimiento

Esta fase parte de la versión de Fase 1 que ya compila y ejecuta.

## Cambios
- Detección de todos los monitores mediante `MonitorService`.
- Selección automática del monitor principal.
- Selector de monitor en la interfaz.
- `OutputProfileService` calcula resolución, FPS y bitrate.
- `ScrcpyService` recibe el perfil calculado sin romper el constructor ni `StartOptimized`.
- La configuración de audio de Fase 1 se conserva.
- Las operaciones de archivos usan `System.IO` explícitamente cuando corresponde.

## Arquitectura de rutas
Las rutas de herramientas siguen centralizadas en `NovoraPaths`. Fase 2 no crea rutas alternativas para ADB, Scrcpy o Gnirehtet.
