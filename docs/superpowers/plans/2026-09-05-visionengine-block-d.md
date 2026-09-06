# VisionEngine Block D Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the final VisionEngine Direct3D11 renderer, connect decoded FFmpeg frames to it, and switch NOVORA's main Play button from ScrcpyService to VisionEngine without changing the user's MainWindow video layout.

**Architecture:** DecoderVideoVE clones reference-counted AVFrames, ManagerRendererVE keeps only the freshest frames in a capacity-2 queue, and SDL3 wraps HostRendererVE's Win32 child HWND with an explicit `direct3d11` renderer. All rendering runs on the WPF Dispatcher; YUV420P/NV12/NV21 are uploaded directly without BGRA conversion.

**Tech Stack:** .NET 8 WPF/WindowsFormsHost, FFmpeg avcodec 62 + avutil 60, SDL3, Direct3D11 backend, ADB/scrcpy-server 4.1 protocol.

**Spec:** `docs/superpowers/specs/2026-09-05-visionengine-block-d-design.md`

## Global Constraints

- Naming: `<Function><Folder><Engine>.cs`, suffix `VE`.
- Do not modify the user's `MainWindow.xaml` video layout.
- Do not execute `scrcpy.exe` or `ffmpeg.exe` from VisionEngine.
- Renderer queue capacity is exactly 2.
- SDL renderer name is explicitly `direct3d11`.
- Supported first-frame formats are YUV420P, NV12 and NV21.
- Every owned AVFrame must be disposed on present/drop/failure/shutdown.

---

### Task 1: Renderer contracts and deterministic policies

**Files:**
- Create: `src/NOVORA/VisionEngine/Renderer/StatesRendererVE.cs`
- Create: `src/NOVORA/VisionEngine/Renderer/StatusRendererVE.cs`
- Create: `src/NOVORA/VisionEngine/Renderer/MetricsRendererVE.cs`
- Create: `src/NOVORA/VisionEngine/Renderer/QueueRendererVE.cs`
- Create: `src/NOVORA/VisionEngine/Renderer/ScalingRendererVE.cs`
- Create: `src/NOVORA/VisionEngine/Renderer/RotationRendererVE.cs`
- Test: `tests/NOVORA.Tests/VisionEngineBlockDTests.cs`

**Interfaces:**
- Produces `QueueRendererVE`, `ScalingRendererVE`, `RotationRendererVE`, `StatusRendererVE` used by ManagerRendererVE.

- [ ] Write tests for capacity-2 latest-frame semantics and aspect-fit scaling.
- [ ] Run static Block D verifier and confirm RED because renderer files do not exist.
- [ ] Implement contracts/policies.
- [ ] Run static verifier for Task 1 expectations.

### Task 2: FFmpeg AVFrame ownership

**Files:**
- Create: `src/NOVORA/VisionEngine/Video/PixelFormatVideoVE.cs`
- Create: `src/NOVORA/VisionEngine/Video/NativeFrameVideoVE.cs`
- Modify: `src/NOVORA/VisionEngine/Video/FrameVideoVE.cs`
- Modify: `src/NOVORA/VisionEngine/Video/DecoderVideoVE.cs`

**Interfaces:**
- Produces `FrameVideoVE` with owned `AVFrame*`, dimensions, format, plane pointers and pitches.

- [ ] Add source tests requiring `av_frame_clone` and owned frame disposal.
- [ ] Load `av_frame_clone` and clone every received frame before unref.
- [ ] Parse the stable AVFrame prefix for avutil 60 on x64.
- [ ] Dispose cloned frames with `av_frame_free`.

### Task 3: SDL3 Direct3D11 surface and texture path

