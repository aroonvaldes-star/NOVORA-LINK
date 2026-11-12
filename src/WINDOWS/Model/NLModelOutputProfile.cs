namespace NOVORA.Model;

public sealed record NLModelOutputProfile(
    int Width,
    int Height,
    double SourceRefreshRateHz,
    int TargetFps,
    string Bitrate,
    int MaxSize)
{
    public string Summary =>
        $"{Width}x{Height} @ {TargetFps} FPS · {Bitrate}";
}