# VisionEngine — Block B

Block B amplía el pipeline headless de Block A con cuatro subsistemas sin habilitar el renderer de video:

- `Control/`: teclado, mouse, touch, comandos, clipboard y UHID con el protocolo de control compatible con scrcpy 4.1.
- `Audio/`: demux, decode FFmpeg a PCM16LE estéreo 48 kHz y reproducción mediante SDL3.
- `Gamepad/`: descubrimiento SDL3, normalización Xbox/PlayStation y envío UHID de 15 bytes a Android.
- `Exchange/`: clipboard de texto, archivos ADB bidireccionales, imágenes y media con MediaScanner.

## Pipeline

```text
Android scrcpy-server 4.1
  ├─ video   -> ProtocolVE -> DecoderVideoVE -> discard (renderer OFF)
  ├─ audio   -> DemuxerAudioVE -> DecoderAudioVE -> PlayerAudioVE/SDL3
  └─ control <-> ManagerControlVE
                  ├─ Keyboard/Mouse/Touch/Commands
                  ├─ ClipboardExchangeVE
                  └─ ManagerGamepadVE -> SDL3 -> UHID

ADB
  └─ FileExchangeVE <-> /sdcard/Download
```

## Reglas preservadas

- No se ejecuta `scrcpy.exe` desde VisionEngine.
- `scrcpy-server` se mantiene como backend Android temporal mientras se sustituye gradualmente.
- No existe `RendererVideoVE`, superficie Direct3D ni presentación de frames.
- Todos los archivos siguen `<Función><Carpeta>VE.cs`.
- Los snapshots de métricas de video/audio/control/gamepad se limitan para no competir con los data planes.

## Prueba en Galaxy A56 5G

```powershell
.\scripts\Test-VisionEngine-BlockB.ps1 -DeviceSerial "SERIAL_ADB" -Seconds 20
```

Durante la prueba, reproduce audio en el teléfono para validar el flujo `AudioVE`.
