using NOVORA.VisionEngine.Video;
using System.Diagnostics;

namespace NOVORA.VisionEngine.Renderer;

public sealed class VERendererFrame : IDisposable
{
    private readonly VERendererDevice _deviceVE;
    private readonly VERendererTexture _textureVE;
    private bool _disposedVE;

    public VERendererFrame(VERendererDevice device)
    {
        _deviceVE = device ?? throw new ArgumentNullException(nameof(device));
        _textureVE = new VERendererTexture(device);
    }

    public (double RenderLatencyMs, int Width, int Height, VEVideoPixelFormat Format) PresentVE(
        VEVideoFrame frame,
        int rotationDegrees)
    {
        ObjectDisposedException.ThrowIf(_disposedVE, this);
        ArgumentNullException.ThrowIfNull(frame);

        Stopwatch timer = Stopwatch.StartNew();
        _textureVE.UploadVE(frame);

        (int outputWidth, int outputHeight) = _deviceVE.GetOutputSizeVE();
        VERendererRect viewport = VERendererViewport.CalculateVE(
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
