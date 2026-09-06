# VisionEngine — Block A

## Alcance

Block A implementa el primer pipeline ejecutable de VisionEngine sin renderer:

```text
Core
  ↓
Device (ADB)
  ↓
Transport (adb reverse; forward fallback)
  ↓
Android server (scrcpy-server 4.1 compatibility)
  ↓
Protocol (codec/session/media headers)
  ↓
Video demuxer
  ↓
H.264/H.265 configuration merger
  ↓
FFmpeg decoder
  ↓
Decoded-frame counting with session metadata
  ↓
DISCARD
```

No existe `RendererVideoVE`, Direct3D, WPF Image, BitmapSource ni una ventana de pantalla en este bloque.

## Reutilización de scrcpy 4.1

NOVORA conserva `Tools/scrcpy-server` como backend Android transitorio. VisionEngine no inicia `scrcpy.exe` para Block A. La parte Windows implementada aquí toma como referencia la arquitectura/protocolo de scrcpy 4.1:

- SCID de 31 bits y socket `scrcpy_<8hex>`.
- `adb reverse localabstract:<socket> tcp:<port>` como transporte preferido.
- `adb forward tcp:<port> localabstract:<socket>` como fallback.
- Device name de 64 bytes en el primer socket.
- Codec id de 4 bytes big-endian.
- Session/media header de 12 bytes.
- bit 63 = session, bit 62 = config, bit 61 = key frame.
- Config H.264/H.265 retenida y antepuesta al siguiente media packet.
- Decode mediante FFmpeg `avcodec_send_packet()` / `avcodec_receive_frame()`.

Los avisos/licencias de scrcpy se mantienen en `THIRD-PARTY-NOTICES.md` y `Tools/LICENSE.txt`.

## Prueba en Windows

Primero compilar/validar:

```powershell
.\scripts\Test-VisionEngine-BlockA.ps1
```

Con un Android autorizado por ADB:

```powershell
adb devices
.\scripts\Test-VisionEngine-BlockA.ps1 -DeviceSerial TU_SERIAL -Seconds 15
```

Una prueba correcta termina con paquetes y frames decodificados mayores a cero, cero errores de decode y `Renderer enabled : False`.
