namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Estadísticas acumuladas del pipeline headless.
/// </summary>
public sealed record StatsVideoVE(
    long PacketsReceived,
    long BytesReceived,
    long ConfigurationPackets,
    long KeyFramesReceived,
    long FramesDecoded,
    long DecodeErrors);
