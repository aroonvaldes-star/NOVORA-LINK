# VisionEngine Linux Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development to implement bounded components, then integrate and review the combined tree.

**Goal:** Complete functional VE settings, acceleration, input including laptop touchpad, recording, files and monitor/audio outputs.

**Architecture:** Extend existing Avalonia/SDL3/FFmpeg and Android contracts. Shared immutable settings and per-session services feed USB and LAN; the coordinator owns engine integration and UI.

**Tech Stack:** .NET 8, Avalonia, SDL3, FFmpeg, existing FILES V2 and Android accessibility.

**Spec:** `visionengine-cierre-design.md` (approved by user with Android input changes and touchpad verification).

## Global Constraints

- Work in Dev_Local, preserve all existing local changes. No commit, push or installations.
- Linux VE and indispensable Android keyboard changes only. Windows contracts may be read but not modified.
- Forced acceleration never silently falls back. Hardware absence is unverified, not a pass.
- A single writer/reader per wire channel; retain TLS/pinning, tokens, generation and revocation.
- Existing USB/LAN and LinkEngine behavior must remain intact.

## Review Focus

- Touchpad fractional scroll, HiDPI letterboxing and rotation must map correctly.
- Device removal and unsupported acceleration must report actionable errors.
- Cancellation while writing files/recording must not strand session shutdown.
- Localized labels and remote settings must agree without partial mutation.
- Captures and received files must not overwrite or escape their destinations.

### Task 1: Shared settings and protocol

Files: new `VisionEngine/LinuxVisionSettings.cs`, existing host/server and tests.

Interfaces: `LinuxVisionSettings` immutable record: Profile, Bitrate, MaxSize, Fps, Acceleration (Automatic/Software/Forced), HardwareBackend, MonitorId (uint), Fullscreen, AudioDeviceId (uint; 0=default), AudioEnabled, RemoteInput, LowLatency, VSync, RecordingDirectory, ReceiveDirectory. Static `Default`, `ForProfile(string)`, instance `Validate()`; `LinuxVisionSettingsStore.Load()/Save(settings)`.

- [x] Add regression checks for translated profile IDs, atomic validation, preset values, persistence and stale revision.
- [x] Run failures before implementation, then implement store/configuration and host propagation.
- [x] Run Linux suite and inspect diff.

### Task 2: SDL outputs and Android input

Files: renderer/audio; new `LinuxVisionInput.cs`, `LinuxSdlDevices.cs`; Android accessibility only as needed; separate `tests/VisionInputChecks.cs`.

Interfaces: renderer optional constructor `LinuxSdlVideoRenderer(LinuxVisionSettings? settings=null)`; events `Action<byte[]>? Input`, `Action? Closed`; method `UpdateSession(VideoSession)`; device enumeration `LinuxSdlDevices.Monitors()/AudioOutputs()` returns records `LinuxOutputDevice(uint Id,string Name)`. Audio player optional `LinuxSdlAudioPlayer(uint deviceId=0)`.

- [x] Add wire/geometry/Unicode/scroll/focus tests. Run RED, implement input serializer and SDL events including touchpad wheel.
- [x] Add output selection and close cancellation; ensure no input outside image or after focus loss.
- [x] Extend Android cursor navigation and modifiers using accessibility APIs; report unsupported global key injection honestly.
- [x] Verify Linux tests and Android compilation if SDK is available.

### Task 3: FILES V2

Files: new Linux `Files/` self-contained protocol/engine/storage; LAN session/protocol; separate tests.

Interfaces: `LinuxVisionFiles` exposes `Generation`, `IsConnected`, `Status`, `event Action? Changed`, `AttachAsync(Stream,long,CancellationToken)`, `SendAsync(string,CancellationToken)`, `Acquire(string)` and `CancelLease(string)` returning serialized grant, `DisposeAsync()`. Constructor `(string receiveDirectory)`; no dependencies on WPF code or linked files outside Linux. LAN streams gain optional Files stream and session Prepare accepts optional generation.

- [x] Test interoperability against existing wire constants, duplex transfers, lease generation, SHA, traversal/symlinks, cancellation and no overwrite.
- [x] Implement compatible V2 and secure local storage; LAN channel negotiation. Root integrates USB and server routing.
- [x] Run focused checks and report exact hooks.

