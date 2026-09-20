using NOVORA.VisionEngine.Protocol;

namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Demultiplexa el único socket de video del Block B.
/// Lee codec id, session headers y paquetes MediaCodec de 12-byte metadata.
/// </summary>
public sealed class VEVideoDemuxer
{
    private readonly VEProtocolReader _reader;

    public VEVideoDemuxer(Stream stream)
    {
        _reader = new VEProtocolReader(
            stream ?? throw new ArgumentNullException(nameof(stream)));
    }

    public event EventHandler<VEProtocolSession>? SessionChangedVE;

    public VEProtocolCodec? CodecVE { get; private set; }

    public VEProtocolSession? SessionVE { get; private set; }

    public async Task<VEProtocolSession> OpenAsync(
        CancellationToken cancellationToken = default)
    {
        CodecVE = await _reader.ReadCodecAsync(cancellationToken)
            .ConfigureAwait(false);

        VEProtocolHeader header =
            await _reader.ReadHeaderAsync(cancellationToken)
                .ConfigureAwait(false);

        if (header.Kind != VEProtocolKind.Session ||
            header.Session is null)
        {
            throw new InvalidDataException(
                "VisionEngine esperaba un session header inmediatamente después del codec id.");
        }

        SessionVE = header.Session;
        SessionChangedVE?.Invoke(this, header.Session);

        return header.Session;
    }

    public async Task<VEVideoPacket?> ReadPacketAsync(
        CancellationToken cancellationToken = default)
    {
        if (CodecVE is null || SessionVE is null)
        {
            throw new InvalidOperationException(
                "VEVideoDemuxer debe abrirse antes de leer paquetes.");
        }

        while (true)
        {
            VEProtocolHeader header =
                await _reader.ReadHeaderAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (header.Kind == VEProtocolKind.Session)
            {
                VEProtocolSession session = header.Session
                    ?? throw new InvalidDataException(
                        "Session header VisionEngine sin datos de sesión.");

                SessionVE = session;
                SessionChangedVE?.Invoke(this, session);
                continue;
            }

            byte[] payload =
                await _reader.ReadPayloadAsync(
                        header.PacketLength,
                        cancellationToken)
                    .ConfigureAwait(false);

            return new VEVideoPacket(
                PresentationTimeUs: header.PresentationTimeUs,
                IsConfiguration: header.IsConfiguration,
                IsKeyFrame: header.IsKeyFrame,
                Data: payload);
        }
    }
}
