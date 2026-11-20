# Revisión V1.1

Fecha de revisión: 2026-08-14.

## Resultado

Se conservaron los 19 archivos que venían en `NOVORA V1.1.zip`.

Los binarios de ADB, scrcpy y Gnirehtet se conservaron dentro de `Tools/NOVORA V1.1`.

Los scripts de texto que contenían problemas de rutas o sintaxis fueron corregidos manteniendo el mismo nombre. Las versiones originales se guardaron en `docs/original-scripts/` para no perderlas.

## Problemas detectados

- `TEST2.bat` contenía marcadores Markdown ````` que no son válidos en CMD.
- Varios scripts dependían del directorio de trabajo actual en lugar de `%~dp0`.
- `scrcpy-noconsole.vbs` podía ejecutar `scrcpy.exe` desde un directorio incorrecto.
- `CERRAR EMERGENCIA.bat` dependía de `adb` en PATH.
- `gnirehtet-run.cmd` dependía de `gnirehtet.exe` en PATH.

## Cambios de arquitectura

- Las rutas del Launcher están centralizadas en `NovoraPaths.cs`.
- ADB, scrcpy y Gnirehtet se invocan desde servicios separados.
- El proyecto copia `Tools/NOVORA V1.1` al output y al publish.
- Se agregó un perfil de publicación `win-x64`.
- Se agregó una plantilla de instalador Inno Setup.

## Limitación de esta revisión

El entorno de análisis no tiene instalado .NET/MSBuild, por lo que no es posible afirmar que se realizó una compilación real aquí. Se realizó revisión estática de archivos, referencias, rutas y estructura. La primera comprobación en Windows debe ser `Build > Rebuild Solution` en Visual Studio.
