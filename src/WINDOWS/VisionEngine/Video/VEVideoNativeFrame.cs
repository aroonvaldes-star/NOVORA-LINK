using System.Runtime.InteropServices;

namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Lee únicamente el prefijo público y estable de AVFrame que necesita D.
/// FFmpeg 8/libavutil 60: data[8], linesize[8], extended_data,
/// width, height, nb_samples y format.
/// </summary>
public static class VEVideoNativeFrame
{
    private const int DataOffsetVE = 0;
    private const int LinesizeOffsetVE = 64;
    private const int WidthOffsetVE = 104;
    private const int HeightOffsetVE = 108;
    private const int FormatOffsetVE = 116;

    public static VEVideoLayout ReadVE(IntPtr frame)
    {
        if (frame == IntPtr.Zero)
            throw new ArgumentException("AVFrame nulo.", nameof(frame));

        if (IntPtr.Size != 8)
            throw new PlatformNotSupportedException(
                "VisionEngine Block D requiere proceso Windows x64.");

        IntPtr plane0 = Marshal.ReadIntPtr(frame, DataOffsetVE + 0 * IntPtr.Size);
        IntPtr plane1 = Marshal.ReadIntPtr(frame, DataOffsetVE + 1 * IntPtr.Size);
        IntPtr plane2 = Marshal.ReadIntPtr(frame, DataOffsetVE + 2 * IntPtr.Size);

        int pitch0 = Marshal.ReadInt32(frame, LinesizeOffsetVE + 0 * sizeof(int));
        int pitch1 = Marshal.ReadInt32(frame, LinesizeOffsetVE + 1 * sizeof(int));
        int pitch2 = Marshal.ReadInt32(frame, LinesizeOffsetVE + 2 * sizeof(int));

        int width = Marshal.ReadInt32(frame, WidthOffsetVE);
        int height = Marshal.ReadInt32(frame, HeightOffsetVE);
        int format = Marshal.ReadInt32(frame, FormatOffsetVE);

        if (width <= 0 || height <= 0)
            throw new InvalidDataException($"AVFrame inválido: {width}x{height}.");

        return new VEVideoLayout(
            width,
            height,
            format,
            plane0,
            plane1,
            plane2,
            pitch0,
            pitch1,
            pitch2);
    }
}
