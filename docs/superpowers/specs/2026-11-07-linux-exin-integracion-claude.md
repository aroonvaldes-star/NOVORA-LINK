# NOVORA-LINUX: integración ExInEngine (parte de Claude)

Estado: implementado y verificado sin hardware, 2026-11-07; falta validación física. Complementa
`2026-11-07-linux-paridad-windows-usb-lan-design.md` (Codex), que dejó ExInEngine/UHID
fuera de su alcance. Claude se encarga de ExInEngine; Codex conserva shell, tema,
Inicio y conectividad USB/LAN.

## Enfoque

Paridad por código, no por reescritura: el motor Windows se compila **sin cambios** en
Linux (enlace de fuente desde `src/NOVORA LINK` en `NOVORA.Linux.csproj`):

- `ExInEngine/*.cs` salvo `ExInSdl.cs` (manager, core, calibración, perfiles, paddles,
  puntero, diagnóstico, batería, informes HID).
- `Integration/ExInHidProtocol.cs`, `ExInHidOutput.cs`, `ExInControlSession.cs`,
  `ExInConnectionSupervisor.cs` (Android `hid -` por `adb shell`, reconexión, identidad).
- `Service/NLServiceTimerResolution.cs`, `Contracts/Input/NLInputOutput.cs`.

Solo la frontera nativa es Linux:

- `ExInEngine/LinuxExInSdl.cs`: misma API que `ExInSdl` Windows sobre `libSDL3.so.0`.
  No usa `SDL_WaitEventTimeout`: en Linux el renderer de VisionEngine vacía la cola SDL
  desde el hilo de UI y X11/Wayland no permiten bombear vídeo desde otro hilo. Un hilo
  ExIn llama a `SDL_UpdateGamepads` (sin vídeo), un `SDL_AddEventWatch` copia los eventos
  de mando y `SDL_FlushEvents(0x600..0x6FF)` los retira para que el renderer no los vea.
  `SDL_AUTO_UPDATE_JOYSTICKS=0` evita que el `SDL_PumpEvents` del renderer toque mandos.
- `ExInEngine/LinuxExInPlatform.cs`: `NLServiceNovoraPaths` (perfiles en
  `$XDG_DATA_HOME/NOVORA-LINK/ExInProfiles`) y el subconjunto de `NLServiceADB` que usa la
  sesión (adb del sistema).

## Propiedad de archivos

Claude (nuevos): `ExInEngine/**`, `MainWindow.ExIn.cs`, `tests/ExInChecks.cs`, este
documento.

Ediciones mínimas de Claude en archivos compartidos (por favor, consérvalas al reestilizar):

- `NOVORA.Linux.csproj`: bloque ExIn (enlaces + `Microsoft.Extensions.Logging.Abstractions`).
- `MainWindow.cs`: solo se eliminó el bloque estático `BuildExInPage…BuildExInDiagnostics`
  (ahora en `MainWindow.ExIn.cs`). El motor se inicia al construir la página y se cierra
  con `Closed`; no hay más ganchos. Inicio usa `ExInStatusText` (equivale a
  `GamepadStatusText14`).
- `Control/ControlProtocol.cs`, `Control/LinuxControlServer.cs`, `Control/LinuxControlHost.cs`:
  campo `ExIn` del snapshot (mismo JSON que `NLControlExIn` Windows), `ExInState/Message/
  CanStart` en `ControlEngines`, y acciones `exin.start`, `exin.mode`, `exin.synchronize`
  a través de `ILinuxExInControl`; `AuthorizedPeerAddress`/`LanPeerAddress` para el
  respaldo ADB Wi-Fi por LAN (como `TryConnectLanEndpointAsync` en Windows).
- `tests/Program.cs`: `--exin-native` y `--exin-ui`.

La página ExIn usa los helpers de `MainWindow.cs` (`Card`, `SectionTitle`, `Caption`,
`FlatButton`, `StyleSelector`, `Brush`), así que hereda el restyling de Codex.

## Límites

