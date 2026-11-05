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

VisionEngine supports video and optional audio over an authorized USB session
and native LAN capture over an authorized LAN session. USB uses token-authenticated
ADB reverse endpoints; LAN prepares independent, short-lived TLS video, control,
and optional audio channels, pinned to the existing Linux trust-store certificate
and authenticated with per-channel tokens. The AppControl `startVideoLan` action
returns the LINK Control 1.4.124 offer format. Both paths parse VE framing, decode
video with FFmpeg, and present frames through SDL3; LAN audio is optional and has
a bounded connection grace period. FFmpeg uses its default software decoder and
applies the rotation announced by the stream. The current Android video path sends
H.264 and PCM S16LE stereo audio at 48 kHz. Protocol, authenticated-channel,
control-action, FFmpeg rotation, and SDL checks run locally, including LAN
loopback tests; physical-device audio/video playback and end-to-end LAN capture
are still unverified. Remote input, file exchange, recording, and configurable
stream profiles are not integrated.

The SDL video renderer now selects SDL's default backend (override with
`NOVORA_SDL_RENDERER=opengl|vulkan|software`), uses linear texture scaling,
preserves the video aspect ratio, and creates a high-pixel-density resizable
window. The VisionEngine page reports the selected SDL backend, output pixel
size, video packets, rendered frames, and an approximate packet/frame difference;
the same telemetry is included in AppControl snapshots. The difference is
diagnostic only and is not a precise network-drop counter. On the development
host, `glxinfo` reports Mesa `llvmpipe`, so an `opengl` SDL backend there does
not imply hardware acceleration. A physical-device comparison against Windows
at native and fractional scaling is still required.

Protocol framing and LAN/USB pairing/resume follow LINK Control protocol v1 used by
LINK Control 1.4.124. A USB-authorized `startLink` now launches the Linux LinkEngine
relay and CONTROL listener, authenticates the Android HELLO/heartbeat, and prepares
ADB reverse ports 27183/27184 without replacing existing routes. Only reverse routes
created by this app are removed on stop. This USB LinkEngine path has not yet been
confirmed end-to-end on physical Android hardware. LAN LinkEngine, ExInEngine,
file transfer, and LinkEngine speed/telemetry controls remain unavailable.

## Protocol and trust checks

```sh
cargo test --manifest-path src/NOVORA-LINUX/LinkRelay/Cargo.toml --locked
dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj
```

With an authorized physical Android device attached and LINK Control 1.4.124
installed, the optional hardware check can be run with
`dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -- --device <adb-serial>`.
