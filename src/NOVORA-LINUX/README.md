# NOVORA-LINK for Linux

This is a standalone Avalonia application. All Linux-specific files, runtime code,
and tests live in this directory; it does not edit or link to the Windows desktop
project.

The interface reuses NOVORA's Windows brand assets (emblems, wordmarks, app icon,
and Montserrat fonts) and provides Light/Dark themes from Settings. The selected
theme is saved in `$XDG_CONFIG_HOME/novora-link-linux/theme` or
`~/.config/novora-link-linux/theme`.

## Build and run

The Linux build requires .NET 8, Rust/Cargo, Android platform-tools (`adb`),
FFmpeg (`ffmpeg` executable), and SDL3 (`libSDL3.so.0`).

```sh
dotnet run --project src/NOVORA-LINUX/NOVORA.Linux.csproj
```

The app discovers private IPv4 interfaces, selects authorized physical USB
devices reported by the system `adb` executable, and exposes pinned-TLS control
over LAN (TCP 27215) or USB reverse (device TCP 27214). USB opens LINK Control
through its protected `UsbBootstrapActivity` and only owns/removes an ADB reverse
mapping it created. LAN discovery is on UDP 27216; the pairing code expires after
two minutes and is single-use. The certificate and trusted-device hashes are kept in
`$XDG_CONFIG_HOME/novora-link-linux` (or `~/.config/novora-link-linux`) with
user-only file permissions.

## Dynamic connectivity (USB or LAN, no route selection)

When the application starts, `LinuxAutoConnector` keeps LINK Control reachable
without asking the user to choose USB or LAN first:

- **LAN** listens automatically on the active private network (physical interface
  with a default gateway first; Docker/libvirt/VPN bridges are skipped) and moves
  to the new address when the network changes. A manual «Detener control LAN»
  keeps LAN off until it is prepared again.
- **USB** follows ADB hotplug with `adb track-devices -l` (no polling). An
  authorized USB phone is bootstrapped through `UsbBootstrapActivity`; a lost
  session is bootstrapped again while the cable stays connected. Bootstraps that
  LINK Control never confirms are bounded to 3 per plug-in (they open the app on the
  phone); replugging or «Enlazar por USB» restores them. Two USB phones are never
  chosen silently. ADB over Wi-Fi is not treated as USB.
- A LAN session with running engines is not moved to a newly plugged cable; an
  idle LAN session moves to USB.
- Engines use whichever route is authorized (USB preferred). A running capture
  keeps its route; a preferred but unauthorized route never blocks the other.
  Engines still start only when the user (PC or Android) asks for them.
- LINK Control 1.4.125+ stores the PC after an ADB-bootstrapped session, finds its
  LAN address by certificate fingerprint and continues over Wi-Fi when the cable is
  removed, retrying every 1, 2, 4, 8 and then 15 s while in foreground. A phone that
  never linked over USB still needs the one-time LAN pairing code.

Pure policy, ADB framing and the connector state machine are covered by
`dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release -- --auto-connect`.
`--control-only` runs connection, trust, USB tunnel and LinkEngine LAN checks
without the media fixtures.

## VisionEngine

The VisionEngine page now connects its settings and actions to the USB/LAN runtime:

- Persistent capture profiles, bitrate, maximum size and FPS; Android batch settings
  use revisions and share the same configuration transaction as the Linux UI.
- Automatic/software/forced hardware decoding. VAAPI, CUDA, QSV and Vulkan are
  offered for probing with a real H.264 decode and RGBA download. A forced backend
  fails explicitly when unavailable. SDL rendering and video decoding are reported
  separately; an OpenGL renderer does not prove hardware decoding.
- Monitor and audio-device enumeration, stable saved names, window/fullscreen,
  VSync and low-latency decoding. Output/decoder changes apply to the next session.
- Keyboard, text Unicode, mouse clicks/drag and horizontal/vertical fractional
  touchpad scrolling. The SDL event timer works even when no video frames arrive;
  focus loss releases input and closing the video window stops the session.
  Android requires LINK Control accessibility. Android cursor navigation, Tab,
  selection and edit shortcuts were extended; unrestricted global raw key injection
  remains outside accessibility capabilities.
- H.264/optional PCM recording to unique MKV files, original video timestamp deltas,
  audio clock normalization and segments on rotation/geometry change. A bounded
  background writer avoids blocking the decoder; failed captures retain `.partial`
  files and visible diagnostics. No audio track is published when no audio arrived.
- Bidirectional FILES V2 during VE, individual files or directories, progress,
  cancellation, generation-bound leases, SHA-256 verification and confined receive
  storage without overwrite. LAN uses a separate authenticated TLS channel; USB
  uses a separate token-authenticated ADB reverse endpoint on device port 27218.
  Android-to-PC transfers use the compatible generation-bound budget negotiation
  in Observe mode (Linux does not advertise adaptive network shaping).

Use **Solicitar pantalla a Android** with LINK Control visible, then accept Android's
screen-capture permission. Remote input needs the accessibility permission enabled
on the phone. Choose recording/receive folders before starting a new session.
USB FILES V2 and the additional keyboard behavior require an Android APK built from
this updated source; the previously recorded 1.4.124 APK has not been rebuilt or
installed by this change.

Local verification covers framed protocols/trust, batch settings and stale
revisions, decoder reconfiguration, SDL events and selected outputs, real FFmpeg
and MKV decoding, files and failure cleanup. The native runtime check sends live
video/audio over sockets through FFmpeg and SDL and finalizes a recording.

Physical-device USB/LAN capture, real laptop touchpad-to-Android interaction,
GPU hardware decoding and multi-monitor/fractional-scaling behavior still require
hardware validation. On this host all probed hardware decoders were unavailable;
software decoding passed. Mesa/Zink warnings do not establish physical GPU use.

Protocol framing and LAN/USB pairing/resume follow LINK Control protocol v1 used by
LINK Control 1.4.124. An authorized `startLink` now selects the transport of the
active control session. USB launches the Linux LinkEngine relay and CONTROL
listener, authenticates the Android HELLO/heartbeat, and prepares ADB reverse
ports 27183/27184 without replacing existing routes. LAN launches the same relay
on loopback and an ephemeral TLS DATA gateway bound to the authorized private
address; the gateway pins the trust-store certificate, validates the single-use
token, authenticates DATA against the relay, and returns the LINK Control
`{Host,Port,Fingerprint,Token}` offer. Only reverse routes created by this app are
removed on stop. The LAN gateway and transport routing are covered by loopback
tests, but neither USB nor LAN LinkEngine has been confirmed end-to-end on
physical Android hardware. ExInEngine, file transfer, LinkEngine telemetry, and
the speed test remain unavailable.

## Protocol and trust checks

```sh
cargo test --manifest-path src/NOVORA-LINUX/LinkRelay/Cargo.toml --locked
dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj
dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -- --le-lan-only
```

With an authorized physical Android device attached and LINK Control 1.4.124
installed, the optional hardware check can be run with
`dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -- --device <adb-serial>`.

## Focused native VE checks

After building the test project, run each mode in a separate process:

```sh
dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release -- --input-native
dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release -- --outputs-native
dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release -- --ve-ui
SDL_VIDEO_DRIVER=dummy SDL_AUDIO_DRIVER=dummy NOVORA_SDL_RENDERER=software dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release -- --ve-runtime-native
```

The runtime fixture currently requires the `libopenh264` encoder. Main protocol
and recording checks detect either `libx264` or `libopenh264`. `--ve-ui` creates a
temporary configuration and renders `/tmp/novora-ve-ui.png`.
