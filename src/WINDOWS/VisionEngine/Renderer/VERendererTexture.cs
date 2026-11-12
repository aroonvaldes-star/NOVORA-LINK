using NOVORA.VisionEngine.Video;

namespace NOVORA.VisionEngine.Renderer;

public sealed class VERendererTexture : IDisposable
{
    private readonly VERendererDevice _deviceVE;
    private IntPtr _textureVE;
    private int _widthVE;
    private int _heightVE;
    private VEVideoPixelFormat _formatVE = VEVideoPixelFormat.Unknown;
    private bool _disposedVE;

    public VERendererTexture(VERendererDevice device)
    {
        _deviceVE = device ?? throw new ArgumentNullException(nameof(device));
    }

    public int WidthVE => _widthVE;
    public int HeightVE => _heightVE;
    public VEVideoPixelFormat FormatVE => _formatVE;
    public IntPtr TextureVE => _textureVE;

    public void UploadVE(VEVideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposedVE, this);
        ArgumentNullException.ThrowIfNull(frame);
        frame.ValidateForRendererVE();

        EnsureTextureVE(frame);

        switch (frame.PixelFormat)
        {
            case VEVideoPixelFormat.Yuv420P:
                _deviceVE.UpdateYuvVE(
                    _textureVE,
                    frame.Plane0,
                    frame.Pitch0,
                    frame.Plane1,
                    frame.Pitch1,
                    frame.Plane2,
                    frame.Pitch2);
                break;

            case VEVideoPixelFormat.Nv12:
            case VEVideoPixelFormat.Nv21:
                _deviceVE.UpdateNvVE(
                    _textureVE,
                    frame.Plane0,
                    frame.Pitch0,
                    frame.Plane1,
                    frame.Pitch1);
                break;

            default:
                throw new NotSupportedException($"Pixel format FFmpeg {frame.NativePixelFormat} no soportado por RendererVE.");
        }
    }

    private void EnsureTextureVE(VEVideoFrame frame)
    {
        if (_textureVE != IntPtr.Zero &&
            _widthVE == frame.Width &&
            _heightVE == frame.Height &&
            _formatVE == frame.PixelFormat)
            return;

        _deviceVE.DestroyTextureVE(ref _textureVE);

        uint sdlFormat = frame.PixelFormat switch
        {
            VEVideoPixelFormat.Yuv420P => VERendererDevice.PixelFormatIyuvVE,
            VEVideoPixelFormat.Nv12 => VERendererDevice.PixelFormatNv12VE,
            VEVideoPixelFormat.Nv21 => VERendererDevice.PixelFormatNv21VE,
            _ => throw new NotSupportedException($"Pixel format FFmpeg {frame.NativePixelFormat} no soportado.")
        };

        _textureVE = _deviceVE.CreateTextureVE(sdlFormat, frame.Width, frame.Height);
        _widthVE = frame.Width;
        _heightVE = frame.Height;
        _formatVE = frame.PixelFormat;
    }

    public void Dispose()
    {
        if (_disposedVE)
            return;
        _deviceVE.DestroyTextureVE(ref _textureVE);
        _disposedVE = true;
    }
}
