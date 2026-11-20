# NOVORA 1.1

Launcher WPF para transmitir Android con Scrcpy y herramientas locales de NOVORA.

## NOVORA 1.1
- Launcher original conservado como base.
- Widgets dinámicos en el centro; la ventana crece/reduce según los widgets activos.
- Botón **WIDGETS** junto a **GNIREHTET** y **ACTUALIZAR**.
- Consola integrada para logs y comandos limitados a NOVORA, ADB, Scrcpy y Gnirehtet.
- Bitrate, FPS, resolución máxima y audio configurables.
- Ayuda `?` sencilla: qué es y en qué afecta.
- Estadísticas opcionales y sin sondeo constante de ADB.
- Gnirehtet sigue siendo manual.
- Configuración guardada localmente en `%LOCALAPPDATA%\NOVORA`.

## Compilación
Abrir `NOVORA.sln` en Visual Studio y compilar con .NET 8 / Windows.

## Privacidad
NOVORA no necesita cuentas ni nube para esta configuración. Los ajustes se guardan localmente.

## Integridad de actualizaciones

NOVORA 1.1 acepta actualizaciones únicamente por HTTPS y verifica la huella SHA-256 publicada para el instalador antes de ejecutarlo. Si la huella no coincide, la actualización se rechaza.
