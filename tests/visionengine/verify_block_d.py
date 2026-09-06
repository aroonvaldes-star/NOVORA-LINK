from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
VE = ROOT / "src" / "NOVORA" / "VisionEngine"
RENDERER = VE / "Renderer"

required = [
    RENDERER / "StatesRendererVE.cs",
    RENDERER / "StatusRendererVE.cs",
    RENDERER / "MetricsRendererVE.cs",
    RENDERER / "QueueRendererVE.cs",
    RENDERER / "RectRendererVE.cs",
    RENDERER / "ScalingRendererVE.cs",
    RENDERER / "RotationRendererVE.cs",
    RENDERER / "HostRendererVE.cs",
    RENDERER / "DeviceRendererVE.cs",
    RENDERER / "SurfaceRendererVE.cs",
    RENDERER / "TextureRendererVE.cs",
    RENDERER / "FrameRendererVE.cs",
    RENDERER / "ViewportRendererVE.cs",
    RENDERER / "ManagerRendererVE.cs",
    VE / "Video" / "PixelFormatVideoVE.cs",
    VE / "Video" / "LayoutVideoVE.cs",
    VE / "Video" / "NativeFrameVideoVE.cs",
    ROOT / "src" / "NOVORA" / "MainWindow.VisionEngineVE.cs",
]

missing = [str(p.relative_to(ROOT)) for p in required if not p.exists()]
if missing:
    print("BLOCK D STATIC CONTRACT: FAIL")
    print(" Missing files:")
    for item in missing:
        print("  -", item)
    sys.exit(1)

all_cs = "\n".join(p.read_text(encoding="utf-8-sig", errors="ignore") for p in required)
checks = {
    "direct3d11 backend": "direct3d11" in all_cs.lower(),
    "queue capacity 2": bool(re.search(r"capacity[^\n]*2|CapacityVE\s*=\s*2", all_cs, re.I)),
    "YUV upload": "SDL_UpdateYUVTexture" in all_cs,
    "NV upload": "SDL_UpdateNVTexture" in all_cs,
    "HWND wrapping": "SDL.window.create.win32.hwnd" in all_cs,
    "dispatcher rendering": "Dispatcher" in all_cs,
}

video = (VE / "Video" / "DecoderVideoVE.cs").read_text(encoding="utf-8-sig", errors="ignore")
checks["av_frame_clone"] = "av_frame_clone" in video

main_cs = (ROOT / "src" / "NOVORA" / "MainWindow.xaml.cs").read_text(encoding="utf-8-sig", errors="ignore")
main_ve = (ROOT / "src" / "NOVORA" / "MainWindow.VisionEngineVE.cs").read_text(encoding="utf-8-sig", errors="ignore")
checks["Play uses VisionEngine"] = "ToggleVisionEngineVEAsync" in main_cs and "EngineCoreVE" in main_ve
checks["Play no StartOptimized"] = "_scrcpy.StartOptimized" not in main_cs
checks["Play no ScrcpyService field"] = "private readonly ScrcpyService _scrcpy" not in main_cs
checks["renderer owns AVFrame"] = "IDisposable" in (VE / "Video" / "FrameVideoVE.cs").read_text(encoding="utf-8-sig", errors="ignore")

renderer_text = "\n".join(
    p.read_text(encoding="utf-8-sig", errors="ignore")
    for p in RENDERER.glob("*.cs")
)
for forbidden in ("WriteableBitmap", "ffmpeg.exe", "scrcpy.exe"):
    checks[f"no {forbidden}"] = forbidden not in renderer_text

failed = [name for name, ok in checks.items() if not ok]
if failed:
    print("BLOCK D STATIC CONTRACT: FAIL")
    for name in failed:
        print("  -", name)
    sys.exit(1)

print("BLOCK D STATIC CONTRACT: PASS")
for name in checks:
    print("  +", name)
