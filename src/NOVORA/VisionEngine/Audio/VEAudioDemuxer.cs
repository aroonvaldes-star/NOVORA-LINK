using NOVORA.VisionEngine.Protocol;

namespace NOVORA.VisionEngine.Audio;

/// <summary>
/// Lee el stream de audio MediaCodec de scrcpy 4.1. Audio contiene codec id
/// y luego paquetes con metadata de 12 bytes, sin session header de video.
/// </summary>
public sealed class VEAudioDemuxer
{
    private readonly VEProtocolReader _reader;

    public VEAudioDemuxer(Stream stream)
        => _reader = new VEProtocolReader(stream ?? throw new ArgumentNullException(nameof(stream)));

    public VEProtocolCodec? CodecVE { get; private set; }

    public async Task<VEProtocolCodec> OpenAsync(CancellationToken cancellationToken = default)
    {
        CodecVE = await _reader.ReadAudioCodecAsync(cancellationToken).ConfigureAwait(false);
        return CodecVE.Value;
    }

    public async Task<VEAudioPacket> ReadPacketAsync(CancellationToken cancellationToken = default)
    {
        if (CodecVE is null)
            throw new InvalidOperationException("VEAudioDemuxer debe abrirse antes de leer paquetes.");

        VEProtocolHeader header = await _reader.ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
        if (header.Kind == VEProtocolKind.Session)
            throw new InvalidDataException("El stream de audio VisionEngine recibió un session header de video inesperado.");

        byte[] payload = await _reader.ReadPayloadAsync(header.PacketLength, cancellationToken).ConfigureAwait(false);
        return new VEAudioPacket(header.PresentationTimeUs, header.IsConfiguration, payload);
    }
}
