namespace NOVORA.VisionEngine.Metrics;

public sealed record AudioMetricsVE(
    long PacketsReceived,
    long BytesReceived,
    long FramesDecoded,
    long PcmBytesProduced,
    long DecodeErrors,
    long PlaybackErrors,
    double PacketsPerSecond,
    double FramesPerSecond)
{
    public static AudioMetricsVE EmptyVE() => new(0, 0, 0, 0, 0, 0, 0, 0);
}
