using NOVORA.VisionEngine.Protocol;

namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Demultiplexa el único socket de video del Block B.
/// Lee codec id, session headers y paquetes MediaCodec de 12-byte metadata.
/// </summary>
public sealed class DemuxerVideoVE
{
    private readonly ReaderProtocolVE _reader;

    public DemuxerVideoVE(Stream stream)
    {
        _reader = new ReaderProtocolVE(
            stream ?? throw new ArgumentNullException(nameof(stream)));
    }

    public event EventHandler<SessionProtocolVE>? SessionChangedVE;

    public CodecProtocolVE? CodecVE { get; private set; }

    public SessionProtocolVE? SessionVE { get; private set; }

    public async Task<SessionProtocolVE> OpenAsync(
        CancellationToken cancellationToken = default)
    {
        CodecVE = await _reader.ReadCodecAsync(cancellationToken)
            .ConfigureAwait(false);

        HeaderProtocolVE header =
            await _reader.ReadHeaderAsync(cancellationToken)
                .ConfigureAwait(false);

        if (header.Kind != KindProtocolVE.Session ||
            header.Session is null)
        {
            throw new InvalidDataException(
                "VisionEngine esperaba un session header inmediatamente después del codec id.");
        }

        SessionVE = header.Session;
        SessionChangedVE?.Invoke(this, header.Session);

        return header.Session;
    }

    public async Task<PacketVideoVE?> ReadPacketAsync(
        CancellationToken cancellationToken = default)
    {
        if (CodecVE is null || SessionVE is null)
        {
            throw new InvalidOperationException(
                "DemuxerVideoVE debe abrirse antes de leer paquetes.");
        }

        while (true)
        {
            HeaderProtocolVE header =
                await _reader.ReadHeaderAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (header.Kind == KindProtocolVE.Session)
            {
                SessionProtocolVE session = header.Session
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

            return new PacketVideoVE(
                PresentationTimeUs: header.PresentationTimeUs,
                IsConfiguration: header.IsConfiguration,
                IsKeyFrame: header.IsKeyFrame,
                Data: payload);
        }
    }
}