- Requiere SDL3 y `adb` del sistema; acceso a `/dev/input/event*` (uaccess/udev).
- Android necesita el comando `hid` (como en Windows). No se instala nada en el teléfono.
- «CONTROLES DE WINDOWS» pasa a «CONTROLES DE LINUX» y abre la herramienta del sistema si existe
  (`jstest-gtk`); si no, informa de que no hay una disponible.

## Evidencia (2026-11-07)

- Build Release de app y pruebas: 0 errores, 0 advertencias.
- `--exin-native`: 34/34, estable en 3 ejecuciones. Mando virtual SDL3 → `ExInSdl` Linux
  con un bucle tipo renderer (`SDL_PollEvent`, vídeo dummy) drenando la cola en paralelo:
  ExIn recibe alta/ejes/botones/baja. Motor Windows completo → registro HID e informes.
  `ExInControlSession` Windows con `adb` simulado: `getprop ro.serialno`, `shell hid -`,
  `register` + `report`, parada limpia. Contrato JSON idéntico a `NLControlExIn*`.
- `--exin-ui`: página completa en Light/Dark, estado sin mando y validaciones `exin.*`
  con los mensajes de Windows. Capturas en `/tmp/novora-exin-ui`.
- Suites Windows ejecutadas en Linux: ExInRuntime 23/23, ExInSession 7/7; ExInOutput 2/3
  (el fallo restante es del fixture, que lanza `powershell.exe`).
- Suite Linux predeterminada, `--windows-ui` y 29 pruebas Rust: correctas.

Pendiente: validación física con teléfono (USB y ADB Wi-Fi) y mandos reales Xbox/DS4,
incluida la batería y el touchpad, que el mando virtual no cubre.

# LinkEngine (parte de Claude, 2026-11-07)

Mismo criterio: lo portable de Windows se compila sin cambios (`LESpeedTestServer`,
`LESessionMeasurements`, `LERecoveryPolicy`); Linux aporta la frontera de proceso y la UI.

- `Control/LinuxLinkRelay.cs`: lee `LE_METRIC` (mismo parser que `LENetworkRelay`) como
  instantánea de tráfico; una salida inesperada reinicia el relay con el mismo token
  (backoff 1→8 s, 8 fallos seguidos, como `LERecoveryManager`). Android reconecta DATA al
  mismo puerto. Durante la recuperación el estado es `Degraded` (LINK Control lo muestra
  como «Degradado»); tras agotar intentos, `Error`.
- `ILinuxLinkEngine`: `Transport`, `Serial`, `SessionKey`, `ClientId`, `HeartbeatCount`,
  `RelayRestarts`, `TrafficSnapshot` (con valores por defecto para fakes de prueba).
- `Control/LinuxLinkMeasurements.cs`: una sesión, sin historial; SpeedTest en
  `10.0.2.2:28085` (el relay lo mapea al loopback); tráfico manual; exportación JSON.
- `link.speedtest` en `LinuxControlServer` con la validación y mensajes de Windows.
- `MainWindow.LinkSession.cs`: estados de `LESessionPresentation`, detalles técnicos reales,
  MEDIR INTERNET / MEDIR ENLACE (USB: `am start` en el teléfono; LAN: desde AppControl),
  ACTUALIZAR TRÁFICO, EXPORTAR RESUMEN y CONFIGURAR ADB INALÁMBRICO (`tcpip` + `connect`
  con USB; si no, IP:puerto de Depuración inalámbrica con descubrimiento mDNS).

Ediciones en archivos de Codex: `MainWindow.Link.cs` (los textos fijos pasan a mis campos y
se llama a `InitializeLinkSessionVE()`), `MainWindow.cs` (`UpdateLinkState` usa
`LinkPresentationVE`), `LinuxControlHost.cs` (`LinkMeasurements`, `CurrentLinkEngine`),
`NOVORA.Linux.csproj` (enlaces LinkEngine), `tests/Program.cs` (`--link-native`).

No incluido todavía: la red experimental (`network.policy` de LE/VE/archivos con límites
adaptativos), porque cruza VisionEngine y FILES; su sección sigue desactivada en Linux.

