# Inicio de VisionEngine desde AppControl y PC — 2026-10-12

Se corrigieron fallos reproducibles de arranque en el código, se instaló el paquete local 1.4.101 en PC y Android y se aplicó la excepción privada de firewall autorizada por el usuario. El teléfono ya encuentra la PC mediante descubrimiento LAN. No se ha validado una sesión física completa de VisionEngine con la versión nueva.

## Evidencia del diagnóstico

- Checkout comprobado: `E:\NOVORA\NOVORA LINK\GITHUB\NL`, rama `NOVORA-LINK`, HEAD `5afbabc`. Se conservaron los cambios preexistentes y se comparó esta tarea contra sus snapshots, no contra el diff completo de HEAD.
- AppControl instalado: `com.novora.appcontrol`, 1.4.99 / code 99. El descriptor de PC instalado coincidía en versión Android y protocolo de control 1.
- ADB detectó el mismo Samsung SM-A566E por dos rutas inalámbricas. En esta captura no se observó una conexión USB física.
- El registro de PC `%TEMP%\NOVORA_VE_error.log` mostraba un corte TLS de video, error de socket 10054. Android mostraba errores `Broken pipe` de audio USB y pérdidas/reintentos de sesión. Esos registros prueban cortes, pero no determinan por sí solos quién inició cada cierre ni prueban un crash del proceso Android. `dumpsys activity exit-info` y el buffer de crashes no aportaron un crash reciente de AppControl.
- El arranque de la instalación PC respondió y abrió los listeners de control y descubrimiento, pero la búsqueda Android no encontró la PC. Se comprobaron dos reglas locales explícitas `Block` TCP/UDP para `C:\Program Files\NOVORA\NOVORA.exe`, habilitadas en Private y Public.
- La herramienta Windows Computer Use no pudo conectar su canal nativo (`os error 2`). No se sustituyó por automatización UI mediante PowerShell.

## Correcciones

1. **Permiso Android y servicio.** `NLAndroidUIActivity` conserva la autorización de captura mientras la actividad vuelve a enlazar su servicio. `NLAndroidVideoProjectionRequest<T>` liga la autorización a la instancia del servicio, generación y ruta. Espera estado confirmado y disponibilidad; consume la autorización una sola vez. La decisión de audio usa el mismo mecanismo antes de abrir el permiso de pantalla. Los callbacks de conexión y estado reanudan el trabajo sin polling.
2. **Revisiones del estado.** Un cambio de telemetría o disponibilidad durante el permiso de Android ya no invalida `startAppVideo`/`startVideoLan`. `NLControlCommands` vuelve a validar las opciones y disponibilidad actuales. Las ediciones de ajustes, los comandos restantes, el protocolo y la autorización de transporte/sesión conservan sus comprobaciones.
3. **Inicio desde PC.** El botón principal admite LAN autorizada sin ADB, mantiene disponibles cancelar/detener y se actualiza al cambiar la autorización LAN. Una sesión AppControl autorizada del mismo serial puede iniciar por ADB inalámbrico; no exige un cable en esa ruta.
4. **Cancelación desde PC.** La solicitud PC tiene un identificador opcional nuevo en `NLControlEngines`. Un permiso originado por PC se invalida al cancelar, vencer o reemplazar esa solicitud. Android observa el identificador después de instalar la solicitud, lo reinicia al cambiar servicio/sesión y puede abrir el permiso de una solicitud reemplazante después de terminar el resultado anterior. Las solicitudes manuales Android permanecen independientes. Al aceptar `startVideoLan`, PC cancela también el temporizador de autorización.
5. **Firewall preparado, sin aplicar.** `scripts/Repair-NOVORA-PrivateLanFirewall.ps1` muestra una vista previa por defecto. `-Apply` conserva el bloqueo del programa en Public/Domain y propone TCP/UDP exclusivamente para ese programa, perfil Private y `LocalSubnet`. Guarda los bloques que modificará, incluidos los encontrados en aplicaciones posteriores. Rechaza nombres de reglas ajenos o reglas propias cuyo alcance cambió. `-Undo` restaura los bloques y elimina las reglas creadas; no requiere que el ejecutable siga presente.

## Verificación fresca

Dos regresiones fallaron antes de las correcciones: el botón PC no permitía LAN sin ADB, y `NLControlCommands` rechazaba un inicio válido después de cambiar la revisión. Una tercera regresión reprodujo el permiso PC todavía utilizable tras cancelar la solicitud; pasó con la identidad de solicitud. La prueba de firewall reprodujo un bloque nuevo perdido al hacer Apply dos veces y Undo; pasó después de ampliar la copia antes de modificar reglas.

Comando integrado final:

```powershell
.\scripts\Verify-NOVORA-Repository.ps1 -NoRestore -OutputRoot '.\artifacts\ve-startup-20261012\verification-final'
```

Resultado observado: código 0, Windows Release y Android target `Compile` con 0 errores y 0 advertencias; suites LinkEngine, ExIn HID, DynamicAddress y VisionEngine Startup pasadas; Cargo check/build y 3 pruebas Rust pasadas. La prueba PowerShell del firewall pasó con colaboradores en memoria, sin cambiar el firewall real. El log completo está en `artifacts/ve-startup-20261012/verification-final.log`.

Las pruebas Startup compilan por enlace el código real de botones y el coordinador de consentimiento con los contratos reales. Sus colaboradores de UI no abren WPF, y la autorización de prueba no es un `Intent` Android real. Validan las decisiones de estado; no sustituyen una ejecución de los callbacks del sistema Android.

