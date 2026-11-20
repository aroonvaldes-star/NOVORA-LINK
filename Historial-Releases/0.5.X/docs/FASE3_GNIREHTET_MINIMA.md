# NOVORA — Fase 3 mínima: Gnirehtet

Esta fase parte de la base de Fase 2.2 y agrega únicamente la integración manual de Gnirehtet.

## Comportamiento

- NOVORA no inicia Gnirehtet automáticamente.
- El usuario lo activa desde el botón de Gnirehtet cuando lo necesita.
- `INICIAR NOVORA` inicia únicamente scrcpy.
- Gnirehtet puede permanecer activo al mismo tiempo que scrcpy.
- Al finalizar la transmisión, NOVORA detiene el túnel de Gnirehtet y limpia ADB si no queda otro dispositivo conectado.
- No se incluye telemetría ni polling periódico en esta fase.

## Integración

NOVORA usa el `adb.exe` y `gnirehtet.apk` incluidos en `Tools/Runtime` mediante las variables de entorno `ADB` y `GNIREHTET_APK`.

Para un dispositivo se utiliza `gnirehtet run SERIAL`.
Para varios dispositivos se utiliza `gnirehtet relay` y `gnirehtet start SERIAL`.


## v0.5 — limpieza segura

- El botón DETENER NOVORA detiene scrcpy y Gnirehtet y limpia ADB si no queda otro dispositivo.
- El cierre de la ventana ejecuta la misma limpieza con un límite de tiempo para evitar bloqueos de salida.
- El bitrate de scrcpy queda fijo en 10M.
