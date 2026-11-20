# NOVORA 1.1 — resumen recuperado

- Launcher WPF para Android usando Scrcpy y herramientas locales.
- Widgets dinámicos.
- Consola limitada a NOVORA/ADB/Scrcpy/Gnirehtet.
- Bitrate, FPS, resolución máxima y audio configurables.
- Estadísticas opcionales sin sondeo constante de ADB.
- Gnirehtet manual.
- Configuración guardada en `%LOCALAPPDATA%\NOVORA`.
- Actualizaciones por HTTPS con validación SHA-256.
- Flujo Wi-Fi ADB documentado: USB → IP → `adb tcpip 5555` → `adb connect`.

También se documentó una fase de limpieza de servicios:
`Widget -> Servicio NOVORA -> herramienta -> Android`.
