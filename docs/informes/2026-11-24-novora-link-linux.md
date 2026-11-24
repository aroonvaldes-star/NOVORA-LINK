# 2026-11-24 — NOVORA LINK Linux en la copia LINKCONTROL

## Alcance

Se incorporó el cliente Linux de NOVORA LINK a la copia local, además del
módulo Android. El proyecto Linux se conserva como aplicación Avalonia
independiente y se publicó en una rama separada de `main`:

- Repositorio: `https://github.com/aroonvaldes-star/NOVORA-LINK.git`
- Rama: `linkcontrol-linux-adaptaciones`
- Commit: `222c15b` (`Adaptar NOVORA LINK Linux: Home LinkEngine, monitor de streaming y aceleración`)

La copia local contiene:

- `src/Linux/`: proyecto Linux completo (UI, control, VisionEngine, LinkEngine,
  ExInEngine, recursos, pruebas y código Rust del relay).
- `src/LinuxShared/`: subconjunto mínimo de fuentes compartidas de `src/WINDOWS`
  que el proyecto Linux enlaza. El `csproj` fue redirigido a esta carpeta para
  que la copia sea autocontenida.

## Adaptaciones aplicadas

- Home: conexión/preparación de LinkEngine (`USB · cable` / `LAN · Wi-Fi`),
  estado del teléfono y acceso a los tres motores.
- Página LinkEngine: se quitaron los controles de conexión; conserva estado,
  mediciones y pruebas técnicas.
- Streaming: monitor en tiempo real con cuatro señales verificables (FPS,
  bitrate, duración y salud), opciones avanzadas y backend RTMP/OBS WebSocket v5.
- Aceleración: se eliminó `Forced`; se conserva el backend elegido por el perfil
  personalizado y el selector queda limitado a dos opciones.
- Se corrigió la asignación a una lista de solo lectura en
  `NLStreamingService.BuildFfmpegArguments` y el tipado de candidatos de
  dispositivo en `LinuxVisionAcceleration`.

## Compilación

Dentro del clon de origen, en la rama `linkcontrol-linux-adaptaciones`:

- `dotnet build src/LINUX/NOVORA.Linux.csproj -c Debug -t:Rebuild` → 0 errores, 0 advertencias.
- `dotnet build src/LINUX/NOVORA.Linux.csproj -c Release -t:Rebuild` → 0 errores, 0 advertencias.

Dentro de esta copia local:

- `dotnet build src/Linux/NOVORA.Linux.csproj -c Debug -t:Rebuild` → 0 errores, 0 advertencias.
- `dotnet build src/Linux/NOVORA.Linux.csproj -c Release -t:Rebuild` → 0 errores, 0 advertencias.

## Relay Rust

El binario `novora-linkengine-relay` sí se generó en este entorno. El caché Git
de `cargo` guardaba referencias temporales inválidas (`refs/commit/.l2s.…`); se
sorteó clonando `tcp_ip` por separado y aplicando un `patch` temporal de Cargo.
Como tampoco había `cc`, se extrajo una cadena de herramientas mínima en
`~/.local/toolchain` y se compiló con un `--sysroot` aislado. El script
`build-relay.sh` reproduce el proceso.

El `csproj` de la copia local solo incluye/copia el relay cuando el binario
existe. Para recompilarlo todo:

```bash
./build-relay.sh
dotnet build src/Linux/NOVORA.Linux.csproj -c Release -t:Rebuild
```

No se validó una sesión real de LinkEngine, RTMP ni OBS.

## Accesos directos

En `~/Desktop` se dejaron:

- `LINKCONTROL Android · Recompilar.desktop`
- `NOVORA LINK Linux · Recompilar.desktop`
- `NOVORA LINK Linux · Ejecutar.desktop`

Todos los `.desktop` pasaron `desktop-file-validate`.
