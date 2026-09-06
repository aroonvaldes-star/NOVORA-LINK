# VisionEngine Block A Design

VisionEngine Block A is a headless Windows-side pipeline built on the clean NOVORA-LINK 1.4 base. It reuses the bundled scrcpy 4.1 Android server while replacing the scrcpy Windows client path with NOVORA-owned C# components for device validation, server launch, ADB tunneling, protocol parsing, packet merging and FFmpeg decode.

The naming rule is `<Function><Folder><Engine>.cs`, with `VE` for VisionEngine and `LE` for LinkEngine. Renderer code is explicitly excluded. Successful Block A operation means Android video packets arrive and FFmpeg produces decoded frames while `RendererEnabled` remains false.

The runtime sequence is Device -> Transport preparation -> Android server -> transport connection -> demux -> decoder. `adb reverse` is preferred and `adb forward` is the fallback. Only video is enabled; audio and control remain disabled until later blocks.
