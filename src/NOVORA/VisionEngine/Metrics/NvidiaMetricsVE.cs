namespace NOVORA.VisionEngine.Metrics;

public sealed record NvidiaMetricsVE(
    long PacketsSubmitted,
    long FramesDecoded,
    long FramesDropped,
    long Fallbacks,
    double LastDecodeMilliseconds,
    double MaxDecodeMilliseconds)
{
    public static NvidiaMetricsVE EmptyVE() => new(0, 0, 0, 0, 0, 0);
}
