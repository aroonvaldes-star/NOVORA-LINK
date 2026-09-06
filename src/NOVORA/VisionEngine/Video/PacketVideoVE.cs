namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Paquete codificado recibido desde MediaCodec a través del protocolo VE.
/// </summary>
public sealed record PacketVideoVE(
    long? PresentationTimeUs,
    bool IsConfiguration,
    bool IsKeyFrame,
    byte[] Data)
{
    public int LengthVE => Data.Length;
}
