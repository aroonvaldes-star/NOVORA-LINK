# VisionEngine Block A Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the complete headless VisionEngine Block A pipeline without presenting an image.

**Architecture:** Reuse the bundled scrcpy 4.1 Android server, but replace its Windows client execution path with NOVORA C# components. ADB reverse/forward carries the video socket, the scrcpy 4.1 media protocol is parsed directly, H.26x config is merged, and bundled FFmpeg validates decode output without renderer access.

**Tech Stack:** .NET 8, C# 12, ADB, scrcpy-server 4.1, FFmpeg avcodec 62/avutil 60/swresample 6.

**Spec:** `docs/superpowers/specs/2026-09-04-visionengine-block-a-design.md`

## Global Constraints

- Naming: `<Function><Folder><Motor>.cs`.
- VisionEngine suffix: `VE`.
- LinkEngine suffix: `LE`.
- No renderer, Direct3D, WPF Image, BitmapSource or WriteableBitmap in Block A.
- Do not execute `scrcpy.exe` from VisionEngine Block A.
- Preserve scrcpy attribution and Apache-2.0 notices.

---

### Task 1: Protocol contracts
- [x] Add codec/session/media protocol reader.
- [x] Verify codec IDs, 12-byte headers and flags with synthetic fixtures.

### Task 2: Device and server
- [x] Reuse `AdbService` for device validation.
- [x] Deploy bundled `scrcpy-server` and launch `com.genymobile.scrcpy.Server 4.1` with Block A options.

### Task 3: Transport
- [x] Implement `adb reverse` listener-first transport.
- [x] Implement `adb forward` fallback and forward dummy-byte handling.
- [x] Read the 64-byte device name field.

### Task 4: Headless video
- [x] Implement codec/session demux.
- [x] Implement H.264/H.265 config merger.
- [x] Load bundled FFmpeg DLLs by absolute path.
- [x] Decode via `avcodec_send_packet` / `avcodec_receive_frame`.
- [x] Discard AVFrames after counting/metadata validation.

### Task 5: Core runtime and verification
- [x] Wire Device -> Server -> Transport -> Video under `EngineCoreVE`.
- [x] Add xUnit protocol/merger/headless tests.
- [x] Add Windows headless harness.
- [x] Add naming/headless static validation.
