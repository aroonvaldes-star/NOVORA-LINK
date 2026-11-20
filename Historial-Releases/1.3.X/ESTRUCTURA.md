# NOVORA — estructura de Visual Studio

La solución usa una sola fuente de verdad para las herramientas:

src/NOVORA/Tools/

Contiene:
- adb.exe
- AdbWinApi.dll
- AdbWinUsbApi.dll
- scrcpy.exe
- scrcpy-server
- gnirehtet.exe
- gnirehtet.apk
- DLLs auxiliares de scrcpy/ADB

NovoraPaths.cs resuelve siempre:
AppContext.BaseDirectory/Tools/

El .csproj copia `Tools/**` al directorio de salida y publicación.

No usar:
- Tools/Runtime
- Tools/scrcpy/
- Tools/gnirehtet/
- rutas relativas con ../../../Tools
- rutas hardcodeadas en los servicios

Todos los servicios deben consumir NovoraPaths.
