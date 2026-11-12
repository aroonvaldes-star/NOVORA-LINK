namespace NOVORA.VisionEngine.Video;

public sealed record VEVideoLayout(
    int Width,
    int Height,
    int NativePixelFormat,
    IntPtr Plane0,
    IntPtr Plane1,
    IntPtr Plane2,
    int Pitch0,
    int Pitch1,
    int Pitch2);
