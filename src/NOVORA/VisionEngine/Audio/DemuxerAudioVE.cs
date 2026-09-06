using NOVORA.VisionEngine.Protocol;

namespace NOVORA.VisionEngine.Audio;

/// <summary>
/// Lee el stream de audio MediaCodec de scrcpy 4.1. Audio contiene codec id
/// y luego paquetes con metadata de 12 bytes, sin session header de video.
/// </summary>
public sealed class DemuxerAudioVE
{
    private readonly ReaderProtocolVE _reader;

    public DemuxerAudioVE(Stream stream)
        => _reader = new ReaderProtocolVE(stream ?? throw new ArgumentNullException(nameof(stream)));

    public CodecProtocolVE? CodecVE { get; private set; }

    public async Task<CodecProtocolVE> OpenAsync(CancellationToken cancellationToken = default)
    {
        CodecVE = await _reader.ReadAudioCodecAsync(cancellationToken).ConfigureAwait(false);
        return CodecVE.Value;
    }

    public async Task<PacketAudioVE> ReadPacketAsync(CancellationToken cancellationToken = default)
    {
        if (CodecVE is null)
            throw new InvalidOperationException("DemuxerAudioVE debe abrirse antes de leer paquetes.");

        HeaderProtocolVE header = await _reader.ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
        if (header.Kind == KindProtocolVE.Session)
            throw new InvalidDataException("El stream de audio VisionEngine recibió un session header de video inesperado.");

        byte[] payload = await _reader.ReadPayloadAsync(header.PacketLength, cancellationToken).ConfigureAwait(false);
        return new PacketAudioVE(header.PresentationTimeUs, header.IsConfiguration, payload);
    }
}
