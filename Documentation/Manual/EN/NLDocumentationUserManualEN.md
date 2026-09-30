# User Manual - NOVORA-LINK 1.4 PRERELEASE

## English - secondary language

Updated: September 27, 2026.

### 1. What NOVORA-LINK is
NOVORA-LINK connects a Windows PC with one active Android device. The PC owns the main session and Android uses the `com.novora.appcontrol` app.

This manual describes the 1.4 baseline under validation. It does not make the current folder a stable Release.

### 2. Release status
The following evidence belongs to the earlier 1.4 validation and is retained for reference. The current baseline removed STEngine and must repeat the affected gates before a new Release:

- Desktop Release: 0 warnings and 0 errors.
- .NET suite: 405/405 tests passed.
- RelayCore: 34/34 tests passed; one benchmark remains ignored by design.
- Android Release: `com.novora.appcontrol` version `1.4.36`, code `36`, minSdk `26`, targetSdk `36`.
- Canonical and installed APK: SHA-256 `8F2E6E81FD70B692FD0E370A75A4BC482D598B468D70A0B4A12A61236A4BE405`.
- Samsung SM-A566E `R5CY3118MEW`: incremental install and `MainActivity` launch passed.
- Windows installer: build, install, launch and uninstall passed.

### 3. Basic requirements
- Windows 11 is recommended for the PC.
- One active Android phone per PC.
- A reliable USB cable for USB mode.
- USB debugging authorized on the phone when USB is used.
- A trusted local network when LAN/QR is used.

Do not connect multiple active phones to the same PC session; NOVORA is designed to focus resources on one device.

### 4. Connection
**USB:** connect the phone, accept USB debugging authorization and let NOVORA prepare the session. USB is the preferred route for controlled tests and performance work.

**LAN/QR:** use discovery, invitation or QR when available. Before accepting, confirm that the PC name and network are correct.

NOVORA avoids constant polling whenever it can. Repeated Android queries should only be used when there is no practical alternative.

### 5. Engines
- **LinkEngine:** provides PC Internet access to Android through the NOVORA tunnel.
- **VisionEngine:** handles capture, video, audio, decoding, rendering, and recording.
- **ExInEngine:** handles mouse, keyboard, touch, and gamepad input.
- **NOVORA Integrations:** handles files, Drag and Drop, clipboard, and Android-Windows features without requiring active video.
- **ExInEngine:** translates external input, controllers and control sessions when the backend is available.

Each engine keeps its own recovery scope. A failure in one engine should not bring down all of NOVORA or unnecessarily stop independent engines.

### 6. LinkEngine
The **Network** panel starts and stops LinkEngine. When active, NOVORA prepares the control and data channels used by the tunnel.

Congestion, `WouldBlock`, high queues or backpressure are not by themselves Recovery reasons. Recovery remains a last resort when the engine actually loses its operating state.

### 7. VisionEngine
The **Display** panel configures resolution, FPS, bitrate, monitor, audio and video profile. Start VisionEngine only when you want to view/control Android from the PC.

VisionEngine uses the transmission component integrated with NOVORA-LINK to display and control Android from the PC.

To control Android, enable **NOVORA Control** in Accessibility and **NOVORA Keyboard** in keyboard settings. Text follows the active Windows input language (for example ES or EN); Android receives the resolved Unicode characters. Physical UHID keyboard mode remains under investigation and is not advertised as available.

### 8. ExInEngine and Game Input
Supported controllers use the SDL/UHID route when the required backend is available. Recent physical validation detected an Xbox One Elite 2 Controller (`045E:028E`) and received live neutral telemetry.

VisionEngine/ExIn coexistence was corrected: each session now uses its own SCID-isolated remote server, preventing VisionEngine from overwriting the ExIn control session.

Pending: test every stick, trigger and button with complete Android-side translation.

### 9. Android app
On Android you will find:

- **Connect:** USB/LAN preparation.
- **Control:** engines and available tools.
- **Settings:** options confirmed by the PC.
- **NOVORA Files:** file functions when the backend is available.
- **Manual:** Spanish and English content.

The current app package is `com.novora.appcontrol`.

### 10. Android-Windows integration
NOVORA includes infrastructure for clipboard, files, Drag & Drop, sharing, notifications, capture, recording and other Android-Windows capabilities. Some features depend on their backend being ready and tested.

Do not treat a feature as final just because it exists, builds or appears in the UI. Distinguish between exists, builds, integrated, connected, functional and physically tested.

### 11. Privacy and security
NOVORA minimizes personal-data storage. It should not unnecessarily store passwords, accounts, emails, contacts, OTP codes, tokens, cookies, clipboard history or private content.

**Privacy Shield** may prevent video exposure, new audio, control, gamepad, clipboard and transfers in sensitive contexts.

Do not disable antivirus, Windows protections or security controls as a general workaround.

### 12. Updates
NOVORA may check the official channel when the app opens. It should not continuously poll only to discover updates.

Stable updates are published through the official NOVORA-LINK channel.

### 13. Troubleshooting
1. Verify that the phone is still connected and authorized.
2. Confirm that you are using the expected APK/version.
3. Check the affected engine status.
4. Restart only that engine when possible.
5. Avoid restarting all of NOVORA when an independent engine can recover.
6. Report the first concrete error, the complete version and whether it used USB, LAN or QR.
