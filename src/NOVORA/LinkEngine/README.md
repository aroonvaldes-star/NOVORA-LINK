# NOVORA LinkEngine â€” LE-002

Checkpoint LE-002.

## Objetivo


## Regla de nomenclatura

Todo cÃ³digo propio de LinkEngine termina en `LE`.

## Dependencia

`LinkEngineLE` recibe una instancia existente de `NOVORA.Services.AdbService`.

No se crea un segundo ADB ni se ejecuta `adb.exe` directamente desde LinkEngine.

## Flujo

LinkEngineLE -> DeviceLE -> AdbService -> adb.exe -> Android

## LE-002

- ValidaciÃ³n ADB real.
- Estado online/offline/unauthorized/error.
- DetecciÃ³n inicial USB/Wi-Fi.
- SesiÃ³n por dispositivo.
- MÃ©tricas LE.

## Respaldo local y cliente Android

Consultar [base local y APK 1.4.A4](../../../docs/BASE-LOCAL-Y-APK.md) para la política de respaldo, identidad verificada y diferencias entre compilaciones.
