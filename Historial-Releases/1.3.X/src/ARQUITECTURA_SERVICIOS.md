# NOVORA — limpieza de servicios, fase 1

## Objetivo

Reducir trabajo innecesario entre NOVORA, ADB y Android evitando que cada parte de la interfaz cree su propia lógica de comunicación.

## Cambios de esta fase

### AdbService
- Es el único punto para ejecutar ADB desde la aplicación.
- El servidor ADB se inicia una sola vez por instancia del servicio.
- Las consultas simultáneas de dispositivos se serializan.
- El listado de dispositivos se reutiliza durante 2 segundos para evitar polling duplicado.
- El refresco puede forzarse cuando realmente sea necesario.
- La lectura de propiedades básicas del teléfono se agrupa en una sola llamada `adb shell`.
- El caché se invalida al cambiar la conexión ADB por Wi-Fi.

### DeviceIdentityService
- Usa el serial ADB como identidad técnica estable.
- Permite un nombre personalizado por dispositivo.
- Guarda los nombres en `%LOCALAPPDATA%\\NOVORA\\devices.json`.
- Mantiene `LastSeenUtc` por dispositivo.
- Entrega a la UI la información del dispositivo con su nombre personalizado sin duplicar la lógica de ADB.

### Widgets
Se retiraron del registro y configuración los widgets antiguos `Conexión` y `Dispositivo`.

## Regla para las siguientes fases

Los widgets no deben lanzar `adb.exe`, `scrcpy.exe` o `gnirehtet.exe` directamente para obtener información que ya pertenece a un servicio.

La ruta será:

`Widget -> Servicio NOVORA -> herramienta -> Android`

Cuando varios widgets necesiten el mismo dato, el servicio deberá reutilizarlo en lugar de volver a consultarlo al teléfono.

## Siguiente fase

Centralizar el estado por dispositivo para RED, Rendimiento, Estadísticas, REF, Captura, Archivos y Aplicaciones; después revisar Gnirehtet y scrcpy para evitar instancias duplicadas y polling innecesario.
