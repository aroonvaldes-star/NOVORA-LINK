namespace NOVORA.VisionEngine.Metrics;

public sealed record VEMetricsControl(
    long MessagesSent,
    long MessagesReceived,
    long BytesSent,
    long BytesReceived,
    long Errors,
    double MessagesPerSecond)
{
    public static VEMetricsControl EmptyVE() => new(0, 0, 0, 0, 0, 0);
}
