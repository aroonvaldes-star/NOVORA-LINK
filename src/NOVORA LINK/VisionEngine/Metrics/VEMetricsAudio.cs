namespace NOVORA.VisionEngine.Metrics;

public sealed record VEMetricsAudio(
    long PacketsReceived,
    long BytesReceived,
    long FramesDecoded,
    long PcmBytesProduced,
    long DecodeErrors,
    long PlaybackErrors,
    double PacketsPerSecond,
    double FramesPerSecond)
{
    public static VEMetricsAudio EmptyVE() => new(0, 0, 0, 0, 0, 0, 0, 0);
}
