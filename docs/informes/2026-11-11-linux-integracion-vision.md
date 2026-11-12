# Linux · Integración VisionEngine, micrófono y ventana — 2026-11-11

## Alcance

Cambios en `src/LINUX` (PC Linux), contrato compartido de oferta VE (`src/WINDOWS/Control`, enlazado en Android) y `src/ANDROID` solo para el micrófono. No se hizo commit ni push. No se instaló nada en el teléfono ni se reconstruyó el APK. Los cambios se suman a otros cambios sin confirmar del repositorio.

## Cambios

1. **Carpeta de recepción NOVORA-Files.** El destino por defecto pasa de `~/Descargas/NOVORA` a `Escritorio/NOVORA-Files` (`VisionEngine/LinuxVisionSettings.cs`, `DefaultReceiveDirectory`). Una configuración guardada que aún apunta a la carpeta antigua se migra; una carpeta elegida a mano se respeta. El host crea la carpeta al arrancar (`Control/LinuxControlHost.cs`). `NLControlFileStorage.PcRoot` deja de duplicar la ruta.
2. **Botón CAMBIAR CARPETA** en la tarjeta Integración «Archivos / Share» (`MainWindow.cs`). Reutiliza `ChooseVisionDirectoryAsync(false)` y muestra la carpeta actual.
3. **Micrófono Android → PC.**
   - Contrato: campo opcional `AudioSource` (`Output` por defecto, `Microphone`) en `NLControlVideoSourceOffer` y `NLControlVeLanOffer` (`src/WINDOWS/Control/NLControlProtocol.cs`, `NLControlVeLanProtocol.cs`). Windows no cambia de comportamiento.
   - Android: `NLAndroidAudioCapture` captura con `AudioSource.Mic` en mono y duplica las muestras a estéreo; en modo micrófono no silencia la reproducción local. USB (`NLAndroidVideoService.cs`): sin permiso `RECORD_AUDIO` el canal se conecta en silencio, para que el PC no falle a los 30 s. LAN ya quitaba el canal sin permiso.
   - PC: `LinuxVisionSettings.AudioSource`, enviado en las ofertas USB (`LinuxVisionEngine.cs`) y LAN (`LinuxVisionLanProtocol.cs`, `LinuxVisionLanSession.cs`). La tarjeta «Micrófono Android» tiene botón ACTIVAR/DESACTIVAR y activa el audio del PC al activarse.
4. **Ventana VisionEngine.**
   - Al mover o cambiar la escala de la ventana se vuelve a dibujar el último fotograma (`LinuxSdlVideoRenderer.cs`, `DrawCurrentFrame`). Antes el mapeo se recalculaba pero la imagen solo cambiaba con un fotograma nuevo del teléfono.
   - Posición: se guarda en `~/.config/novora-link-linux/vision-window.json` tras un movimiento real (con 500 ms de espera) y al cerrar. Se usa al abrir, salvo que se elija un monitor. Nuevo archivo: `VisionEngine/LinuxVisionWindowPlacement.cs`. No se guarda si SDL no informa de movimientos, para no escribir `0,0`.
   - XWayland solo para VE: `VisionEngine/LinuxSdlVideoDriver.cs` fija `SDL_VIDEO_DRIVER=x11` antes de inicializar el vídeo de SDL, en `LinuxSdlVideoRenderer.cs` y `LinuxSdlDevices.cs`. Solo actúa con `DISPLAY` presente y sin driver forzado; si falla, vuelve al driver por defecto. Avalonia sigue en Wayland.

## Verificación

| Comprobación | Resultado |
|---|---|
| `dotnet build src/LINUX/NOVORA.Linux.csproj` | 0 errores |
| `dotnet build` proyecto de pruebas Linux (Release) | 0 errores |
| Build `src/ANDROID/NLProjectAndroid.csproj` (`AndroidSdkDirectory` explícito) | 0 errores |
| Pruebas de protocolo Linux (`NOVORA.Linux.ProtocolTests`) | Aprobadas, incluidas ajustes de VE con `AudioSource` |
| `--ve-exchange-native` | 44 PASS / 0 FAIL |
| `--ve-runtime-native` | Aprobada |
| Sonda SDL3 en el scratchpad, driver X11 | Driver `x11`; ventana creada en `300,200` según SDL |
| Sonda SDL3, driver por defecto (Wayland) | Driver `wayland`; posición informada igual, no concluyente |

## No verificado / pendiente

- **Windows** no se ha compilado (`net8.0-windows`). Los cambios del contrato son parámetros opcionales al final.
- **Micrófono** sin teléfono: `AudioRecord` con `AudioSource.Mic`, calidad, latencia y permiso en la primera ejecución.
- **Ventana**: no se ha comprobado con arrastre real. Mover la ventana después de crearla no funcionó en la sonda (el gestor la recolocó), así que la posición guardada solo se aplica al abrir. Falta confirmar el desajuste corregido, el guardado al cerrar y el aspecto con escalado fraccional bajo XWayland.
- **Micrófono PC → Android**: no implementado (tampoco en Windows).
- **APK**: no reconstruido ni instalado.
- **Revisión física PC–Android** (USB, LAN, vídeo, audio, entrada, archivos) sigue pendiente, como en las bitácoras anteriores.
