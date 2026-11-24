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
  Release en un runner x86-64 y publicar los artefactos. El run `45367818371`
  terminó correctamente y publicó `linkcontrol-android-rebuild` (133 MB).
- Commit `91cf903`: documentación de compilación, cliente Linux, workflow y
  scripts. Commit `f4388f6`: bitácora y resultado del relay Rust. Ambos
  pushados a `linkcontrol-android-adaptaciones`.

## 2026-11-24 — Contraste con LINK-CONTROL

- Se examinó `https://github.com/aroonvaldes-star/LINK-CONTROL` (rama
  `novora-appcontrol`, HEAD `207f132`). Es el compañero Android: fuente
  `1.4.113`, 37 archivos C#, sin proyecto de escritorio ni servicio
  RTMP/OBS. El selector de perfiles de la Android toma las opciones de la PC.
- Los archivos comunes divergen respecto a esta copia (por ejemplo,
  `NLControlCapturePolicy.cs` con 491 líneas diferentes), así que no se hizo
  cherry-pick ni se modificó ese repositorio.
- El artefacto del run `45367818371` incluye una APK Release firmada por CI,
  versión `1.4.128`, SHA-256
  `a15d4999075539710d60bef33663f89386c8137a4c0f5b460328484e553c2f20`. No es
  publicable como oficial: usa firma de depuración y la versión ya publicada;
  harían falta `1.4.129`, la clave autorizada y los ficheros de verificación.
- Contraste completo en
  `docs/informes/2026-11-24-contraste-link-control.md`.

## 2026-11-24 — Compatibilidad de arranque y sesión

- Se contrastó el orden real de arranque: Avalonia crea la ventana, inicia la
  conexión automática (solo prepara rutas USB/LAN) y los motores siguen
  iniciándose bajo petición. VisionEngine resuelve el backend al construir el
  decodificador dentro de una sesión autorizada; LinkEngine sigue ligado a la
  ruta y a `LinkMeasurements`; el monitor de streaming solo arranca desde el
  botón de Streaming.
- En Android (`LINK-CONTROL`, HEAD `207f132`) `OnStart` enlaza el servicio y
  suscribe sesión/estado, `OnResume` vuelve a renderizar el monitor y
  `ApplyState` consume `snapshot.Profiles`/`snapshot.Acceleration` como texto.
  Las formas de `NLControlAcceleration` y `NLControlSnapshot` son idénticas en
  ambos repositorios, así que no hace falta tocar la APK actual.
- Se reforzó el contrato compartido: `Custom` sin perfil válido devuelve
  software en lugar de reutilizar la opción automática. Se actualizaron las dos
  copias de `NLAccelerationProfile.cs` (`src/Shared` y `src/LinuxShared`).
- Se reescribió `src/Linux/tests/VisionAccelerationChecks.cs` para el contrato
  sin `Forced`: recorre 3 plataformas × 5 proveedores × 6 perfiles × 3 modos y
  verifica que toda selección está dentro de las opciones ofrecidas. El bloque
  de aceleración pasa; la suite seguía y fallaba después en
  `VisionExchangeChecks.cs:214` (`Imagenes/a (1).png`), archivo no tocado por
  esta rama: se registra como fallo preexistente del entorno, no como regresión.
- Se corrigieron las rutas relativas de
  `src/Linux/tests/NOVORA.Linux.ProtocolTests.csproj` (`../../Android` y
  `../../LinuxShared`). Release del cliente Linux: 0 errores y 0 advertencias.
- Informe completo:
  `docs/informes/2026-11-24-compatibilidad-arranque-sesion.md`.

## Pendiente

- Decidir si se publica la APK recompilada; la incluida en `apk/` sigue siendo
  la pública original.
- Validación física: GPU Intel/NVIDIA/AMD, sesión RTMP real y conexión OBS.
- Decidir si se aplica en `LINK-CONTROL` el cambio opcional del texto de perfil
  y si se abre allí un issue; no se hizo sin confirmación.
- No se modificó `main` en ninguno de los dos repositorios locales.