**Files:**
- Create: `src/NOVORA/VisionEngine/Renderer/HostRendererVE.cs`
- Create: `src/NOVORA/VisionEngine/Renderer/DeviceRendererVE.cs`
- Create: `src/NOVORA/VisionEngine/Renderer/SurfaceRendererVE.cs`
- Create: `src/NOVORA/VisionEngine/Renderer/TextureRendererVE.cs`
- Create: `src/NOVORA/VisionEngine/Renderer/FrameRendererVE.cs`
- Create: `src/NOVORA/VisionEngine/Renderer/ViewportRendererVE.cs`

**Interfaces:**
- Consumes `HostRendererVE.HandleVE` and `FrameVideoVE`.
- Produces one presented Direct3D11 frame per render request.

- [ ] Wrap the existing Win32 HWND with SDL_CreateWindowWithProperties.
- [ ] Create SDL renderer explicitly using `direct3d11`.
- [ ] Upload YUV420P/NV12/NV21 via SDL planar update APIs.
- [ ] Render aspect-fit with optional rotation and present.

### Task 4: Renderer manager and video lifecycle

**Files:**
- Create: `src/NOVORA/VisionEngine/Renderer/ManagerRendererVE.cs`
- Modify: `src/NOVORA/VisionEngine/Video/ManagerVideoVE.cs`
- Modify: `src/NOVORA/VisionEngine/Video/StatsVideoVE.cs`
- Modify: `src/NOVORA/VisionEngine/Video/StatusVideoVE.cs`

**Interfaces:**
- `ManagerVideoVE.AttachRendererVE(ManagerRendererVE renderer)`.
- `ManagerRendererVE.QueueFrameVE(FrameVideoVE frame)`.

- [ ] Start renderer before decoded frames are queued.
- [ ] Dispose frames immediately when no renderer accepts them.
- [ ] Stop decode, drain renderer queue, then unload decoder/FFmpeg.
- [ ] Publish RendererEnabled and rendered/dropped stats.

### Task 5: Runtime/Core integration

**Files:**
- Modify: `src/NOVORA/VisionEngine/Core/RuntimeCoreVE.cs`
- Modify: `src/NOVORA/VisionEngine/Core/EngineCoreVE.cs`
- Modify: `src/NOVORA/VisionEngine/Core/StatusCoreVE.cs`

**Interfaces:**
- `RuntimeCoreVE.RendererVE` exposes the renderer.
- `EngineCoreVE.AttachRendererHostVE(HostRendererVE host)` attaches UI surface without making Core depend on XAML layout.

- [ ] Construct renderer and attach it to ManagerVideoVE.
- [ ] Include renderer state in Core status.
- [ ] Preserve independent LinkEngine/ADB lifecycle.

### Task 6: MainWindow Play integration without video-layout changes

**Files:**
- Create: `src/NOVORA/MainWindow.VisionEngineVE.cs`
- Modify: `src/NOVORA/MainWindow.xaml.cs`
- Do not modify: `src/NOVORA/MainWindow.xaml`

**Interfaces:**
- Existing `MainActionButton_Click` starts/stops VisionEngine.
- MainWindow discovers `HostRendererVE` from its visual tree if the user has placed one.

- [ ] Remove Play-button dependency on `_scrcpy.StartOptimized()`.
- [ ] Initialize EngineCoreVE with the shared ADB service.
- [ ] Build server options from OutputProfile/audio settings.
- [ ] Stop/dispose VisionEngine during window shutdown.

### Task 7: Verification/package

**Files:**
- Create: `tests/visionengine/verify_block_d.py`
- Create: `scripts/Test-VisionEngine-BlockD.ps1`
- Create: `VISIONENGINE_BLOCK_D_REPORT.md`

**Interfaces:**
- Produces static, xUnit, Windows build and A56 physical verification gates.

- [ ] Verify no scrcpy.exe, ffmpeg.exe or WriteableBitmap renderer path exists.
- [ ] Verify MainWindow.xaml hash is unchanged from Block C.
- [ ] Verify SDL3 required exports exist in bundled DLL.
- [ ] Package complete Block D source and code listing.
