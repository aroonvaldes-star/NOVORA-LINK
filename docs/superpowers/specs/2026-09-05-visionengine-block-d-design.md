# VisionEngine Block D Design

## Goal

Complete the first production renderer stage for VisionEngine while leaving the visual placement and styling of the video area in `MainWindow.xaml` to the user.

## Architecture

VisionEngine keeps the Android/server/transport/decode pipeline from Blocks A-C. `DecoderVideoVE` clones each decoded FFmpeg `AVFrame` with `av_frame_clone()` before the decoder reuses its receive frame. The owned frame is passed to `ManagerRendererVE`, which uses a bounded latest-frame queue (capacity 2) so rendering never builds latency behind the phone.

The renderer wraps an existing Win32 child HWND owned by `HostRendererVE`. SDL3 is loaded from NOVORA Tools and creates a renderer explicitly named `direct3d11`. YUV420P frames are uploaded with `SDL_UpdateYUVTexture`; NV12/NV21 use `SDL_UpdateNVTexture`. Presentation uses aspect-fit scaling and `SDL_RenderTexture`/`SDL_RenderTextureRotated`. All SDL video/render calls execute on the WPF UI dispatcher because SDL requires these APIs on the main thread.

## UI Boundary

`MainWindow.xaml` is not modified by Block D. `HostRendererVE` is a code-only reusable control. The user may place it anywhere in their own XAML. MainWindow automatically finds the first `HostRendererVE` in its visual tree and attaches it to VisionEngine. If no host exists, VisionEngine may still start but remains headless and reports that the renderer is waiting for a host.

## Main Action Button

The existing `MainActionButton_Click` stops using `ScrcpyService` as its primary backend. It initializes and starts/stops `EngineCoreVE`. scrcpy binaries remain as legacy/fallback Tools, but VisionEngine does not execute `scrcpy.exe`.

## Frame Ownership

`FrameVideoVE` owns an FFmpeg frame reference. Every queued, dropped, rendered, failed, or shutdown path disposes it exactly once. Renderer shutdown occurs after decode stops but before `DecoderVideoVE` unloads avutil, so `av_frame_free()` remains valid for all outstanding frame references.

## Supported First Renderer Formats

- FFmpeg `AV_PIX_FMT_YUV420P` -> SDL `SDL_PIXELFORMAT_IYUV`
- FFmpeg `AV_PIX_FMT_NV12` -> SDL `SDL_PIXELFORMAT_NV12`
- FFmpeg `AV_PIX_FMT_NV21` -> SDL `SDL_PIXELFORMAT_NV21`

Unsupported decoded pixel formats fail the renderer explicitly instead of silently corrupting output.

## Performance / Recovery

- Queue capacity: 2 frames.
- When full, oldest frames are dropped first.
- When the UI render pass runs, only the newest queued frame is presented; stale queued frames are disposed.
- Renderer metrics track queued, presented, dropped, errors, queue depth, frame age and render latency.
- Renderer failure does not stop LinkEngine and does not mutate ADB lifecycle.

## Global Constraints

- C# engine naming stays `<Function><Folder><Engine>.cs` with suffix `VE`.
- No `scrcpy.exe` invocation from VisionEngine.
- No `ffmpeg.exe` process.
- No `WriteableBitmap` renderer.
- No production helper renderer executable.
- `MainWindow.xaml` video layout/style is user-owned and remains untouched in this block.