La revisión independiente encontró las omisiones de cancelación/reemplazo y rollback descritas arriba. Después de corregirlas, no encontró defectos materiales pendientes en este alcance y ejecutó nuevamente la suite de consentimiento. Las comparaciones de snapshots no presentan errores de espacio, considerando CRLF de Windows.

## Paquete local

- AppControl: **1.4.101 / code 101**, ARM64, firma v2/v3 verificada y certificado SHA-256 igual al instalado: `9e597321725669de5e5543f568635093d929e619d5db1501c8bf7b1db91feb3d`.
- APK SHA-256: `5536A0188611D02422F6E4EF083F71CC7AB9181B9A5A7F4459D06A36F5325057`.
- Copias verificadas: APK y descriptor canónicos, Windows Release, `package/WindowsBuild` y `package/PC`. Los descriptores indican 101 / 1.4.101 y el mismo SHA-256.
- Windows self-contained: `artifacts/ve-startup-20261012/package/PC`.
- Instalador: `Installer/output/NOVORA-LINK-1.4.101-VE-Setup.exe`, 117 015 700 bytes.
- Instalador SHA-256: `4236c8926a96bfb5fa1865d239b35292c288b50f2b0902925b3832297be532c5`; sidecar verificado.
- Relay canónico/publicado: SHA-256 `DA979BFB6AF88CCF8CD3FF9BF86051DF00EA5E4C88E74F11F3BE8FC607E2E8C0`. No se cambió su implementación en esta tarea.
- Se prepararon los avisos y fuentes legales requeridos por el script del instalador.

La candidata local 1.4.100 usó la clave de depuración predeterminada de otro contexto de ejecución y su firma no coincidió. Se rechazó antes de copiarla al paquete canónico o instalarla. Se localizó la clave compatible original y, tras completar las correcciones de revisión, se generó 1.4.101 respetando N+1 por APK. No se copiaron claves privadas a la entrega ni se desinstaló AppControl.

## Aplicación autorizada y comprobación física

- El usuario autorizó la actualización y la reparación de firewall y confirmó haber activado la depuración por Wi-Fi. También pidió dejar su comprobación como instrucción para las IAs; quedó registrada al inicio de `E:\NOVORA\Bitacora.md`.
- Se verificó `adb_wifi_enabled = 1` y una conexión ADB inalámbrica con estado `device`. Además apareció una ruta USB física al mismo Samsung SM-A566E. Estos datos se revalidan en cada sesión; no equivalen a una sesión de NOVORA autorizada.
- Windows: el instalador terminó con código 0, sin requerir reinicio; el registro y el descriptor instalado indican 1.4.101. El SHA-256 de `C:\Program Files\NOVORA\NOVORA.dll` coincide con el paquete PC publicado localmente (`4C65DB36774A358D04B889CC02A5148E4A8CDA2E7E9BC5A96CA73B1CA20A9500`).
- Android: `adb install -r` terminó con `Success`; `dumpsys package` confirma code 101 / 1.4.101 y la UI muestra esa versión. La firma se comprobó antes de instalar y coincide con la anterior. No se desinstaló ni se borraron datos de AppControl.
- Firewall: se aplicó `Repair-NOVORA-PrivateLanFirewall.ps1 -Apply`; las dos reglas Block originales quedaron habilitadas únicamente en Public. Las nuevas reglas TCP/UDP se verificaron como Inbound/Allow, Private, programa exacto instalado y RemoteAddress `LocalSubnet`. La red Wi-Fi de PC es Private. Se conservó la copia para Undo.
- Descubrimiento físico: la búsqueda desde AppControl mostró la PC en la red y abrió su página de código LAN. Esto confirma anuncios y descubrimiento, pero no confirma emparejamiento ni video.
- La prueba directa de `NLServiceADB` desde el DLL instalado detectó tres transportes del mismo teléfono en 4902 ms, incluido USB físico y ADB Wi-Fi. Este sondeo del componente no demuestra el estado de selección/autorización de la ventana WPF.
- Durante estas comprobaciones la PC estaba respondiendo, con su listener LAN abierto. AppControl continuaba abierto en 1.4.101, pero su UI todavía mostraba USB OFF / 0 motores. No se observaron mappings reverse ni una sesión de control autorizada en esta comprobación; no se inició una captura de prueba completa.
- La herramienta para controlar ventanas Windows volvió a fallar con `Computer Use native pipe is unavailable` / `os error 2`. Se solicitó al usuario pulsar Conectar por USB e Iniciar VisionEngine en PC y comunicar el permiso o error observado. No se sustituyó la herramienta por UI Automation de PowerShell.
- Evidencia adicional: `install-pc-1.4.101.log`, `install-android-1.4.101.log`, `firewall-before.clixml`, `firewall-applied.json`, `pc-adb-service-live.json`, `ui-101-*.xml` y `package-summary.json` en `artifacts/ve-startup-20261012`.

## Estado pendiente

La actualización compatible y la reparación privada de firewall están aplicadas; el descubrimiento LAN quedó observado en el teléfono. Falta completar la autorización de control y verificar el primer inicio de VE desde AppControl y desde PC, con video/audio/control por USB físico y LAN nativo. Sigue pendiente la comprobación manual solicitada porque no hay acceso automatizado a la ventana PC. No se certifican reconexión, rendimiento o compatibilidad general por estos resultados. No se publicó nada en GitHub ni se realizó commit/push.
