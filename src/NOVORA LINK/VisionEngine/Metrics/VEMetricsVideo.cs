namespace NOVORA.VisionEngine.Metrics;

public sealed record VEMetricsVideo(
    long PacketsReceived,
    long BytesReceived,
    long FramesDecoded,
    long DecodeErrors,
    double PacketsPerSecond,
    double FramesPerSecond,
    double MegabitsPerSecond)
{
    public static VEMetricsVideo EmptyVE() => new(0, 0, 0, 0, 0, 0, 0);
}
