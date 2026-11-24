# Bitácora de la copia LINKCONTROL

Registro cronológico de esta copia local. Los detalles técnicos y las tablas de
evidencia están en `docs/informes/`. Las bitácoras históricas del proyecto
original se conservan en `docs/informes/2026-11-04-auditoria-y-bitacoras.md`;
en el repo de origen no está disponible `BITACORA_UNIVERSAL.md`, por lo que no
se modificó ni se suplantó.

## 2026-11-24 — Copia y adaptaciones Android

- Se clonó el repo público `https://github.com/aroonvaldes-star/NOVORA-LINK.git`
  desde el commit base `5c2a2ecea670e4b15880be3d91ffc34a7c6d27ec`.
- Se creó la copia local `/home/aroon61/LINKCONTROL-Android` con fuente Android,
  Shared, adaptaciones de escritorio, `docs/` y la APK pública
  `apk/NOVORA-Android-1.4.8-experimental.apk` (SHA-256
  `e8de4485731bb35f0939bbadfecd442636f1d4076f252ae933d338cc41fe2157`).
- Aceleración: cada perfil ofrece como máximo `Automático · <backend>` y
  `Software`. Para proveedores desconocidos o perfiles de ahorro solo se ofrece
  `Software`. Se añadió el perfil personalizado del usuario, acotado a los
  backends realmente implementados.
- Streaming: se reescribió el servicio con RTMP y OBS WebSocket v5, opciones
  avanzadas y un monitor de cuatro señales verificables (FPS, bitrate, duración
  y salud). `—` significa dato no reportado, nunca `0`; tras 4 s sin progreso se
  muestra `Sin señal del codificador`.
- LinkEngine pasó al Home en WPF y Avalonia. La página LinkEngine conserva
  estado y mediciones técnicas.
- Commit `08a0092` en la rama nueva `linkcontrol-android-adaptaciones`, sin
  tocar `main`.

## 2026-11-24 — Scripts y accesos directos

- Commit `7c113bf`: `build.sh`, `desktop-build.sh` y
  `~/Desktop/LINKCONTROL Android · Recompilar.desktop`.
- Se añadieron `build-linux.sh`, `launch-linux.sh`, `build-relay.sh` y los
  accesos `NOVORA LINK Linux · Recompilar.desktop` y
  `NOVORA LINK Linux · Ejecutar.desktop`. Los tres `.desktop` pasaron
  `desktop-file-validate`.

## 2026-11-24 — NOVORA LINK Linux

- En el clon de origen se publicó la rama `linkcontrol-linux-adaptaciones` con
  el commit `222c15b`, basada en `main`: Home con LinkEngine, monitor de
  streaming, aceleración simplificada y correcciones de compilación.
- En la copia local se incorporó el cliente completo en `src/Linux` y las
  fuentes compartidas mínimas en `src/LinuxShared`, con rutas ajustadas para
  que compile de forma autocontenida.
- Debug y Release de `src/Linux/NOVORA.Linux.csproj`: 0 errores y 0
  advertencias, tanto en el clon de origen como en la copia local.
- El relay Rust `novora-linkengine-relay` compiló en Release tras fijar la
  dependencia `tcp_ip` con un clón local y habilitar una cadena C aislada en
  `~/.local/toolchain` (no había `cc` en el sistema). El binario está ignorado
  por Git y se reproduce con `./build-relay.sh`.

## 2026-11-24 — Compilación Android y seguimiento

- Se instaló el Android SDK con `sdkmanager` (API 36, Build-Tools 36.0.0) y
  JDK 21. El instalador de .NET falló por `Mono.Unix` ausente.
- El host es ARM64 y el workload trae `aapt2` y `libZipSharpNative` x86-64. Se
  usó un `aapt2` ARM64 verificado (SHA-256
  `7e5ae2e1f62fc24cab14072555ffd0a1a7e1ce27e82cc1008967b835b6d8df5b`), con el
  que el build llegó hasta `Javac`, donde se detuvo por la biblioteca x86-64.
- Se añadió `.github/workflows/android-rebuild.yml` para recompilar Debug y
  Release en un runner x86-64 y publicar los artefactos.
- Commit `91cf903`: documentación de compilación, cliente Linux, workflow y
  scripts. Push a `linkcontrol-android-adaptaciones` completado.

## Pendiente

- Confirmar el resultado del workflow de Android y, si genera paquete, republicar
  la APK como artefacto (la APK incluida sigue siendo la pública original).
- Validación física: GPU Intel/NVIDIA/AMD, sesión RTMP real y conexión OBS.
- No se modificó `main` en ninguno de los dos repositorios locales.
