using System.Buffers;
using NOVORA.VisionEngine.Protocol;
using System.IO;

namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Demultiplexa el único socket de video del Block B.
/// Lee codec id, session headers y paquetes MediaCodec de 12-byte metadata.
/// </summary>
public sealed class VEVideoDemuxer
{
    private readonly VEProtocolReader _reader;

    public VEVideoDemuxer(Stream stream, ArrayPool<byte>? payloadPool = null)
    {
        _reader = new VEProtocolReader(
            stream ?? throw new ArgumentNullException(nameof(stream)),
            payloadPool);
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

            VEPooledPayload payload =
                await _reader.ReadPayloadPooledAsync(
                        header.PacketLength,
                        cancellationToken)
                    .ConfigureAwait(false);

            try
            {
                return VEVideoPacket.CreatePooledVE(
                    presentationTimeUs: header.PresentationTimeUs,
                    isConfiguration: header.IsConfiguration,
                    isKeyFrame: header.IsKeyFrame,
                    lease: payload,
                    receivedTimestamp: System.Diagnostics.Stopwatch.GetTimestamp());
            }
            catch
            {
                payload.Dispose();
                throw;
            }
        }
    }
}
