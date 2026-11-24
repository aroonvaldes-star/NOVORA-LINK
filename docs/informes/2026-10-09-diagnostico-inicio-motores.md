# NOVORA-LINK: diagnóstico de inicio de motores e instalador Android

Fecha: 9 de octubre de 2026, hora de Ciudad de México. Checkout examinado: `E:\NOVORA\NOVORA LINK`.

## Seguimiento: instalación física y paquete experimental

Con autorización expresa del propietario para borrar los datos internos de AppControl, se desinstaló la 1.4.65 firmada con la clave anterior y se instaló la APK 1.4.66. `adb install` respondió `Success`; `dumpsys package` confirmó `versionCode=66` y `versionName=1.4.66`. La aplicación abrió, mostró `ANDROID · AppControl · v1.4.66` y mantuvo un proceso activo. ADB Wi-Fi siguió conectado por `10.0.0.28:5555`; USB dejó de aparecer después de la reinstalación. Esto resuelve el bloqueo de instalación en este teléfono mediante reinstalación, no mediante recuperación de la clave anterior.

Se compiló una publicación Windows Release que contiene la pareja APK/descriptor 1.4.66 y un instalador local Inno Setup 7: `artifacts/experimental/NOVORA-LINK-1.4.66-EXPERIMENTAL-Setup.exe` (56,036,692 bytes; SHA-256 `5922C82581B7A2831CA71432BB92E809E7D2884F63D516B024FD71FF72F1FA12`). Incluye los avisos/licencias de terceros; las fuentes redistribuibles de FFmpeg y libusb acompañan el instalador. Es un artefacto experimental sin firma Authenticode, dependiente de .NET 8 Desktop Runtime x64. Su `LEEME-NOVORA-LINK-1.4.66-EXPERIMENTAL.md` registra instalación y requisitos.

