# Instalador de NOVORA

La estructura queda preparada para generar un instalador sin volver a mover herramientas.

1. En Visual Studio, publica `NOVORA` con el perfil `win-x64`.
2. Verifica que `publish/Tools` contenga `adb.exe`, `scrcpy.exe`, `scrcpy-server`, `gnirehtet.exe` y `gnirehtet.apk`.
3. Abre `NOVORA.Installer.iss` con Inno Setup 6.
4. Compila el script.
5. El instalador resultante aparecerá en `Installer/output`.

El instalador no debe descargar ADB, scrcpy ni Gnirehtet: los binarios incluidos en `publish/Tools` forman parte del paquete de NOVORA.


### ADB por Wi-Fi
NOVORA puede preparar ADB por Wi-Fi usando el teléfono conectado por USB: obtiene la IP Wi-Fi, activa `tcpip 5555`, conecta `IP:5555` y selecciona esa conexión para scrcpy. Después puedes retirar el cable USB mientras el teléfono y el PC permanezcan en la misma red.
