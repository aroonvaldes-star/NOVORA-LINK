namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Estadísticas acumuladas del pipeline headless.
/// </summary>
public sealed record VEVideoStats(
    long PacketsReceived,
    long BytesReceived,
    long ConfigurationPackets,
    long KeyFramesReceived,
    long FramesDecoded,
    long DecodeErrors,
    long PooledPayloadRents = 0,
    long PooledPayloadBytes = 0,
    long PayloadBytesPerSecond = 0,
    int NegotiatedMaxPacketLength = 0);
