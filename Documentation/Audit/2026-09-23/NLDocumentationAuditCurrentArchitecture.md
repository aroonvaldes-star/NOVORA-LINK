# NOVORA-LINK Current Architecture Pack

Baseline: `7329c1bdaebbfe51fe6bbe18119328c4d26d4912` plus the preserved dirty working tree captured by `NLToolAuditBaseline.ps1`.

## 1. Project graph

```text
NOVORA.sln
|-- src/NOVORA/NLProjectDesktop.csproj
|   |-- WPF application and composition root
|   |-- VisionEngine, LinkEngine, STEngine and ExInEngine in one assembly
|   `-- Native RelayCore launched as LENetworkRelay.exe
|-- tests/NOVORA.Tests/NLProjectTests.csproj -> Desktop
|-- tests/NOVORA.VisionEngine.BlockCHarness -> Desktop
|-- tests/NOVORA.VisionEngine.HeadlessHarness -> Desktop
|-- tests/NOVORA.VisionEngine.ProfileHarness -> Desktop
`-- src/NOVORA.Android/NLProjectAndroid.csproj
    `-- Separate Android application; protocol compatibility is source-level

src/NOVORA/LinkEngine/Network/Native/RelayCore/Cargo.toml
`-- Rust native relay process/library outside the .NET project graph
```

The engine folders are ownership hints, not binary independence. The Desktop project compiles all four engines into `NOVORA.dll`.

## 2. Ownership graph

```text
NLUIWindowMain (composition and UI orchestration)
|-- VECoreEngine -> VECoreRuntime -> Device/Server/Transport/Video/Audio/Control
|-- LERuntimeManager -> LECoreEngine -> Transport/Network/Relay/Recovery/Metrics
|-- ExInCoreEngine -> ExInManager -> SDL/calibration/UHID
`-- STCoreEngine -> concrete VECoreRuntime + LERuntimeManager + ExInStatus

Cross-owner edges requiring ADR:
ExInControlSession -> VE Control/Device/Server/Transport
VEControlManager -> ExInEngine
STCoreEngine -> concrete VE/LE/ExIn types
```

Each VE and LE recovery path is locally owned. MainWindow owns composition and status-event subscriptions. STEngine is refreshed by VE/LE/UI events rather than a new timer.

## 3. Runtime graph

```text
Android phone
|-- ADB USB/TCP
|   |-- VE: push scrcpy-server -> app_process -> video/audio/control sockets
|   |-- LE CONTROL tcp:27183
|   |-- LE DATA tcp:27184 -> LENetworkRelay.exe / RelayCore
|   `-- Android remote control bootstrap/tunnels
|
Windows NOVORA.exe
|-- WPF UI and four in-process engines
|-- adb.exe child processes
|-- LENetworkRelay.exe child process
|-- FFmpeg shared libraries + SDL3
`-- optional GPU decoder selection
```

The baseline runtime snapshot observed ADB-related processes but no connected Android device and no relevant active TCP endpoint. This does not prove an engine session.

## 4. Android graph

```text
NLProjectAndroid
|-- AndroidUI: Home, Connection, Control, Files, Settings and auxiliary surfaces
|-- AndroidControl: PC command/session protocol
|-- AndroidStorage: trusted PC data in NoBackupFilesDir
|-- Android service/transport paths for remote control and LinkEngine
`-- package com.novora.appcontrol, versionName 1.4.28, versionCode 28
```

The PC and Android projects do not share a compiled Contracts project. Compatibility is maintained through duplicated source-level message and capability expectations. This must be mapped before extracting contracts.

## 5. Storage graph

| Location | Current owner | Content/lifecycle | Audit state |
|---|---|---|---|
| Desktop/NOVORA-Files | Control/VE media | User-visible transfers, captures and recordings | KEEP; verify creator/readers/cleanup per artifact |
| LocalAppData/NOVORA-LINK/ExInProfiles | ExIn | Controller profiles and calibration | KEEP; ExIn-owned |
| LocalAppData/NOVORA | Settings/LE/history/control trust | Multiple subtrees and lifecycles | SPLIT/normalize only after complete inventory |
| Android NoBackupFilesDir/trusted-pcs.bin | Android trust | Trusted PC material, excluded from backup | KEEP; security review required |
| /data/local/tmp/novora-vision-server.jar | VE temporary server | scrcpy-server runtime deployment | TEMPORARY MIGRATION; cleanup and hash gate |
| App/Tools | Shared installation infrastructure | ADB, scrcpy, FFmpeg, SDL and native tools | Neutral owner required; immutable manifest |

## 6. State authority map

| State | Current authority | Known consumers | Risk |
|---|---|---|---|
| Selected Android serial | `NLServiceSettings.SelectedDeviceSerial` plus runtime selections | UI, VE, LE, Android control | Wi-Fi/USB transport identity can be confused without session validation |
| VE lifecycle | `VECoreEngine/VECoreRuntime` | UI, ST, Android control | ST currently receives concrete runtime |
| LE lifecycle | `LERuntimeManager/LECoreEngine` | UI, ST, Android control | Runtime and device metrics are concrete dependencies |
| ExIn lifecycle | `ExInCoreEngine/ExInManager` | UI, VE control, ST | Direct VE/ExIn ownership cycle |
| Global health | `STCoreEngine.LastNovoraSnapshotST` | UI/Android status | Capture currently invokes mutating VE evaluation |
| NVIDIA backend | `NLNVIDIAManager` status | VE/UI | Available, selected and active must remain distinct |

## 7. Process authority map

The baseline found 34 process-related detections. Structured `ArgumentList` is already used in ADB, VE server, update and console paths. Execution remains distributed among `NLServiceADB`, `NLServiceProcess`, `NLServiceConsoleCommand`, VE server/streaming, LinkEngine relay, discovery and UI shell launches. The target is controlled execution with explicit ownership, not eliminating every `Process.Start`.

## 8. Evidence boundary

This architecture pack is based on source inspection, generated inventories, Release build/tests and a single ADB baseline query. It does not establish physical Android behavior, performance, security closure, compatibility or publication readiness.
