# NOVORA General Independence Audit - Final Report

Date: 2026-09-23  
Baseline commit: `7329c1bdaebbfe51fe6bbe18119328c4d26d4912`  
Scope: Windows PC, Android source/package metadata, VisionEngine, LinkEngine/RelayCore, ExInEngine, STEngine, shared control/process infrastructure and evidence governance.

## Outcome

The v1.2 plan is implemented to the limit of evidence available on this host. Six of eight confirmed findings are resolved. Two remain intentionally open: the current VisionEngine runtime still depends on `scrcpy-server 4.1`, and RelayCore still inherits future-incompatible `net2 0.2.39` through `mio 0.6`.

No release, commit, push, APK replacement or external publication was performed. The already-installed Android package was inspected and exercised on the authorized USB device.

## Implemented

- Added reproducible baseline discovery, stable detections, hash manifest and verifier.
- Added current architecture maps, eight findings, nine ADRs and one-active-phone policy.
- Made STEngine observation pure and moved it to neutral immutable stability snapshots.
- Removed concrete VE/LE/ExIn imports from STEngine.
- Removed mutual imports between VisionEngine and ExInEngine through `Contracts.Input`; moved the cross-engine physical coordinator to `NOVORA.Integration`.
- Added a validated finite process runner and migrated finite VE Exchange and LE failover ADB launches; classified persistent and shell-owned processes separately.
- Defined NOVORA protocol requirements and explicit scrcpy retirement gates G0-G5.
- Added honest backend and acceleration state: Available, Selected, Active and BenefitMeasured are distinct; NVIDIA remains optional; software fallback remains valid; AMD/Intel stay unavailable until implemented and tested.
- Added a comparable operational benchmark schema with P50/P95/P99, loss/queue fields and evidence level.
- Removed 21 RelayCore source warnings without changing runtime behavior.
- Updated all 15 engine subsets with automated and physical evidence states.
- Confirmed source-level PC controls for minimize/close and organized engine/Android sections; visual and physical interaction remain separate evidence gates.

## Verification

| Layer | Result |
|---|---|
| Audit verifier | PASS; 13 required artifacts, 19 hashed manifest entries |
| Desktop solution | Release build, 0 warnings, 0 errors |
| .NET tests | 406 passed, 0 failed |
| Fault/protocol/session subset | 44 passed, 0 failed |
| Android | Release `-t:Compile`, 0 warnings, 0 errors; this does not produce or install an APK |
| Physical Android identity | Samsung SM-A566E, serial `R5CY3118MEW`; `com.novora.appcontrol` 1.4.28/code 28; installed base APK SHA-256 matches canonical `src/NOVORA/Android/NLAndroidApp.apk` |
| Physical engine coexistence | A dedicated physical regression proved that ExIn remains ready after a full VE start; both Android server processes use distinct SCID-owned JAR paths |
| Single-instance ownership | A second NOVORA launch exits before creating windows or engines; runtime verification kept exactly one process and one owner for USB/control ports |
| LinkEngine USB/VPN | Control `27214`, control/data `27183`/`27184` reverse tunnels and established localhost sessions; `tun0` default VPN present; TCP reachability to `1.1.1.1:443` and `example.com:80` succeeded |
| VisionEngine USB | Remote start accepted; active scrcpy server reported 8 Mbps, 2340 max size and 120 FPS; independent scrcpy reverse channel established |
| ExInEngine physical | Xbox One Elite 2 Controller detected as `045E:028E`; live raw/corrected neutral values observed; no calibration profile applied |
| RelayCore | 34 passed, 0 failed, 1 ignored benchmark; project warnings 0 |
| Architecture checks | ExIn to VE imports 0; VE to ExIn imports 0; ST to concrete engines imports 0 |
| JSON evidence | All audit JSON files parse |
| Diff hygiene | `git diff --check` passed; no conflict markers |

The .NET suite was executed outside the restricted sandbox because Windows TLS tests require the real current-user cryptographic profile. The same production code is used; no security downgrade was retained.

## Evidence Boundaries

- Physical USB, package identity, Android UI, VE start, LE VPN/TCP reachability, engine coexistence and ExIn controller detection were refreshed on one authorized phone. Detailed evidence is in `Physical/NLDocumentationPhysicalValidation.md`.
- ICMP through LinkEngine returned no replies, while TCP reachability succeeded; this run therefore proves usable TCP routing, not universal protocol reachability.
- VE fullscreen/focus on the Windows display was not visually inspected, and no comparable latency/jitter/loss or NVIDIA-versus-software A/B benchmark was produced.
- ExIn neutral telemetry, device identity and control-channel survival across VE startup were verified. No complete stick/button movement matrix was performed, and the insufficient calibration attempt produced no active profile.
- The canonical signed APK was not rebuilt or reinstalled because its SHA-256 exactly matched the installed base APK. Existing Android 1.4.28 metadata was preserved and not republished.
- `scrcpy-server` remains selected and is not eligible for removal. Only G0 and G1 pass; G2-G5 remain pending.
- NVIDIA decoder activity is asserted only after decoded frames. No new physical A/B benefit claim was produced.
- RelayCore `mio/net2` modernization remains a separate migration because it changes the network stack and needs runtime parity evidence.

## Release Decision

Automated source/build/test gates pass, and the controlled one-phone USB matrix now proves package identity, VE/LE/ST coexistence, LE TCP routing and ExIn device detection. A final release claim remains NOT READY because Windows VE fullscreen/focus, physical input movement, comparable latency/jitter/loss and NVIDIA A/B evidence are still missing; scrcpy Runtime Dependency is nonzero; and RelayCore future compatibility remains open.
