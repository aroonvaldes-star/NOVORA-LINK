namespace NOVORA.Models;
public sealed record OutputProfile(int Width, int Height, double SourceRefreshRateHz, int TargetFps, string Bitrate, int MaxDimension)
{
    public string Summary => $"{Width}x{Height} @ {TargetFps} FPS · {Bitrate}";
}
