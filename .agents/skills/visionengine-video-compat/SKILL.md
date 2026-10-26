---
name: visionengine-video-compat
description: Design, implement, or review Windows video decoding in NOVORA-LINK VisionEngine across NVIDIA, Intel, and AMD GPUs. Use for decoder selection, hardware acceleration, fallback, and performance validation; not for unrelated AI workloads.
---

# VisionEngine video compatibility

Use this skill as development guidance. Loading it does not add a video backend to NOVORA-LINK.

## Current seam

- `src/NOVORA/VisionEngine/Video/ManagerVideoVE.cs` creates `DecoderVideoVE`; that decoder currently selects FFmpeg software decoders.
- `FrameVideoVE` and `TextureRendererVE` expect CPU-addressable YUV420P, NV12, or NV21 planes. A GPU-resident frame cannot be passed to them as though its pointers were CPU memory.
- `src/NOVORA/VisionEngine/Nvidia/ManagerNvidiaVE.cs` detects some NVIDIA APIs and computes a profile. Its `UseNvdec` policy value is not evidence that the active video decoder uses NVDEC.

## Backend choice

Prefer a Windows D3D11VA hardware decode path as the first shared candidate. Test codec, profile, bit depth, resolution, adapter, and driver at runtime; a vendor name alone is insufficient. Select it only after successful decoder initialization. Preserve the current FFmpeg software path when probing, initialization, frame transfer, or decoding fails. Keep vendor-specific NVDEC, Intel VPL, and AMD AMF paths optional and consider them only when measurements justify their maintenance cost.

Treat the renderer handoff as part of the feature. Either transfer hardware frames into a supported CPU pixel format with measured overhead, or implement a GPU texture handoff with explicit device ownership and synchronization. Never label the pipeline zero-copy unless that handoff is verified.

Report the backend that actually decoded frames, separately from detected hardware capability. Do not display NVDEC, VPL, AMF, or D3D11VA as active based only on DLL presence, GPU discovery, or a configured profile.

## Evidence to collect

Before reporting compatibility or a speedup, compile NOVORA-LINK and exercise a real video session on each claimed GPU family and a software-only fallback. Check unsupported codec/profile and decoder failure recovery. Measure frame time, dropped frames, CPU load, GPU video-engine load, and end-to-end latency against the same software baseline. If a GPU family is unavailable, state that it is untested.

## Primary references

- Shared Windows path: https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nn-d3d11-id3d11videodevice and https://ffmpeg.org/doxygen/trunk/hwcontext__d3d11va_8c.html
- NVIDIA: https://developer.nvidia.com/video-codec-sdk
- Intel: https://intel.github.io/libvpl/latest/index.html
- AMD: https://gpuopen.com/advanced-media-framework/