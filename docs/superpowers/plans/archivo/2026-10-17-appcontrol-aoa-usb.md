# Integración USB AOA de AppControl — Plan de implementación

**Estado:** plan de implementación; no se han modificado componentes de producto.
**Proyecto:** NOVORA-LINK, `GITHUB/NL`.

## Objetivo

Proporcionar una conexión USB directa entre NOVORA PC y AppControl mediante Android Open Accessory (AOA), sin que la sesión funcional requiera ADB, depuración USB, Wi-Fi o anclaje USB. La conexión debe soportar control, VisionEngine (VE), LinkEngine (LE), ExIn mediante AOA 2.0 HID y Exchange, incluida la transferencia e instalación interactiva de APK.

La compatibilidad AOA del teléfono y del puerto debe comprobarse físicamente en el SM-A566E. Si el hardware no ofrece AOA, registrar el bloqueo; no sustituirlo por otra ruta.

## Arquitectura prevista

- NOVORA PC actúa como host AOA y abre el dispositivo enumerado por Windows mediante WinUSB.
- AppControl declara el soporte de accesorio, obtiene el permiso del sistema Android y abre el descriptor AOA durante el ciclo de vida de la conexión.
- Un multiplexor sobre los endpoints bulk AOA asigna canales lógicos independientes a control, VE, audio, LE y Exchange. Debe manejar encuadre, backpressure, cancelación, desconexión y cierre de canales sin perder los límites de cada protocolo.
- La capa de control conservará TLS, autenticación y reglas de confianza de NOVORA. La creación del flujo USB se separará del protocolo de control para compartir la lógica de sesión sin convertir AOA en LAN.
- ExIn usará las operaciones HID de AOA 2.0 para registrar dispositivos y enviar informes; el registro HID no se tratará como un canal bulk ordinario.
- Exchange transferirá archivos por el canal AOA. Instalar el APK será una operación distinta: AppControl usará PackageInstaller y requerirá la confirmación visible del usuario en Android.
- NOVORA Setup incluirá el paquete WinUSB firmado y lo instalará con privilegios administrativos. La sesión AOA no invocará ADB, `adb reverse` ni comandos shell.

## Mapa de archivos

| Grupo | Archivos existentes que se prevé modificar | Responsabilidad |
|---|---|---|
| Host USB y UI Windows | `src/NOVORA/UI/NLUIWindowMainAutomaticUsb.cs`; `src/NOVORA/UI/NLUIWindowMainAndroidControl.cs`; `src/NOVORA/UI/NLUIWindowMainAndroidVideoSource.cs` | Reemplazar detección/preparación USB dependiente de ADB y administrar el ciclo de vida AOA y sus canales. |
| Control y sesión | `src/NOVORA/Control/NLControlClient.cs`; `src/NOVORA/Control/NLControlSession.cs`; posiblemente `src/NOVORA/Control/NLControlTrustServer.cs` | Permitir una sesión sobre el canal AOA, manteniendo TLS, pairing, confianza, límites y tratamiento de desconexión. Revisar si el servidor actual puede exponerse mediante un adaptador local sin debilitar su seguridad. |
| AppControl Android | `src/NOVORA.Android/Properties/NLPropertiesAndroidManifest.xml`; `src/NOVORA.Android/AndroidUI/NLAndroidUIUsbBootstrapActivity.cs`; `src/NOVORA.Android/AndroidService/NLAndroidServiceControl.cs`; `src/NOVORA.Android/NLProjectAndroid.csproj` | Declarar y recibir el accesorio, solicitar permiso, iniciar/parar el servicio y compilar los nuevos componentes. Retirar del arranque USB la invitación transmitida por ADB. |
| VisionEngine | `src/NOVORA/UI/NLUIWindowMainAndroidVideoSource.cs`; `src/NOVORA.Android/AndroidVideo/NLAndroidVideoService.cs`; `src/NOVORA.Android/AndroidVideo/NLAndroidVideoLanClient.cs` | Enrutar video, audio y control por los canales AOA conservando protocolos, captura y políticas de sesión existentes. Ajustar los archivos solo donde el flujo actual dependa directamente de sockets/reverse USB. |
| LinkEngine | `src/NOVORA/LinkEngine/Transport/LETransportManager.cs`; `src/NOVORA/LinkEngine/Transport/LETransportData.cs`; `src/NOVORA/LinkEngine/Network/LENetworkManager.cs`; `src/NOVORA.Android/AndroidVpn/NLAndroidVpnService.cs` | Sustituir configuración/verificación de ADB reverse en sesiones AOA por canales AOA para control y datos. Preservar la ruta Wi-Fi/LAN existente. |
| ExIn | `src/NOVORA/Integration/ExInHidOutput.cs`; `src/NOVORA/Integration/ExInControlSession.cs`; `src/NOVORA/Integration/ExInHidProtocol.cs` | Sustituir salida `adb shell hid` por el backend HID AOA 2.0 y conservar los informes producidos por ExInEngine. |
| Exchange | `src/NOVORA/VisionEngine/Exchange/VEExchangeUsbBackend.cs`; `src/NOVORA/VisionEngine/Exchange/VEExchangeUsbTransport.cs`; `src/NOVORA/VisionEngine/Exchange/VEExchangeSessionBinder.cs`; `src/NOVORA/Integration/NLIntegrationRuntime.cs` | Sustituir operaciones USB basadas en ADB por operaciones del canal AOA; asociar transferencia e instalación interactiva a la sesión y sus permisos actuales. |
| Controlador e instalación | `Installer/NOVORA.Installer.iss`; posiblemente `Installer/README.md` y scripts de construcción/empaquetado | Incluir el paquete WinUSB firmado, instalarlo desde Setup elevado y documentar sus recursos/versionado y retirada durante desinstalación si corresponde. |
| Versionado APK | `src/NOVORA.Android/NLProjectAndroid.csproj` y los descriptores/artefactos de APK que use el flujo de publicación | Si se genera una APK, aumentar versión y versionCode a **1.4.106 / 106**, mantener la firma vigente y sincronizar descriptores y copias de distribución. La versión de Windows no se incrementa por esta regla. |

