# NOVORA 1.2 — estado de reconstrucción

Esta entrega reorganiza lo que está respaldado por los materiales disponibles:

- WPF/C# como base de Visual Studio.
- Estructura DEBUG / RELEASE / VS / VSCODE.
- Modo de prueba final.
- Normalización segura de bitrate, incluyendo migración de MB/s a Mbps.
- Recovery separado de la UI y sin polling agresivo por defecto.
- Base de MainWindow y modelos para OutputProfile.

## Importante
No se dispone del ZIP fuente completo de NOVORA 1.2 en los archivos accesibles en esta sesión. Por ello, esta entrega es una **reconstrucción integrada de la base respaldada por los archivos disponibles**, no una copia byte a byte del proyecto original.

## Pruebas previstas
- Conexión ADB.
- Gnirehtet/recovery.
- Wi-Fi ADB.
- Estabilidad 10/30/60 minutos.
- Validación de latencia y pérdida.