Evidencia: `--link-native` 43/43 en 3 ejecuciones (parser, recuperación simulada y del relay
real con sesión DATA autenticada, SpeedTest HTTP extremo a extremo, invalidación de sesión,
`link.speedtest` por TLS). Suite Linux, `--exin-*`, `--windows-ui` y 29 pruebas Rust: correctas.
Pendiente: prueba física con teléfono (VPN, navegador del teléfono y ADB inalámbrico).

## VisionEngine (Claude, 2026-11-08)

Video, audio, entrada, grabación, FILES V2 y perfiles ya existían en Linux. Se añadió lo
que Windows tiene y Linux no (ExchangeVE y acciones multimedia de LINK Control):

- Portapapeles por el canal de control AppControl, mismo contrato que Windows/Android:
  Ctrl+C en la ventana de video envía tipo 8 (`CopyKey=1`) y la respuesta tipo 1 pasa al
  portapapeles de Linux; Ctrl+V envía archivos copiados por FILES V2 o el texto como tipo 9
  (`Paste=true`) y espera el ack tipo 2 de esa secuencia. Ctrl+C/V nunca llegan a Android
  como teclas. El contenido no se muestra ni se registra.
- Archivos soltados en la ventana SDL (`SDL_EVENT_DROP_*`) o en la tarjeta «Archivos / Share»
  de Integración se envían por FILES V2 en cola, cancelable con «Cancelar envío».
- RECIBIR CARPETA NOVORA: por USB, `adb pull /sdcard/NOVORA` y la importación por tipo de
  `NLControlFileStorage` de Windows (enlazado sin cambios); por LAN, el mensaje de Windows.
- Resumen «Archivos de esta sesión» (formato y últimos 20 nombres de Windows); cada sesión VE
  empieza un resumen nuevo.
- `capture` (PNG del último frame en la carpeta de grabaciones) y `audio` (`Pc`/`Disabled`)
  desde LINK Control; `capture`/`startRecording`/`stopRecording` con la validación y los
  mensajes de `NLControlCommands`. El snapshot `Media` refleja si se puede capturar o grabar.
- «Presentación dinámica» (VSync) cambia en vivo; «Mediciones en vivo» muestra FPS y Mbps.

Archivos nuevos: `VisionEngine/LinuxVisionExchange.cs`, `MainWindow.VisionExchange.cs`,
`tests/VisionExchangeChecks.cs`. Modificados: `LinuxSdlVideoRenderer.cs`,
`LinuxVisionRuntime.cs`, `LinuxVisionEngine.cs` (telemetría `VideoBytesReceived`),
`Files/LinuxVisionFiles.cs` (eventos enviado/recibido), `LinuxControlServer.cs`.
Ediciones en archivos de Codex: `MainWindow.cs` (llamada a `InitializeVisionExchangeVE()`,
texto de la tarjeta Clipboard y cableado de «Archivos / Share»), `MainWindow.Vision.cs`
(texto de Presentación dinámica y de Mediciones en vivo), `NOVORA.Linux.csproj`,
`tests/Program.cs` (`--ve-exchange-native`, `--ve-exchange-ui`).

No incluido: la recuperación «esperando a Android» de Windows, que deja la ventana abierta
cuando el teléfono deja de transmitir; en Linux, Android inicia una sesión nueva. Tampoco
instalación de APK (Windows tampoco la tiene conectada), cámara, micrófono ni red experimental.

Evidencia: `--ve-exchange-native` 44/44 con video H.264 real por FFmpeg/SDL: captura PNG
verificada píxel a píxel, portapapeles en ambos sentidos con ack por secuencia, Ctrl+C/V y
archivos soltados inyectados en la ventana SDL real, VSync en vivo, carpeta NOVORA con adb
simulado, y `audio`/`capture` por TLS. `--ve-exchange-ui` 10/10. Regresión completa correcta.
Pendiente: prueba física con teléfono (Accesibilidad, portapapeles y FILES reales).
