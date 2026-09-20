# NOVORA — USB automático V1

Este cambio enlaza los eventos existentes de dispositivo/ADB con la preparación USB y conecta Android al recibir la invitación. El canal de control sigue usando 27214 y TLS con pinning. No elimina la autenticación, no crea un servidor ADB adicional, no agrega sondeo periódico al programa y no inicia automáticamente VPN, video, grabaciones o transferencias.

El código activo ya había eliminado la entrada de ocho dígitos. Faltaban el disparador automático en PC y la llamada a conectar después de recibir la invitación en Android. El antiguo `NLControlServer` se conserva como código heredado; NO se usa para la nueva conexión automática ni se relaja su validación.

## Pruebas

`NLUSBTestPolicy.ps1` compila y ejecuta la clase real `NLControlUsbAutoPolicy` mediante Add-Type. Prueba 14 condiciones de autorización, duplicados, bloqueo por otra operación, desconexión, cambio de dispositivo y reconexión. No necesita Pester.

`NLUSBTestAutomaticSource.ps1` comprueba los contratos de integración. Un resultado correcto demuestra presencia de la integración, no comportamiento físico.

`NLUSBTestAutomatic.ps1` verifica el build registrado, hashes de las fuentes modificadas/protegidas, el APK instalado, el proceso Windows, ADB, reverse, TCP y el estado confirmado que emite el servicio Android. No crea el reverse ni abre un socket de prueba sobre el puerto de emparejamiento. Un estado LISTEN por sí solo no prueba conexión; ESTABLISHED sin autenticación tampoco.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "tests\USB\NLUSBTestAutomatic.ps1" -LaunchPc -InteractiveLifecycle -InteractiveEngines
```

Para la primera conexión y la reconexión no pulses Preparar ni Conectar. `-InteractiveLifecycle` requiere desconectar/reconectar físicamente el cable. `-InteractiveEngines` permite comprobar los estados de arranque después de que tú inicies LE/VE y aceptes el permiso VPN. Iniciar un motor NO prueba toda la transferencia de Internet, la calidad de imagen/audio ni todos los controles.

## Resultados

`results/build-latest.json`: evidencia de compilación y hashes. `results/USB-*.json` y `.txt`: resultados de cada ejecución física. Los resultados se generan; este paquete no incluye un PASS físico pregrabado.

PASS = comprobación ejecutada y satisfecha. FAIL = comprobación fallida. NOT_TESTED = no ejecutada. PARTIAL = hay pruebas pendientes. VERIFIED_USB_LIFECYCLE exige conexión automática, identidad del APK y desconexión/reconexión reales; no certifica todas las funciones de los motores.

La espera acotada con muestreo existe sólo en el test externo, no en NOVORA. La telemetría de Android usa eventos de sesión y no guarda invitaciones, claves, números de serie, nombres de PC, portapapeles ni archivos.

## Límites que se conservan

Solo se controla automáticamente el teléfono USB seleccionado por el flujo existente de descubrimiento. Una sesión LAN activa no se reemplaza automáticamente. Detener en PC pausa ese intento hasta reconectar el cable o pulsar el botón de reintento. El arranque de Internet se hace desde Android para conservar el consentimiento VPN; el botón de PC que ya era informativo no se convierte en un bypass. Las capacidades reales, permisos y requisitos de cada función siguen vigentes.

La entrada nueva `.UsbBootstrapActivity` requiere `android.permission.DUMP`: Android shell/system puede invocarla, las apps ordinarias no. El launcher no consume invitaciones desde extras externos; la invitación se entrega una sola vez dentro del proceso. No se solicita ni concede ese permiso a NOVORA.

## Fuentes técnicas

Base: NOVORA-USB-ACTIVE-CODE.txt generado el 16-09-2026 01:15:56.
Android: developer.android.com/develop/ui/views/notifications/notification-permission (POST_NOTIFICATIONS no es requisito para iniciar foreground service; la notificación sigue siendo obligatoria).
Android: developer.android.com/guide/topics/manifest/activity-element (permiso requerido para iniciar una Activity).
AOSP: platform/frameworks/base/packages/Shell/AndroidManifest.xml (permiso DUMP del shell).

No se han modificado LAN/TLS, RelayCore, el data plane de LE ni los mecanismos internos de VE. Este paquete debe compilarse y probarse en Windows con el teléfono real; el análisis del TXT no sustituye esa ejecución.