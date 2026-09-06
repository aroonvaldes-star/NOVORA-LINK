namespace NOVORA.VisionEngine.Metrics;

public sealed record VideoMetricsVE(
    long PacketsReceived,
    long BytesReceived,
    long FramesDecoded,
    long DecodeErrors,
    double PacketsPerSecond,
    double FramesPerSecond,
    double MegabitsPerSecond)
{
    public static VideoMetricsVE EmptyVE() => new(0, 0, 0, 0, 0, 0, 0);
}
