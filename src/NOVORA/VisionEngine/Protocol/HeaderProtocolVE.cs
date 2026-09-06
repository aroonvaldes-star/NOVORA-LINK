namespace NOVORA.VisionEngine.Protocol;

/// <summary>
/// Header parseado del canal multimedia de VisionEngine.
/// Para SessionVE no existe payload; para MediaVE PacketLength indica su tamaño.
/// </summary>
public sealed record HeaderProtocolVE(
    KindProtocolVE Kind,
    SessionProtocolVE? Session,
    long? PresentationTimeUs,
    bool IsConfiguration,
    bool IsKeyFrame,
    int PacketLength)
{
    public static HeaderProtocolVE CreateSessionVE(
        SessionProtocolVE session)
        => new(
            Kind: KindProtocolVE.Session,
            Session: session,
            PresentationTimeUs: null,
            IsConfiguration: false,
            IsKeyFrame: false,
            PacketLength: 0);

    public static HeaderProtocolVE CreateMediaVE(
        long? presentationTimeUs,
        bool isConfiguration,
        bool isKeyFrame,
        int packetLength)
        => new(
            Kind: KindProtocolVE.Media,
            Session: null,
            PresentationTimeUs: presentationTimeUs,
            IsConfiguration: isConfiguration,
            IsKeyFrame: isKeyFrame,
            PacketLength: packetLength);
}
