# NOVORA Discovery / AutoStart Implementation Plan

> Direct implementation on `C:\Users\Aroon\Desktop\NOVORA-LINK`. No worktree. Changes stay in the real repo so compiler failures can be repaired in place.

**Goal:** Make NOVORA PC start subtly with Windows, remain in the system tray, and make NOVORA Android discover/reconnect to the paired PC automatically over USB first, then LAN and Internet in later gates without application polling.

**Architecture:** Discovery is event-driven. USB uses `adb track-devices` plus a directed Android broadcast after `adb reverse` is confirmed. Android keeps a dedicated presence socket that blocks on async I/O and reports real disconnects. LAN and Internet are added only after this base compiles so the unauthenticated loopback Remote channel is never exposed to the network.

**Tech Stack:** WPF .NET 8, .NET 10 Android, ADB, TCP async I/O, Windows tray via WinForms NotifyIcon.

## Global Constraints

- No application polling loops.
- Timers are allowed only for startup delay, timeout, debounce, pacing, or recovery backoff.
- `WouldBlock`/queue pressure remains Traffic/Performance, not Recovery.
- One Android is paired to one active NOVORA PC at a time.
- No account, email, password, telemetry, or personal content storage.
- Closing MainWindow hides it; only tray `Salir de NOVORA` performs real shutdown.
- `Salir de NOVORA` suppresses future autostart until NOVORA.exe is opened manually.
- Windows autostart delay is exactly 5 seconds.
- Android notifications are silent/discreet and only reflect connection/recovery state.

---

### Task 1: Windows lifecycle and tray

- Add `AutoStartServiceNV`.
- Add `TrayServiceNV`.
- Add `StartWithWindows`/`AutoStartSuppressed` settings.
- Start hidden after 5 seconds for `--autostart`.
- X hides MainWindow; tray Exit performs real shutdown.
- Use the NOVORA executable icon for the tray.

### Task 2: USB DiscoveryEngine

- Add `AdbTrackDevicesDiscoveryNV` using `adb track-devices`.
- Refresh devices only on ADB snapshot changes.
- After `adb reverse tcp:27182` is verified, send `com.novora.linkengine.DISCOVERY_READY`.
- No device scan timer.

### Task 3: Android boot, presence, and notifications

- Add boot/package receiver.
- Add a light foreground discovery service.
- Add a dedicated presence connection to Remote 27182.
- Notify: connected, disconnected, recovering, recovered, recovery failed.
- Do not auto-start VisionEngine or LinkEngine.

### Task 4: LAN pairing/discovery gate

- Add one-PC cryptographic pairing.
- Add QR plus PIN fallback.
- Only after authentication exists, allow a LAN listener and event-driven LAN discovery.
- Never expose current unauthenticated Remote listener to LAN.

### Task 5: Internet rendezvous/relay gate

- Add a minimal self-hostable rendezvous service.
- Try direct peer path first.
- Use relay only when NAT/CGNAT prevents direct connection.
- Remote control may relay; VisionEngine/LinkEngine data paths remain direct whenever possible.

### Verification

1. `dotnet build NOVORA.sln -c Release`
2. `dotnet build NOVORA.linkEngine.Android\NOVORA.linkEngine.Android.csproj -c Release -t:SignAndroidPackage`
3. Install signed APK with ADB.
4. Close MainWindow with X and verify NOVORA remains in tray.
5. Reconnect USB and verify the Android notification changes without manual refresh.
6. Exit from tray, reboot/sign-in, verify NOVORA does not autostart.
7. Open NOVORA.exe manually, sign out/in, verify hidden autostart after 5 seconds.
