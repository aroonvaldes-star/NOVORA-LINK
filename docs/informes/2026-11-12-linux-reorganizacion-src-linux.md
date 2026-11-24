# Linux · Reorganización a `src/LINUX` — 2026-11-12

## Alcance

Movimiento de la aplicación Linux de `src/NOVORA-LINUX` a `src/LINUX` y commit de los cambios de visión pendientes. Rama `swiss-retheme`. No se hizo push a ninguna rama.

## Commits

1. `cb3de3a` — `refactor(linux): mover src/NOVORA-LINUX a src/LINUX`. Renombra la aplicación. Incluye ajustes que ya estaban en el índice (csproj, README, `MainWindow.Connection`/`Home`, pruebas) y `MainWindow.Streaming.cs`.
2. `640dff9` — `feat(linux): visión con ventana SDL, colocación y diagnóstico de FILES`. Añade `LinuxSdlVideoDriver`, `LinuxVisionWindowPlacement` y `LinuxFileDiagnostics`; el título de la ventana VE refleja el estado de FILES; carpeta de recepción `NOVORA-Files` con migración de la antigua; ajuste `AudioSource`; pruebas nuevas en `VisionFilesChecks` y `VisionSettingsChecks`.

## Verificación

- `dotnet build src/LINUX/NOVORA.Linux.csproj -c Release`: 0 errores, 0 advertencias.
- `dotnet run --project src/LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release`, con `DOTNET_ROLL_FORWARD=Major` y SDL en modo dummy/software: exit 0 en 4 ejecuciones seguidas.
- Incidencia: en 3 ejecuciones previas falló `VisionEngine FFmpeg decoder smoke test` con `TaskCanceledException` (sin frame en 20 s). No se reprodujo después. La causa no está identificada.

## Estado del repositorio

- `HEAD` coincide con el árbol de trabajo en `src/LINUX`. `src/NOVORA-LINUX` ya no está en `HEAD`.
- El índice conserva 592 cambios fuera de `src/LINUX`, de la reorganización general del repositorio. No están incluidos en estos commits.

## Pendientes

- Decidir la rama de publicación. `main` aún contiene `src/NOVORA-LINUX` y difiere de `swiss-retheme` en 139 archivos; `swiss-retheme` tiene 5 commits que `main` no tiene.
- Revisar el fallo intermitente de `FFmpeg decoder smoke test`.
- Compilar y probar Windows y Android; no se verificaron en esta sesión.
- Documentos históricos (`docs/informes/` y `docs/superpowers/` de noviembre) siguen citando `src/NOVORA-LINUX`. Se dejan tal cual como registro de lo que se hizo en cada fecha.
