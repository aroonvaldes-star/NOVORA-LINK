using NOVORA.VisionEngine.Video;

namespace NOVORA.VisionEngine.Renderer;

public sealed class TextureRendererVE : IDisposable
{
    private readonly DeviceRendererVE _deviceVE;
    private IntPtr _textureVE;
    private int _widthVE;
    private int _heightVE;
    private PixelFormatVideoVE _formatVE = PixelFormatVideoVE.Unknown;
    private bool _disposedVE;

    public TextureRendererVE(DeviceRendererVE device)
    {
        _deviceVE = device ?? throw new ArgumentNullException(nameof(device));
    }

    public int WidthVE => _widthVE;
    public int HeightVE => _heightVE;
    public PixelFormatVideoVE FormatVE => _formatVE;
    public IntPtr TextureVE => _textureVE;

    public void UploadVE(FrameVideoVE frame)
    {
        ObjectDisposedException.ThrowIf(_disposedVE, this);
        ArgumentNullException.ThrowIfNull(frame);
        frame.ValidateForRendererVE();

        EnsureTextureVE(frame);

        switch (frame.PixelFormat)
        {
            case PixelFormatVideoVE.Yuv420P:
                _deviceVE.UpdateYuvVE(
                    _textureVE,
                    frame.Plane0,
                    frame.Pitch0,
                    frame.Plane1,
                    frame.Pitch1,
                    frame.Plane2,
                    frame.Pitch2);
                break;

            case PixelFormatVideoVE.Nv12:
            case PixelFormatVideoVE.Nv21:
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

    private void EnsureTextureVE(FrameVideoVE frame)
    {
        if (_textureVE != IntPtr.Zero &&
            _widthVE == frame.Width &&
            _heightVE == frame.Height &&
            _formatVE == frame.PixelFormat)
            return;

        _deviceVE.DestroyTextureVE(ref _textureVE);

        uint sdlFormat = frame.PixelFormat switch
        {
            PixelFormatVideoVE.Yuv420P => DeviceRendererVE.PixelFormatIyuvVE,
            PixelFormatVideoVE.Nv12 => DeviceRendererVE.PixelFormatNv12VE,
            PixelFormatVideoVE.Nv21 => DeviceRendererVE.PixelFormatNv21VE,
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
