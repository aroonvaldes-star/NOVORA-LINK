# NOVORA-LINK 1.3 — arquitectura de servicios

## Objetivo

Mantener un solo camino por responsabilidad y evitar polling ADB duplicado.

## Flujo principal

`MainWindow -> servicios NOVORA -> ADB / scrcpy / Gnirehtet -> Android`

## Servicios activos

- `AdbService`: único punto para ejecutar ADB, cachea el listado de dispositivos durante 2 segundos y agrupa identidad básica del teléfono.
- `DeviceIdentityService`: nombre visible del dispositivo sin exponer serial o IP en la lista.
- `DeviceStateService`: caché compartida para RED y Rendimiento.
- `NetworkService`: estado de Internet y latencia.
- `DeviceMetricsService`: CPU, RAM y batería.
- `ScrcpyService`: una sesión de vídeo por dispositivo.
- `GnirehtetService`: un relay/túnel controlado por NOVORA.
- `OutputProfileService`: resolución, FPS y bitrate de salida.
- `UpdateService`: consulta releases oficiales de `aroonvaldes-star/NOVORA-LINK`, descarga solo por HTTPS y verifica SHA-256.
- `SettingsService`: preferencias locales de NOVORA.

## Paneles 1.3

La interfaz base conserva RED y Rendimiento como paneles integrados. Se retiró el framework de widgets anterior para eliminar tipos duplicados, polling paralelo y estados ambiguos.

## Polling

La actualización de RED y Rendimiento usa un único `DispatcherTimer` de 30 segundos. `DeviceStateService` reutiliza datos recientes y se invalida solo al cambiar conexión o dispositivo.
