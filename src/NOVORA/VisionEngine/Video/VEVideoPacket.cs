namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Paquete codificado recibido desde MediaCodec a través del protocolo VE.
/// </summary>
public sealed record VEVideoPacket(
    long? PresentationTimeUs,
    bool IsConfiguration,
    bool IsKeyFrame,
    byte[] Data,
    long ReceivedTimestamp = 0)
{
    public int LengthVE => Data.Length;
}
