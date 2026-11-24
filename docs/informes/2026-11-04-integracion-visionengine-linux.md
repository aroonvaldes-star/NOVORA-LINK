# Bitácora de integración de VisionEngine en Linux — 2026-11-04

## Alcance

Registro del avance en `src/NOVORA-LINUX` para integrar VisionEngine con LINK
Control 1.4.124. No se modificó el cliente Windows.

## Cambios registrados

- Se corrigió el smoke test del decoder FFmpeg: `Task.Delay` ahora recibe un
  `TimeSpan` y el token de cancelación, eliminando un error de compilación que
  impedía ejecutar las pruebas.
- Se actualizaron la pantalla de VisionEngine y el README para reflejar que la
  ruta AppControl USB ya implementa video por FFmpeg/SDL y captura/reproducción
  de audio PCM estéreo S16LE a 48 kHz. La documentación aclara que esta ruta
  todavía no está validada con un teléfono físico.
- Se mantienen explícitamente como pendientes el control remoto de teclado y
  ratón, VisionEngine por LAN, grabación, telemetría y perfiles configurables.

## Validación

- `dotnet build src/NOVORA-LINUX/NOVORA.Linux.csproj -c Release --no-restore`:
  compilación exitosa, sin advertencias ni errores.
- `dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release --no-restore`:
  pruebas de protocolo/confianza y smoke test de FFmpeg exitosos.
- SDL mostró advertencias del driver Mesa/Zink durante las pruebas con driver
  dummy; no impidieron que las pruebas terminaran correctamente.
- `adb devices -l` no reportó dispositivos conectados; no se pudo hacer una
  validación física de video o audio con LINK Control 1.4.124.

## Estado

La integración Linux tiene verificación de compilación y smoke tests locales,
pero no debe considerarse completa hasta validar USB y LAN en hardware y terminar
las funciones pendientes indicadas arriba.

## Avance: VisionEngine LAN nativo

- Se implementó en `src/NOVORA-LINUX` la oferta LINK Control v1 para `startVideoLan`,
  con listeners TCP efímeros independientes para video, control y audio opcional.
  Los canales usan TLS con el certificado fijado por el almacén de confianza actual
  y un token aleatorio por canal; los saludos verifican versión, sesión, canal y
  token en tiempo constante.
- La sesión LAN comparte el bloqueo exclusivo de VisionEngine con USB, aplica un
  vencimiento de 120 segundos, limita el handshake por cliente, admite audio
  degradado si no conecta y detiene/cierra el motor al cerrar el control LAN.
- El decoder FFmpeg aplica la rotación del encabezado VE. No se integraron entrada
  remota, transferencia de archivos, grabación ni perfiles configurables.

## Validación de este avance

- `dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj
  --configuration Release`: exitoso; incluye oferta inválida/vencida, saludo TLS
  con token incorrecto y correcto, cancelación, `startVideoLan`/`stopVideo`,
  exclusión LAN/USB y smoke H.264 con rotación.
- Las pruebas LAN usan loopback simulado. No hay teléfono conectado para confirmar
  compatibilidad física con LINK Control 1.4.124; no se modificó Windows ni Android.
- SDL/Mesa puede emitir advertencias Zink no bloqueantes; `opengl` en este host
  usa `llvmpipe`, no es evidencia de aceleración GPU física.
