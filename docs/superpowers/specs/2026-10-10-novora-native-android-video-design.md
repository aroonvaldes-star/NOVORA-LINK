# VisionEngine with NOVORA's Android capture service

## Purpose and decision

VisionEngine must obtain Android video, audio, and control through App Control, NOVORA's Android companion app. App Control is required for a new capture session. The Windows application must no longer launch or package the scrcpy client or server. Existing USB and LAN connections remain supported.

This design follows the user's decision to use App Control as the Android component rather than maintain a second, PC-launched Android server.

## Existing foundation

- App Control already captures the display with Android `MediaProjection` and encodes H.264 with `MediaCodec` in `NLAndroidVideoService` (USB) and `NLAndroidServiceVeLanCapture` (LAN).
- It already streams audio and accepts remote control through the authenticated NOVORA connection.
- The Windows VisionEngine runtime already accepts App Control streams with `StartAppControlAsync` (USB) and `StartExternalSourceAsync` (LAN).
- A separate legacy path still pushes `Tools/scrcpy-server` over ADB and launches `com.genymobile.scrcpy.Server`. The standalone `scrcpy.exe` remains callable from the console and is required by packaging scripts.
- ExInEngine independently starts that server in control-only mode to create Android UHID gamepads while video is off. App Control's accessibility service does not currently support UHID; the replacement must preserve this behavior.

## Target behavior

1. A user connects App Control to NOVORA over authorized USB or LAN, starts VisionEngine, grants Android screen capture permission, and receives video, audio, and remote control on Windows.
2. Windows requests capture through the existing authenticated App Control control session. Android initiates the media streams. Capture cannot start silently or without Android's `MediaProjection` consent.
3. Starting VisionEngine on Windows without a connected App Control session gives a clear action message to connect App Control. The old direct ADB server path is retired, including its automatic recovery path.
4. Reconfiguration, orientation changes, keyframe recovery, session stop, USB reconnect, LAN reconnect, recording, privacy gates, and audio enabled/disabled modes retain their current observable behavior where supported by App Control.
5. A new build contains no scrcpy executable or server binary and no path that launches, pushes, validates, or downloads either one.
6. ExInEngine continues to create and drive Android gamepads with and without an active video stream. A NOVORA-owned PC adapter uses Android's system `hid` command through ADB for shell-level UHID access where ordinary App Control permissions cannot provide this capability.

## Components and data flow

`NLUIWindowMain` chooses the authorized App Control USB or LAN source and sends the existing capture request. Android's capture service owns `MediaProjection`, `MediaCodec`, audio capture, and control execution. The Windows `VECoreRuntime` consumes the resulting streams and continues using its existing decoder, renderer, audio playback, recorder, and privacy gates. The direct `VEDeviceManager` → `VEServerManager` → legacy `VETransportManager` start path is removed once no active caller needs it.

ExInEngine's standalone output must migrate from the legacy control-only server to a NOVORA-owned adapter for Android's system `hid -` command over the selected, authorized ADB device. Each virtual device owns a process so closing its input removes the device. The adapter validates report and descriptor sizes, serializes writes, detects process failure, and stops on disconnect. It does not capture screen or audio. App Control remains the required application for VisionEngine video.

The current App Control media writer and Windows reader must use one NOVORA-owned, versioned wire contract. Add an explicit protocol identity and version so an incompatible App Control or Windows build fails clearly before media decoding. Keep strict size and frame limits. Remove scrcpy-specific class names, socket names, command formats, and compatibility types from active code when the legacy path is removed. The protocol change must be applied to both USB and LAN in one compatible release.

## Packaging and source cleanup

Remove `Tools/scrcpy.exe` and `Tools/scrcpy-server`, their runtime path properties, console command, setup downloader requirement, installer checks, and references in active release scripts. Audit the remaining DLLs individually: retain FFmpeg and SDL3 because VisionEngine actively loads them; remove files used only by the retired scrcpy client if no other consumer exists. Update build, installer, release checks, and current documentation to state the App Control requirement.

Review third-party notices after dependency removal. Required notices for retained binaries or copied/derived source stay. Historical provenance may remain in development documentation; no runtime or package should claim a scrcpy dependency after removal.

## Verification and release conditions

- Unit and contract tests cover protocol identity/version, malformed or oversized frames, video session updates, audio enabled/disabled, control messages, cancellation, and reconnect boundaries.
- ExIn tests cover UHID create/input/destroy, helper loss, cleanup, and reactivation without video.
- Desktop and Android builds pass; the signed APK and installer contain the expected App Control and native dependencies, with no scrcpy executable/server.
- On a physical Android device, USB and LAN sessions demonstrate capture permission, video, audio, control, rotation, stop/restart, and reconnect. Compare key latency and dropped-frame metrics with the current App Control path to detect regressions.
- Search active source, scripts, and package contents for remaining scrcpy launch, push, download, and validation paths. Document any retained attribution separately.

## Constraints and risks

- Keep the user's current uncommitted work intact; changes must be limited to the capture migration and related package/documentation surfaces.
- Android 10+ remains required for playback audio; capture consent and accessibility authorization follow the current App Control flow.
- An installed old Windows or App Control release may not understand the new media protocol. Reject mismatched versions with a clear upgrade message; do not silently fall back to scrcpy.
- Physical USB/LAN verification is required before claiming full runtime replacement. Build success alone is insufficient.
# Estado de implementación (2026-10-10)

Se retiraron `scrcpy.exe`, `scrcpy-server`, el gestor de servidor Android heredado y las rutas directas de inicio. La captura USB/LAN existente se inicia desde App Control. En un Samsung físico con AppControl 1.4.90 se verificó por LAN la autorización MediaProjection y video actualizado en el renderizador de Windows al abrir el teclado Android. Quedan por validar audio, control, rotación, reconexión y la ruta de video USB. No se creó un instalador nuevo. La sustitución del relay derivado de Gnirehtet sigue pendiente antes de declarar completa su eliminación.

