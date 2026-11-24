# 2026-11-24 · Streaming, aceleración simplificada y Home

## Alcance

Este informe documenta tres cambios aplicados sobre la copia local
`LINKCONTROL-Android`:

1. Aceleración reducida a uno o dos perfiles por equipo, con perfil
   personalizado del usuario.
2. Monitor de streaming en tiempo real y opciones avanzadas/OBS en Windows y
   Linux.
3. Preparación/conexión de LinkEngine movida al Home.

No se generó una APK nueva ni se compiló el escritorio: en este entorno no hay
SDK de .NET, workload Android ni SDK Android. Tampoco hubo una sesión RTMP, OBS
o GPU física disponible para validación real.

## 1. Aceleración

### Contrato compartido

`src/Shared/Control/NLAccelerationProfile.cs` centraliza:

- Proveedores normalizados: `Nvidia`, `Intel`, `Amd`, `Apple`, `Unknown`.
- Modos visibles: `Software`, `Automatic`, `Custom`. Desaparece `Forzada`.
- Backend por plataforma y proveedor:

| Plataforma | Nvidia | Intel | AMD | Apple | Desconocido |
| --- | --- | --- | --- | --- | --- |
| Windows | `NvidiaNvdec` | `qsv` | `amf` | — | software |
| Linux | `cuda` | `qsv` | `vaapi` | — | software |
| macOS | — | — | — | `videotoolbox` | software |

- `OptionsFor` devuelve como máximo dos entradas: hardware del proveedor y
  `Software`. Los perfiles `Software`, `MobileExtreme` y `MobileEconomy`, y los
  equipos sin backend implementado, devuelven una sola entrada `Software`.
- `CreateCustom` valida proveedor y backend contra la plataforma. Al guardarse,
  el selector muestra dos opciones: `Personalizado · <backend>` y `Software`.

### Por plataforma

- Linux: `LinuxVisionAcceleration` detecta proveedor, ofrece las opciones del
  contrato y degrada a software si la sonda real falla. `LinuxVisionSettings`
  migra el antiguo `Forced` a `Automatic` y conserva el backend elegido cuando
  el perfil es `Custom`.
- Windows: `VEAccelerationProfile` traduce el contrato a los tipos internos de
  VisionEngine. `VEDynamicLatencyOptimizer` ya no decide por `preferNvidia`,
  sino por el perfil y las capacidades detectadas. Se conserva el parámetro por
  compatibilidad de firma.

La UI de personalización está en la sección de diagnóstico de VisionEngine
(Linux): proveedor, backend, guardar y volver a automático. Windows expone el
mismo contrato mediante el bridge, pero la ventana principal no tiene selector
de backend propio: el perfil es la entrada del usuario.

## 2. Monitor de streaming: telemetría mínima y verificable

### Principio

Un valor `null` en `NLStreamMonitorSample` significa **la fuente no lo entrega**
y la UI lo muestra como `—`. Nunca se rellena con cero, se estima ni se
republica una muestra antigua como si fuera fresca. Si un transporte sigue
activo pero no reporta progreso durante 4 segundos, el estado pasa a
`Sin señal del codificador`.

### Datos mostrados

| Señal | RTMP (ffmpeg) | OBS WebSocket v5 |
| --- | --- | --- |
| Estado | proceso ffmpeg activo; errores reales de stderr; `Sin señal` si no hay progreso | `outputActive`, `outputReconnecting` |
| FPS | `fps=` de `-stats` | `activeFps` de `GetStats` (FPS de render de OBS, rotulado como tal) |
| Bitrate | `bitrate=` de `-stats` (kbits/s) | calculado del delta de `outputBytes`; se anula si el contador se reinicia |
| Duración | `time=` de `-stats` | `outputDuration` (ms) |
| Salud | `speed=` de `-stats` (velocidad real del codificador) | `outputCongestion` |
| Detalle | línea real de progreso (`q=`) o error literal de ffmpeg | `outputTimecode` y `outputSkippedFrames` |

Los campos de OBS se verificaron contra la documentación generada del
protocolo: <https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md>
(`GetStreamStatus` y `GetStats`, RPC v1).

### Métricas retiradas

No se muestran porque la fuente no las reporta o porque no cambian una decisión:

- `outputFrames`, `outputDroppedFrames` y `outputBitsPerSec`: **no existen** en
  `GetStreamStatus` de OBS WebSocket v5. Se usan `outputTotalFrames`/
  `outputSkippedFrames` y `outputBytes` cuando corresponde.
- `outputBytes` total y `q=` como casillas separadas: el primero no aporta una
  decisión en tiempo real y el segundo queda en el detalle de ffmpeg.
- `DroppedFrames`/porcentaje en RTMP: ffmpeg no lo entrega; mostrar `0 (0%)` era
  engañoso.
- `Actualizado` como reloj decorativo: la antigüedad se resume en el estado
  `Sin señal`.

### Modo OBS

`NLStreamingService` implementa WebSocket v5: handshake con autenticación,
`SetCurrentScene` opcional, `StartStream`/`StopStream` y lectura de estado. La
implementación cubre el control de la transmisión y su monitorización; **no**
replica la pipeline interna de codificación de OBS, que sigue siendo
responsabilidad de OBS Studio. Las credenciales nunca se incluyen en eventos.

## 3. Home y LinkEngine

- Windows (`NLUIWindowMain.xaml`): la tarjeta «CONECTAR LINKENGINE» con el
  selector de ruta (USB/LAN), «PREPARAR ESTA RUTA» y «CONFIGURAR ADB
  INALÁMBRICO» está en `HomePage14`. La página `NetworkPage14` conserva las
  mediciones técnicas.
- Linux: `BuildHomePage()` crea la tarjeta de conexión con los mismos controles;
  `BuildLinkPage()` muestra estado, mediciones y pruebas, y ya no contiene el
  expander de preparación del canal.

## 4. Comprobaciones realizadas

- `NLUIWindowMain.xaml` parsea como XML válido.
- Comprobación de balance de llaves/paréntesis en los 22 archivos `.cs` de
  `src/Desktop`.
- Búsqueda de referencias obsoletas a los campos eliminados
  (`StreamMonitorFrames`, `outputBitsPerSec`, `sample.Live`, etc.): sin
  coincidencias.
- No hay compilador C# (`dotnet`, `csc`, `mcs`) en el entorno.

## 5. Pendiente para integrar

1. Copiar los partial classes y XAML adaptados al proyecto de escritorio
   original y compilar con .NET/SDK correspondientes.
2. Validar con un servidor RTMP real y con OBS WebSocket v5 real: estados,
   reconexión y bitrate derivado de bytes.
3. Validar el perfil personalizado en una PC con GPU Intel/NVIDIA/AMD y en un
   equipo sin proveedor conocido.
4. Revisar que la licencia NLPL v1.1 permita la redistribución antes de publicar
   cualquier versión modificada.
