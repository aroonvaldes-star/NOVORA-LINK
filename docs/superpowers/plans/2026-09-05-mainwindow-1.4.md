# NOVORA-LINK 1.4 MainWindow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the approved NOVORA-LINK 1.4 MainWindow as a functional WPF surface connected to LinkEngine, VisionEngine and Android metrics.

**Architecture:** MainWindow remains the composition root. Existing event handlers and ViewModel bindings are preserved. Presentation-only metric formatting lives in `MainWindow.Interface14.cs`; VisionEngine dynamically creates its renderer host only when streaming.

**Tech Stack:** .NET 8, WPF, WindowsFormsHost, VisionEngine, SDL3/Direct3D11, ADB.

**Spec:** `docs/superpowers/specs/2026-09-05-mainwindow-1.4-design.md`

## Global Constraints

- Do not add shadows.
- Main matte background is `#ECEFF2`.
- NOVORA blue is `#00AEEF`.
- Title bar shows only Settings, Minimize and Close.
- MainWindow lower boundary is the LinkEngine and VisionEngine buttons.
- Play remains VisionEngine, not scrcpy.

---

### Task 1: MainWindow visual contract

**Files:**
- Modify: `src/NOVORA/MainWindow.xaml`
- Test: `tests/ui/verify_mainwindow_14.py`

**Interfaces:**
- Consumes: existing MainViewModel properties and existing MainWindow click/selection handlers.
- Produces: named controls used by MainWindow code-behind and renderer integration.

- [x] Write the static UI contract test.
- [x] Run it against the old MainWindow and verify it fails.
- [x] Replace MainWindow XAML with the approved matte NOVORA design.
- [x] Verify XML, event handlers and named-control contract.

### Task 2: Performance metrics surface

**Files:**
- Create: `src/NOVORA/MainWindow.Interface14.cs`
- Modify: `src/NOVORA/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `DeviceMetrics`.
- Produces: `SetPerformanceReading14()`, `ResetPerformanceSurface14(string)`, `ApplyPerformanceSurface14(DeviceMetrics)`.

- [x] Add named CPU, RAM, battery and temperature controls to XAML.
- [x] Add presentation helpers without changing `DeviceMetricsService`.
- [x] Wire existing one-shot refresh to the new surface.

### Task 3: Dynamic VisionEngine renderer

**Files:**
- Modify: `src/NOVORA/MainWindow.VisionEngineVE.cs`
- Modify: `src/NOVORA/VisionEngine/Renderer/HostRendererVE.cs`

**Interfaces:**
- Consumes: `EngineCoreVE.AttachRendererHostVE(HostRendererVE)`.
- Produces: dynamic center-card renderer lifecycle.

- [x] Create `HostRendererVE` only when Start is requested.
- [x] Replace the center settings panel with the live renderer while running.
- [x] Restore settings on Stop/failure.
- [x] Set the native renderer background to black.

### Task 4: 1.4 identity and package verification

**Files:**
- Modify: `src/NOVORA/NOVORA.csproj`
- Create: `scripts/Verify-MainWindow-1.4.ps1`

**Interfaces:**
- Produces: version `1.4.0` and repeatable static/build verification on Windows.

- [x] Set project version to `1.4.0`.
- [x] Verify XAML contract and source wiring statically.
- [ ] Run `dotnet build` on the user's Windows machine because this environment does not contain the .NET SDK.
