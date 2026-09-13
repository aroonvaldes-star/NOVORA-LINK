# NOVORA-LINK 1.4 A3 — Estado de integración

## MainWindow

- Estado compacto visible de Privacy, Integration, Gamepad, NVIDIA y Remote Android.
- START/STOP VisionEngine sigue controlando la sesión visual.
- LinkEngine mantiene su panel RED/estado/recovery existente.
- Los cambios guardados en Settings se aplican al runtime de VisionEngine y reconfiguran RemoteNV.

## SettingsWindow

Configuración persistente conectada al runtime:

- Privacy Shield manual.
- Clipboard Android↔Windows.
- Transferencia de archivos.
- Drag & Drop.
- Inicio remoto de aplicaciones (capability gate).
- Panel de notificaciones (capability gate).
- Resize dinámico del display (capability gate).
- Gamepad event-driven.
- Perfil NVIDIA.
- Remote Android.
- Modo/monitor/bitrate/FPS/resolución/audio existentes.

La cámara virtual Windows y micrófono PC→Android siguen marcados como no disponibles porque sus backends no existen todavía. No se presentan como funciones terminadas.

## LinkEngine corregido

- Eliminado mantenimiento periódico de `ManagerNetworkLE`.
- Eliminado refresco de estado cada 500 ms de `ManagerRuntimeLE`.
- `RelayNetworkLE` publica `ExitedLE` para degradar sesiones por evento.
- `ManagerTransportLE` publica `SessionChangedLE`.
- `MonitorRecoveryLE` es event-driven y usa deadlines one-shot.
- `adb wait-for-device` sustituye al sondeo de retorno cada 500 ms.
- Eliminadas clases de polling legacy sin referencias.

## RemoteNV

- Protocolo PC/Android v2.
- Token efímero aleatorio de 256 bits por sesión.
- `HELLO` autenticado por token.
- Token no persistido.
- Android actualizado a display version `1.4.A3`.

## Verificación

Ejecutar en Windows:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-NOVORA-Repository.ps1
```

El script valida RelayCore con Cargo y compila NOVORA Windows + Android en Release.