La entrega está publicada en la [pre-release 1.4.66-pre.1](https://github.com/aroonvaldes-star/NOVORA-LINK/releases/tag/1.4.66-pre.1). GitHub agotó el tiempo al subir el ejecutable completo; se adjuntaron 75 partes, un manifiesto SHA-256 y un script de descarga y reconstrucción. Los 83 adjuntos finales se verificaron en estado `uploaded` y con hash coincidente. La reconstrucción local de las 75 partes produjo el SHA-256 del instalador original.

NOVORA PC Release abrió y generó un listener LAN en `10.0.0.27:27215`. No se completó la vinculación: durante el intento de introducir el código la pantalla activa del teléfono pasó a otra aplicación. Por tanto, no hay aún prueba física de tráfico LinkEngine, cuadros VE o reportes ExIn con la versión 1.4.66 instalada.

## Corrección prioritaria y estado actual

1. **LinkEngine LAN, causa comprobada en código.** `NLAndroidServiceControl` detenía la VPN propia ante cualquier cambio de estado de una sesión LAN conectada, porque la condición exigía literalmente `Transport == "USB"`. `StartInternetAsync` sí acepta LAN. Se cambió la política de ciclo de vida para conservar la VPN en sesiones conectadas USB y LAN. La regresión de `NOVORA.LinkEngine.Tests` falló antes de la corrección y pasó después en Debug y Release. Esto elimina una causa concreta de arranque o desconexión inmediata de LinkEngine LAN; falta probar tráfico VPN real en el teléfono con la APK nueva.
2. **Entrega Android 1.4.66.** Se incrementaron `ApplicationVersion` a 66 y `ApplicationDisplayVersion` a 1.4.66 antes de compilar AppControl. El empaquetado Release ARM64 `SignAndroidPackage` terminó sin advertencias ni errores. `aapt dump badging` verificó `com.novora.appcontrol`, código 66, nombre 1.4.66 y SDK mínimo 26. `apksigner verify` confirmó firma v2/v3. El APK canónico y el copiado al binario PC Release coinciden con el descriptor SHA-256 `038DC9EFE0E8C5BB64A7CE3825D4769B49DDB7A4480EBB2E0AA86A0D090A6D42`. La compilación Windows Release terminó con 0 errores y 2 avisos NU1900 de consulta NuGet; la prueba de LinkEngine Release pasó.
3. **Bloqueo de actualización física: firma distinta.** Android rechazó inicialmente `adb install -r` de 1.4.66 sobre la 1.4.65 con `INSTALL_FAILED_UPDATE_INCOMPATIBLE` y conservó sus datos. El APK anterior tenía certificado SHA-256 `0b64428256b7b8d26a7ea057fd0e0141240728e9189a1bce3812ba5a58725a22`; el nuevo tiene `9e597321725669de5e5543f568635093d929e619d5db1501c8bf7b1db91feb3d`. No se encontró la clave anterior en los almacenes revisados. Después, el propietario autorizó expresamente borrar los datos internos: se desinstaló la 1.4.65 y se instaló la 1.4.66 con éxito. En otros teléfonos, preservar los datos al actualizar desde esa firma requiere la clave original. El instalador PC explica la incompatibilidad y no borra datos automáticamente.
4. **VE y ExIn.** La captura previa con 1.4.65 demostró cuadros VE reales y reactivación ExIn; sus fallos intermitentes siguen sin reproducción causal simultánea. Se priorizó la condición LAN reproducible y se dejaron las hipótesis de reconexión y diagnóstico registradas abajo para una captura física posterior. La instalación y apertura de 1.4.66 no bastan para afirmar que el arranque de los tres motores quedó resuelto físicamente.

## Alcance y resultado del diagnóstico inicial

Se inspeccionó el código y se ejecutó el binario Windows Release de este checkout con el teléfono Samsung SM-A566E conectado inicialmente por ADB Wi-Fi. Se leyeron estados de la interfaz, procesos, puertos y registros Android. Se reactivó ExInEngine desde la interfaz. El teléfono quedó bloqueado y ADB pasó a `offline` al final; por ello no se autorizó una nueva VPN ni se completó una prueba de transferencia de Internet con LinkEngine. No se compiló ni reinstaló AppControl durante este diagnóstico.

La APK firmada más reciente encontrada en el checkout era `1.4.65` (código 65), mientras que el paquete integrado en el instalador PC era `1.4.61`. Se actualizó la pareja `src/NOVORA/Android/NLAndroidApp.apk` y `NLAndroidRelease.json` a 1.4.65 y se copió al directorio del binario Release. SHA-256: `2E4EF8F0B66456EC30CFFC04CD57338DF478886E6CE4E5FC66EA43602B9C8F59`. La UI de la instancia anterior mostró `NOVORA Android 1.4.65` tras pulsar **COMPROBAR APP**, confirmando que leyó la nueva pareja. `.gitignore` ahora deja visible la APK canónica para su revisión y futura inclusión en Git.

## Evidencia observada

| Momento local | Entrada / llamada | Salida observada | Alcance |
| --- | --- | --- | --- |
| 04:11 | Sesión de video VE previa | `NOVORA_VE_error.log`: `SocketException 10054`, conexión cerrada por el extremo remoto durante lectura TLS del encabezado | Prueba de corte de transporte; no identifica por sí sola qué proceso lo originó. |
| 12:18:53–12:19:46 | Sesión Android anterior en LAN | Log `NOVORA-USB`: `LinkRunning=true` y `VpnRunning=true`; luego `NOVORA-VE-LAN` registra `EndOfStreamException` en el canal de control; estado pasa a `Lost` y repite `Connecting`/`Lost` aproximadamente cada 10 segundos | Evidencia de una pérdida y recuperación fallida previa; no fue un inicio de LinkEngine realizado en esta prueba. |
| 12:25–12:27 | Apertura de `NOVORA.exe` Release y negociación LAN | ADB mostró el teléfono `device` por `10.0.0.28:5555`; Android confirmó `Phase=Connected`, `Transport=LAN`, `VideoRunning=true`; el proceso PC escuchaba en `27215` y tenía conexión establecida con el teléfono | Confirma transporte de video LAN durante esta sesión. |
| 12:27–12:32 | VisionEngine activo | UI PC: 1,720 cuadros decodificados / 1,665 renderizados / 54 perdidos; más tarde 5,879 / 5,708 / 170, con 0 errores de decodificación | El flujo corrió; los ~2 FPS instantáneos con pantalla casi inmóvil no miden la capacidad máxima ni la latencia visual. |
| 12:32–12:34 | Botón **REACTIVAR** de ExInEngine | UI: mando `Microsoft X-Box 360 Pad`, 1 control detectado; reportes enviados subieron de 6 a 9 al terminar la reactivación | La ruta de reactivación devolvió estado activo. No hubo una prueba física de cada botón dentro de Android. |
| 12:27–12:34 | Pantalla LinkEngine de PC | `Detenida`, Internet no iniciado; indica que la activación se hace desde Android y requiere VPN. No había listener DATA `27184` en la observación. | No hubo inicio nuevo ni validación de tráfico de Internet. |
| Después del cierre de la sesión de prueba | Reinicio de NOVORA para verificar el binario nuevo | ADB Wi-Fi pasó a `offline`; un `adb connect` terminó con error 10060. El teléfono estaba bloqueado. Al cierre del diagnóstico, `adb devices -l` no listó dispositivos. | Impidió validar el flujo Wi-Fi real tras el cambio. No prueba que el cambio de código haya causado la caída de ADB. |

Los registros consultados fueron `C:\Users\Aroon\AppData\Local\Temp\NOVORA_VE_error.log`, `C:\Users\Aroon\AppData\Local\NOVORA\Logs\LinkEngine-Relay-Diagnostics.log` y `adb logcat` con etiquetas `NOVORA-USB`, `NOVORA-VE-LAN`, `NOVORA-VE` y `AndroidRuntime`. El archivo del relay a las 12:18:58 indicaba proceso detenido, sin advertencias ni errores internos del relay; no demuestra que el túnel entero estuviera sano antes de detenerse.

## Llamadas y salidas de inicio

### LinkEngine

1. En Windows, `UI/NLUIWindowMainLinkEngine.cs` presenta `LERuntimeSession`: ADB, transporte, reverse, listener, cliente y handshake. `LinkEngine/Runtime/LERuntimeManager.cs:120` inicia el runtime y en `:335` llama a `LECoreEngine.StartInternetAsync`; `LinkEngine/Core/LECoreEngine.cs:343` exige dispositivo y transporte listos antes de llamar a `LENetworkManager.StartAsync` (`:375`). El relay registra sus diagnósticos en `LinkEngine/Network/LENetworkRelay.cs:50`.
2. En Android, `AndroidUI/NLAndroidUIActivity.cs:1668` solicita `startLink`; `:1807` abre el consentimiento de VPN; `:1834` completa la autorización y llama a `AndroidService/NLAndroidServiceControl.cs:111`. Ahí `VpnService.Prepare` (`:117`) verifica permiso y se inicia el servicio VPN (`:130–134`).
3. Salida de esta prueba: la UI PC quedó detenida. Una entrada Android anterior había mostrado LinkEngine/VPN activos, seguida de pérdida de sesión LAN. No se puede atribuir un fallo concreto del primer arranque sin una captura de la transición VPN → PC → relay → cliente.

### VisionEngine

1. `UI/NLUIWindowMainVisionEngine.cs:1905` inicia el motor; para LAN usa sesión VE LAN y en Android `NLAndroidUIActivity.cs:1708` distingue `startVideoLan` de `startAppVideo` según el transporte. El flujo acaba en `VisionEngine/Video/VEVideoManager.cs:102`; los fallos se escriben desde `:449` a `%TEMP%\NOVORA_VE_error.log` (`:465`).
2. Salida de esta prueba: hubo cuadros reales decodificados/renderizados sin errores de decodificación en la sesión observada. El log anterior 10054 y el `EndOfStreamException` Android muestran que el canal puede terminar inesperadamente. `VEVideoManager.cs:475` sobrescribe el log VE previo, así que un fallo posterior puede ocultar el anterior.

### ExInEngine

1. Al cargar la ventana, `UI/NLUIWindowMain.xaml.cs:107–128` inicializa ajustes, dispositivo y sesión ExIn. `UI/NLUIWindowMainVisionEngine.cs:270` arranca la inicialización local y `:1660–1688` resuelve el serial, espera la inicialización y da 20 segundos a `ExInControlSession.StartAsync`. La sesión abre dispositivo, túnel, servidor, control y salida en `Integration/ExInControlSession.cs:119–129`.
2. Salida de esta prueba: SDL detectó un control Xbox y la reactivación incrementó reportes enviados. `Integration/ExInControlSession.cs:154–193` intenta recuperación después de fin de flujo, pero su `catch` final no deja diagnóstico; una recuperación fallida puede quedar difícil de distinguir de ausencia de mando o de transporte.

## Archivos con riesgo o incompatibilidad

| Prioridad | Archivo | Hallazgo y efecto | Estado |
| --- | --- | --- | --- |
| Alta, comprobada | `Service/NLServiceAndroidInstaller.cs:20–47,160` | Aceptaba solo seriales USB y usaba `adb -d`, aunque la UI admitía ADB Wi-Fi. La UI devolvió literalmente “El identificador USB del teléfono no es válido” para `IP:puerto`. | Corregido: valida el serial Wi-Fi, consulta con `-s <serial>` y conserva la comprobación USB para seriales USB. |
| Alta, comprobada | `Android/NLAndroidApp.apk`, `Android/NLAndroidRelease.json` | APK integrada 1.4.61 frente a AppControl 1.4.65 instalada durante el diagnóstico inicial. Un paquete antiguo podía mostrarse al usuario y bloquear una instalación/actualización coherente. | Pareja actualizada finalmente a 1.4.66, con hash y manifiesto verificados. |
| Alta, observada | `AndroidService/NLAndroidServiceVeLanCapture.cs:196–201`, `Control/NLControlInputLoop.cs` | Fin inesperado del canal de control VE LAN a las 12:18:58; siguieron transiciones `Lost`/`Connecting`. | Causa de cierre no determinada. Requiere captura simultánea PC/Android con el teléfono desbloqueado. |
| Media, observabilidad | `UI/NLUIWindowMainAutomaticUsb.cs:164–278` | Prepara varias rutas `adb reverse` bajo un plazo de 30 segundos; el error visible al fallar solo conserva el tipo de excepción. | Sin cambio: conviene registrar la etapa concreta que falla. |
| Media, observabilidad | `Integration/ExInControlSession.cs:154–193` | La recuperación de control captura excepciones sin registrar la causa. | Sin cambio: no hubo fallo reproducible de ExIn en esta sesión. |
| Media, secuencia | `UI/NLUIWindowMain.xaml.cs:107–136`, `UI/NLUIWindowMainVisionEngine.cs:270,1660–1688` | ExIn se inicializa al abrir la UI y su sesión depende luego del serial/ADB; un error temprano puede dejar la preparación parcial y el mensaje general de la ventana no conserva toda la traza en Release. | Hipótesis de fiabilidad, no causa confirmada. |
| Media, trazabilidad | `VisionEngine/Video/VEVideoManager.cs:465–483` | El archivo de error VE se sobrescribe, por lo que una segunda caída puede borrar la primera. | Sin cambio. |
| Alta, entrega | `NOVORA.Android/NLProjectAndroid.csproj:10–11`, `.gitignore`, `NLProjectDesktop.csproj:60` | `ApplicationVersion` es manual; la APK canónica estaba ignorada por Git y el PC copia su pareja solo si existe. | APK 1.4.66 compilada e integrada; próxima compilación nueva: 1.4.67/código 67. La firma incompatible exigió desinstalar la 1.4.65 con autorización y perder sus datos internos en este teléfono. |

No se confirmó incompatibilidad de protocolo entre el Windows actual y Android 1.4.65: el control LAN y VisionEngine transmitieron. El desacuerdo de versiones del instalador sí era real. El cierre 10054/EOF y los reintentos sugieren una debilidad de ciclo de vida o reconexión, pero no hay evidencia suficiente para atribuirla a un archivo concreto ni para llamarla problema de optimización.

## Cambios y verificación

- En la etapa inicial, `NLAndroidRelease.json` y `NLAndroidApp.apk` se actualizaron a 1.4.65 / código 65. La entrega experimental posterior contiene la pareja 1.4.66 / código 66 con SHA-256 `038DC9EFE0E8C5BB64A7CE3825D4769B49DDB7A4480EBB2E0AA86A0D090A6D42`, también integrada en la publicación Windows Release.
- `NLServiceAndroidInstaller.cs`: acepta un destino ADB Wi-Fi seleccionado, valida puerto 1–65535 y usa `adb -s` para confirmar su identidad; para USB conserva `adb -d`.
- `tests/NOVORA.LinkEngine.Tests/Program.cs`: nueva regresión con serial Wi-Fi. Antes del cambio falló con “El identificador USB del teléfono no es válido”; después pasó. `dotnet run --project tests/NOVORA.LinkEngine.Tests/NOVORA.LinkEngine.Tests.csproj -c Debug --no-restore`: `PASS`.
- `dotnet build src/NOVORA/NLProjectDesktop.csproj -c Release --no-restore`: 0 errores, 2 advertencias `NU1900` por acceso a los datos de vulnerabilidades de NuGet. El binario Release fue recompilado con el cambio.
- La validación UI completa de la ruta corregida quedó pendiente en esa etapa: ADB Wi-Fi pasó a `offline` y el binario Debug nuevo abrió un aviso del Firewall de Windows. La etapa posterior instaló y abrió la APK 1.4.66, pero aún no confirmó una sesión de los tres motores con ella.

## Regla de versión AppControl

En el diagnóstico inicial se omitió la compilación Android. En la corrección posterior de este mismo informe se compiló **1.4.66 / código 66** y se actualizó la pareja del instalador PC. La actualización directa se bloqueó por incompatibilidad de firma; tras la autorización para borrar datos internos, la reinstalación y apertura de 1.4.66 funcionaron. La próxima APK nueva debe incrementar a **1.4.67 / código 67**; no reutilizar `66` para una APK distinta.
