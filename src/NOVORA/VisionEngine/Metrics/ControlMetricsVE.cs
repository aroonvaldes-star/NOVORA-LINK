namespace NOVORA.VisionEngine.Metrics;

public sealed record ControlMetricsVE(
    long MessagesSent,
    long MessagesReceived,
    long BytesSent,
    long BytesReceived,
    long Errors,
    double MessagesPerSecond)
{
    public static ControlMetricsVE EmptyVE() => new(0, 0, 0, 0, 0, 0);
}