### Componentes nuevos previstos

- `src/NOVORA/UsbAccessory/`: host WinUSB, enumeración AOA, protocolo de frames y multiplexor/canales de transporte.
- `src/NOVORA.Android/UsbAccessory/`: receptor/administrador de accesorio Android, puente de streams y controlador HID AOA 2.0.
- Paquete de controlador WinUSB firmado y sus metadatos para Setup, en la ubicación compatible con el flujo de build existente.
- Pruebas de protocolo y ciclo de vida AOA en `tests/`, incluyendo implementaciones fake para no depender de un teléfono en cada prueba.

El mapa indica candidatos a integrar; antes de editar se confirmará cada referencia en el checkout vigente y se preservarán todos los cambios preexistentes. No se reescribirán carpetas completas de motores si la capa de transporte permite conservar sus protocolos.

## Fases

1. **Viabilidad y diseño de interfaces:** comprobar enumeración AOA, capacidades reales del SM-A566E/puerto y requisitos de firma/instalación WinUSB. Definir contrato de transporte, formato de frames, IDs de canal, límites, ownership, reconexión y compatibilidad de protocolo antes de conectar motores.
2. **AOA base:** implementar host Windows y receptor Android; permiso, apertura, handshaking, canales lógicos, errores, desconexión y liberación idempotente. Probar el enlace con harnesses fake y transferencia de carga controlada.
3. **Control y VE:** adaptar la apertura de sesión de control al canal AOA; conservar autenticación y TLS. Migrar video, audio y control y verificar que el enlace USB no dependa de `adb reverse`.
4. **LE y ExIn:** mover control y datos de LE a canales AOA; implementar AOA2 HID para registro e informes ExIn. Mantener aislada la ruta LAN.
5. **Exchange y APK:** transferir en ambas direcciones, comprobar integridad y limpieza de parciales; iniciar PackageInstaller y reportar por separado “transferido”, “confirmación pendiente”, “instalado” o “rechazado”.
6. **Setup, Release y evidencia:** empaquetar el controlador firmado en NOVORA Setup; compilar PC y Android en Release; aplicar N+1 al APK si se genera; validar primero por harness y luego físicamente en SM-A566E.

## Pruebas y aceptación

- **Protocolo/harness:** frames fragmentados y agrupados, canales concurrentes, orden por canal, límites de tamaño, presión/colas, cancelación, timeout, cierre y rechazo de datos malformados.
- **Ciclo USB:** dispositivo compatible/no compatible, primer permiso Android, permiso denegado, attach/detach, suspensión/reanudación, cambio de sesión y reconexión sin canales huérfanos.
- **WinUSB/Setup:** instalación elevada del paquete firmado, asociación al dispositivo AOA, fallo de instalación, reparación y desinstalación; sin modificar ni eliminar otro controlador ajeno.
- **Motores:** control; VE video/audio/control; LE control y datos; ExIn HID para conexión, informes y cierre; Exchange en ambas direcciones con cancelación y validación de integridad; instalación APK con consentimiento Android.
- **Independencia:** repetir la sesión con depuración USB desactivada y ADB no disponible, confirmar que no se ejecutan `adb`, `adb reverse`, shell ADB ni tethering como parte funcional de la sesión. ADB se permite para instalar o diagnosticar builds durante desarrollo, no para acreditar el transporte final.
- **Compatibilidad:** probar con el SM-A566E y registrar modelo, versión de AppControl, estado del controlador, estados de los motores y resultados observados. Si el dispositivo no soporta AOA, declarar bloqueada la validación física sin activar un transporte sustituto.
- **Preservación LAN:** ejecutar regresión de conexión Wi-Fi/LAN y confirmar que el camino LAN no hereda el controlador ni estado exclusivos de AOA.

## Restricciones y criterios de cierre

- No implementar depuración ADB como mecanismo funcional, USB tethering, fallback LAN silencioso ni una ruta inalámbrica exigida por el modo AOA.
- Mantener separados el resultado de build, pruebas de protocolo, instalación, UI observada y sesión física real.
- No declarar AOA terminado hasta que los motores incluidos pasen las pruebas funcionales por AOA con ADB fuera de la ruta, y se documente la validación física disponible.
- Si WinUSB firmado, permiso Android, soporte AOA o alguno de los motores no se puede validar, registrar el bloqueo concreto y no presentar la integración como completa.
