using NOVORA.VisionEngine.Video;
using System.Diagnostics;

namespace NOVORA.VisionEngine.Renderer;

public sealed class FrameRendererVE : IDisposable
{
    private readonly DeviceRendererVE _deviceVE;
    private readonly TextureRendererVE _textureVE;
    private bool _disposedVE;

    public FrameRendererVE(DeviceRendererVE device)
    {
        _deviceVE = device ?? throw new ArgumentNullException(nameof(device));
        _textureVE = new TextureRendererVE(device);
    }

    public (double RenderLatencyMs, int Width, int Height, PixelFormatVideoVE Format) PresentVE(
        FrameVideoVE frame,
        int rotationDegrees)
    {
        ObjectDisposedException.ThrowIf(_disposedVE, this);
        ArgumentNullException.ThrowIfNull(frame);

        Stopwatch timer = Stopwatch.StartNew();
        _textureVE.UploadVE(frame);

        (int outputWidth, int outputHeight) = _deviceVE.GetOutputSizeVE();
        RectRendererVE viewport = ViewportRendererVE.CalculateVE(
            frame.Width,
            frame.Height,
            outputWidth,
            outputHeight,
            rotationDegrees);

        _deviceVE.PresentVE(_textureVE.TextureVE, viewport, rotationDegrees);
        timer.Stop();

        return (timer.Elapsed.TotalMilliseconds, frame.Width, frame.Height, frame.PixelFormat);
    }

    public void Dispose()
    {
        if (_disposedVE)
            return;
        _textureVE.Dispose();
        _disposedVE = true;
    }
}
