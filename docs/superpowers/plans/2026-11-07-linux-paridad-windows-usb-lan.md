# NOVORA-LINUX Windows parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Reproduce the Windows interface in Linux and connect its USB/LAN actions to real Linux services.
**Architecture:** Keep Avalonia and existing Linux runtimes. Translate Windows theme, templates and page composition; introduce explicit transport selection shared by status and capture actions.
**Tech Stack:** C#/.NET 8, Avalonia 11, Rust, FFmpeg/SDL3, existing ADB/TLS protocols.
**Spec:** `docs/superpowers/specs/2026-11-07-linux-paridad-windows-usb-lan-design.md`

## Global Constraints

- Windows source is the visual reference; do not modify Windows or substitute generated mockups.
- Preserve USB/LAN authorization, AppControl capture, input, audio, recording, FILES V2 and owned-route cleanup.
- Preserve original fonts/assets and third-party notices.
- No APK installation, firewall changes, deployment, commit or push.
- Keep concurrent user/Claude changes. Recheck files before modifying them.

## Review Focus

- Both transports authorized: capture and status must use the explicitly selected route.
- Selection while capture/link is starting: reject a switch without disrupting the existing session.
- USB waiting while LAN is authorized: never label USB as authenticated from a global boolean.
- Minimum size and Dark theme: preserve wrapping, contrast and reachable actions.
- Published package: include the executable relay and its license.

### Task 1: Windows visual resources and shell

**Files:** `LinuxTheme.cs`, new `WindowsVisualStyles.axaml`, `App.cs`, `MainWindow.cs`, `tests/WindowsParityChecks.cs`.
**Interfaces:** Theme resources consumed by all page builders and Avalonia styles; keep existing `CreateBrush` callers compatible.
- [x] Capture current UI and add native UI checks for Windows Home composition and route-menu availability; observe failure.
- [x] Port resource palette, gradients, button/control templates, shell and Home from Windows source.
- [x] Verify native UI construction/rendering and Light/Dark layouts.

### Task 2: USB/LAN integration

**Files:** new `Control/LinuxConnectionSelection.cs`, `Control/LinuxControlHost.cs`, new `MainWindow.Connection.cs`, `MainWindow.cs`, `MainWindow.Vision.cs`, new `tests/ConnectionSelectionChecks.cs`.
**Interfaces:** host exposes separate USB/LAN authorization, selected route, and route-coherent Vision access; Home uses the host's actual capture request.
- [x] Add selection tests for both/one/no authorized routes and existing-session priority; run red.
- [x] Implement selection and busy-switch protection; wire USB/LAN menu, real capture, device status and guide.
- [x] Run selection/protocol tests; verify waiting USB does not inherit LAN authorization.

### Task 3: Remaining Windows page composition

**Files:** `MainWindow.Vision.cs`, `MainWindow.cs`, new focused partial files as useful.
**Interfaces:** preserve all existing functional controls and callbacks while moving them into Windows layout.
- [ ] Certify exact visual equality for all pages against executed Windows; source-based layouts are applied and unavailable capabilities remain explicit.
- [x] Render every page at 1180×800 and minimum size in both themes; inspect captures.
- [x] Run native UI and affected runtime checks.

### Task 4: Package and final verification

**Files:** `NOVORA.Linux.csproj`, tests and verification report.
**Interfaces:** publish exposes `Tools/LinkEngine/novora-linkengine-relay` at runtime's existing path.
- [x] Verify existing publish omits relay (already reproduced in audit).
- [x] Include relay as publish content; verify published executable and license.
- [x] Run Release build, full Linux suite, Rust tests, input/output/runtime native modes and UI checks.
- [x] Independent final review and bounded fixes; report evidence and Windows/hardware gaps truthfully.

## Execution ledger

- Design approved by user: «Va»; subsequent «date» directs execution.
- Ruling: execute in the shared Dev_Local workspace to preserve the user's working copy and cooperation with Claude; use focused files and inspect concurrent diffs, no commits.
- Ruling: native inline implementation; no additional planning approval because the design and execution are already authorized.
- Preflight: Tasks 1/3 share theme/layout helpers; Task 2 supplies state to both. Implement resource primitives first, selection next, final page integration last.
- Task 1: local implementation complete; native UI verifies Home/menu, all pages, Light/Dark and both window sizes. Captures in `artifacts/reviews/linux-parity-2026-11-07`.
- Task 2: implementation complete; real loopback TLS tests verify route selection, consent cancellation, independent authorization and LAN stop. UI regressions reproduce then verify cleanup failures, active serial and pending USB labels.
- Task 3: source-based port applied. Ruling: consume concurrent `MainWindow.ExIn.cs`/`ExInStatusText` instead of replacing Claude's ExIn work. Exact WPF screenshot comparison remains unverified.
- Task 4: Release build and Linux suite pass; 29 Rust tests pass; native input/output/runtime/UI modes pass. Published relay executes and authenticates DATA. Tests have one CA1416 warning in concurrent ExInChecks; application builds without errors.
- Final review: three material findings received; all reproduced and fixed, with native UI regressions passing. No merge/commit/push; preserve shared workspace.
- Remaining acceptance: actual Android USB/LAN hardware and equivalent Windows screenshots; no claim of certified 100% visual identity.
