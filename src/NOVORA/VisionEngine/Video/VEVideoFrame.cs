using NOVORA.VisionEngine.Protocol;

namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Frame FFmpeg con ownership explícito. El AVFrame fue clonado con
/// av_frame_clone(), de modo que sus buffers permanecen referenciados hasta
/// Dispose(). No tiene finalizer deliberadamente: debe liberarse antes de
/// descargar libavutil.
/// </summary>
public sealed class VEVideoFrame : IDisposable
{
    private IntPtr _nativeFrameVE;
    private Action<IntPtr>? _releaseVE;
    private int _disposedVE;

    public VEVideoFrame(
        long sequence,
        long? sourcePresentationTimeUs,
        VEProtocolCodec codec,
        int expectedWidth,
        int expectedHeight,
        DateTimeOffset decodedAtUtc,
        IntPtr nativeFrame,
        VEVideoLayout layout,
        Action<IntPtr> release)
    {
        if (nativeFrame == IntPtr.Zero)
            throw new ArgumentException("AVFrame clonado nulo.", nameof(nativeFrame));

        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(release);

        Sequence = sequence;
        SourcePresentationTimeUs = sourcePresentationTimeUs;
        Codec = codec;
        ExpectedWidth = expectedWidth;
        ExpectedHeight = expectedHeight;
        DecodedAtUtc = decodedAtUtc;
        _nativeFrameVE = nativeFrame;
        _releaseVE = release;

        Width = layout.Width;
        Height = layout.Height;
        NativePixelFormat = layout.NativePixelFormat;
        PixelFormat = Enum.IsDefined(typeof(VEVideoPixelFormat), layout.NativePixelFormat)
            ? (VEVideoPixelFormat)layout.NativePixelFormat
            : VEVideoPixelFormat.Unknown;
        Plane0 = layout.Plane0;
        Plane1 = layout.Plane1;
        Plane2 = layout.Plane2;
        Pitch0 = layout.Pitch0;
        Pitch1 = layout.Pitch1;
        Pitch2 = layout.Pitch2;
    }

    public long Sequence { get; }
    public long? SourcePresentationTimeUs { get; }
    public VEProtocolCodec Codec { get; }
    public int ExpectedWidth { get; }
    public int ExpectedHeight { get; }
    public DateTimeOffset DecodedAtUtc { get; }
    public int Width { get; }
    public int Height { get; }
    public int NativePixelFormat { get; }
    public VEVideoPixelFormat PixelFormat { get; }
    public IntPtr Plane0 { get; }
    public IntPtr Plane1 { get; }
    public IntPtr Plane2 { get; }
    public int Pitch0 { get; }
    public int Pitch1 { get; }
    public int Pitch2 { get; }
    public bool IsDisposedVE => Volatile.Read(ref _disposedVE) != 0;

    public void ValidateForRendererVE()
    {
        ObjectDisposedException.ThrowIf(IsDisposedVE, this);

        if (Width <= 0 || Height <= 0)
            throw new InvalidDataException($"Frame VisionEngine inválido: {Width}x{Height}.");

        if (Plane0 == IntPtr.Zero || Pitch0 == 0)
            throw new InvalidDataException("Frame VisionEngine no contiene plano Y válido.");

        switch (PixelFormat)
        {
            case VEVideoPixelFormat.Yuv420P:
                if (Plane1 == IntPtr.Zero || Plane2 == IntPtr.Zero || Pitch1 == 0 || Pitch2 == 0)
                    throw new InvalidDataException("Frame YUV420P incompleto.");
                break;

            case VEVideoPixelFormat.Nv12:
            case VEVideoPixelFormat.Nv21:
                if (Plane1 == IntPtr.Zero || Pitch1 == 0)
                    throw new InvalidDataException("Frame NV12/NV21 incompleto.");
                break;

            default:
                throw new NotSupportedException($"Pixel format FFmpeg {NativePixelFormat} no soportado por Block D.");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposedVE, 1) != 0)
            return;

        IntPtr native = Interlocked.Exchange(ref _nativeFrameVE, IntPtr.Zero);
        Action<IntPtr>? release = Interlocked.Exchange(ref _releaseVE, null);

        if (native != IntPtr.Zero)
            release?.Invoke(native);
    }
}
