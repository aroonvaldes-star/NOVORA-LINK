namespace NOVORA.VisionEngine.Protocol;

/// <summary>
/// Header parseado del canal multimedia de VisionEngine.
/// Para SessionVE no existe payload; para MediaVE PacketLength indica su tamaño.
/// </summary>
public sealed record VEProtocolHeader(
    VEProtocolKind Kind,
    VEProtocolSession? Session,
    long? PresentationTimeUs,
    bool IsConfiguration,
    bool IsKeyFrame,
    int PacketLength)
{
    public static VEProtocolHeader CreateSessionVE(
        VEProtocolSession session)
        => new(
            Kind: VEProtocolKind.Session,
            Session: session,
            PresentationTimeUs: null,
            IsConfiguration: false,
            IsKeyFrame: false,
            PacketLength: 0);

    public static VEProtocolHeader CreateMediaVE(
        long? presentationTimeUs,
        bool isConfiguration,
        bool isKeyFrame,
        int packetLength)
        => new(
            Kind: VEProtocolKind.Media,
            Session: null,
            PresentationTimeUs: presentationTimeUs,
            IsConfiguration: isConfiguration,
            IsKeyFrame: isKeyFrame,
            PacketLength: packetLength);
}
