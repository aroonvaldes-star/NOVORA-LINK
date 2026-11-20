# Instalador de NOVORA

La estructura queda preparada para generar un instalador sin volver a mover herramientas.

1. En Visual Studio, publica `NOVORA.Launcher` con el perfil `win-x64`.
2. Verifica que `Tools/NOVORA V1.1` aparezca dentro de `publish/Tools/NOVORA V1.1`.
3. Abre `NOVORA.Installer.iss` con Inno Setup 6.
4. Compila el script.
5. El instalador resultante aparecerá en `Installer/output`.

El instalador no debe descargar ADB, scrcpy ni Gnirehtet: los binarios incluidos en `Tools/NOVORA V1.1` forman parte del paquete de NOVORA.