### Task 4: Decoder acceleration and recording

Files: decoder and new `LinuxVisionAcceleration.cs`, `LinuxVisionRecorder.cs`; separate tests.

Interfaces: decoder optional constructor settings parameter after renderer. `LinuxVisionAcceleration.ProbeAsync(CancellationToken)` yields backend diagnostics; forced mode must validate actual decode. `LinuxVisionRecorder` constructor `(string directory,int fps)`, `Start()`, `WriteVideo(VideoCodec,VideoPacket,byte[]? configuration)`, `WriteAudio(byte[] pcm)`, `StopAsync()`, `IsRecording`, `Status`, `DisposeAsync()`; bounded queues and recoverable partial output. Record from keyframe and segment geometry changes. Root fans session video/audio to recorder.

- [x] Add real software/invalid-force tests; exercise recorder with synthetic H264/PCM and ffprobe; observe RED.
- [x] Implement hardware selection and real probe, MKV recording with timestamps/audio, bounded shutdown, errors and segment finalization.
- [x] Run focused checks; report any hardware-only gaps.

### Task 5: Integrate engines and VE UI

Files: USB/LAN engines, host/server/protocol, `MainWindow.cs`, new partial UI file if needed.

- [x] Feed settings to both transports; one session input writer and response reader, event-triggered cancellation, optional files/audio and recorder disposal.
- [x] Connect profiles, manual overrides, forced acceleration, monitor/audio devices, fullscreen, input, recording, send/cancel and receive-folder controls to real services. Remove placeholder VE controls.
- [x] Sync remote snapshots/settings with host/UI and persist only valid settings; block incompatible recording-time changes.
- [x] Verify input/record/files failures stop or degrade explicitly without leaving resources.

### Task 6: Integrated verification and evidence

- [x] Build Release, full protocol/native suite and new feature checks; `git diff --check` scoped to touched files.
- [x] Review combined tree independently and resolve material findings.
- [x] Inspect actual UI and native output where display is available.
- [x] Check adb devices, GPU and output inventory; record physical cases unavailable as unverified. No phone detected, all hardware decoders unavailable; Android build blocked by missing SDK/workload.
- [x] Update Linux README/design/evidence report. No publication.

## Execution ledger

- User approved design and indispensable Android input changes; requested laptop touchpad verification.
- Ruling: execute in existing Dev_Local because this is the user's explicit working tree with valuable uncommitted changes. No worktree migration or commits.
- Ruling: approval already authorizes implementation; proceed without a second plan approval.

## Evidence ledger — final local verification

- Task 1: settings, stale revision, atomic batch validation and concurrent persistence checks PASS.
- Task 2: pure input/Android keyboard helper checks PASS; actual SDL event injection, no-frame close/focus, fractional touchpad and concrete output selection PASS. Physical touchpad-to-Android unverified.
- Task 3: portable existing NOVORA V2 contract copied into Linux namespace; core transfers/SHA/no-overwrite/path/symlink/generation checks PASS. Added Android USB attachment and Android budget negotiation. APK not rebuilt.
- Task 4: FFprobe/FFmpeg validate MKV video/audio, source timestamps, rotation, resize, video-only, partial recovery and queue bounds. Hardware probes correctly reject unavailable backends and forced mode never falls back.
- Task 5: native socket video/audio→FFmpeg→SDL→recording/keyframe request/shutdown PASS. GUI construction and screenshot PASS. Fixed pre-existing repeated TextBlock parenting that prevented app startup.
- Task 6: build and full Linux suite PASS, 0 build warnings/errors; separate native input/output/runtime/UI modes PASS. Independent reviews fixed cleanup, async progress, batch settings, Android file budgets, transient output IDs, settings transaction and renderer-start cleanup. Android compile NETSDK1139: only SDK8, no Android platform/workload. ADB empty. No physical certification.
- Ruling: the Android USB files path required three additive optional fields in the platform-neutral offer in `src/NOVORA LINK/Control/NLControlProtocol.cs`. No Windows runtime behavior was changed.
- Ruling: preserve keys and modifier semantics available via Android accessibility, documenting OS-wide raw injection limits instead of pretending unrestricted support.
- No commit, push, APK install or dependency installation.
