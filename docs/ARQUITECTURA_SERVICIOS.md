# NOVORA-LINK 1.3 â€” arquitectura de servicios

## Objetivo

Mantener un solo camino por responsabilidad y evitar polling ADB duplicado.

## Flujo principal


## Servicios activos

- `AdbService`: Ãºnico punto para ejecutar ADB, cachea el listado de dispositivos durante 2 segundos y agrupa identidad bÃ¡sica del telÃ©fono.
- `DeviceIdentityService`: nombre visible del dispositivo sin exponer serial o IP en la lista.
- `DeviceStateService`: cachÃ© compartida para RED y Rendimiento.
- `NetworkService`: estado de Internet y latencia.
- `DeviceMetricsService`: CPU, RAM y baterÃ­a.
- `ScrcpyService`: una sesiÃ³n de vÃ­deo por dispositivo.
- `OutputProfileService`: resoluciÃ³n, FPS y bitrate de salida.
- `UpdateService`: consulta releases oficiales de `aroonvaldes-star/NOVORA-LINK`, descarga solo por HTTPS y verifica SHA-256.
- `SettingsService`: preferencias locales de NOVORA.

## Paneles 1.3

La interfaz base conserva RED y Rendimiento como paneles integrados. Se retirÃ³ el framework de widgets anterior para eliminar tipos duplicados, polling paralelo y estados ambiguos.

## Polling

La actualizaciÃ³n de RED y Rendimiento usa un Ãºnico `DispatcherTimer` de 30 segundos. `DeviceStateService` reutiliza datos recientes y se invalida solo al cambiar conexiÃ³n o dispositivo.
