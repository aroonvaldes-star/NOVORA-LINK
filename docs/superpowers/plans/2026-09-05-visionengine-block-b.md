# VisionEngine Block B Implementation Plan

**Goal:** Integrar Control, Audio, Gamepad y Exchange sobre Block A sin renderer de video.

**Architecture:** Mantener `scrcpy-server` 4.1 como backend Android temporal, abrir los sockets video/audio/control en el orden del protocolo y sustituir el cliente externo con módulos C# VisionEngine. Audio usa FFmpeg+SDL3; gamepad usa SDL3+UHID; exchange usa control+ADB.

**Tech Stack:** .NET 8 WPF, ADB, scrcpy-server 4.1 protocol, FFmpeg DLLs, SDL3.

## Constraints

- Naming `<Función><Carpeta>VE.cs`.
- No `scrcpy.exe` desde VisionEngine.
- No renderer/Direct3D/Bitmap en Block B.
- Video continúa headless.
- Gamepads máximo 8, UHID IDs 3..10.
- Compilar/tests en Windows antes de prueba física.

## Tasks

- [x] Control wire protocol y response reader.
- [x] Audio demux/decode/playback.
- [x] Gamepad SDL3/UHID.
- [x] Clipboard/files/images/media exchange.
- [x] Multi-socket transport y server options.
- [x] Runtime/Core integration.
- [x] xUnit source tests + Windows harness.
- [x] Static naming/delimiter/native-export verification.
- [ ] Windows `dotnet build` and `dotnet test` (requires user machine).
- [ ] Galaxy A56 5G physical run (requires device).
