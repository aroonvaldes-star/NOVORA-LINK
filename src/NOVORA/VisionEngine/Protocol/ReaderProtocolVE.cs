namespace NOVORA.VisionEngine.Protocol;

/// <summary>
/// Lector exacto del protocolo multimedia de scrcpy 4.1.
/// Sirve tanto para video como para audio y no crea superficies gráficas.
/// </summary>
public sealed class ReaderProtocolVE
{
    private readonly Stream _stream;

    public ReaderProtocolVE(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public async ValueTask<CodecProtocolVE> ReadCodecAsync(
        CancellationToken cancellationToken = default)
    {
        CodecProtocolVE codec = await ReadCodecValueAsync(cancellationToken)
            .ConfigureAwait(false);

        if (codec == CodecProtocolVE.Disabled)
        {
            throw new InvalidOperationException(
                "El servidor Android deshabilitó explícitamente el video.");
        }

        if (codec == CodecProtocolVE.ConfigurationError)
        {
            throw new InvalidOperationException(
                "El servidor Android reportó un error de configuración de video.");
        }

        if (!codec.IsVideoVE())
        {
            throw new NotSupportedException(
                $"Codec de video desconocido: 0x{(uint)codec:x8}.");
        }

        return codec;
    }

    public async ValueTask<CodecProtocolVE> ReadAudioCodecAsync(
        CancellationToken cancellationToken = default)
    {
        CodecProtocolVE codec = await ReadCodecValueAsync(cancellationToken)
            .ConfigureAwait(false);

        if (codec is CodecProtocolVE.Disabled or CodecProtocolVE.ConfigurationError)
        {
            return codec;
        }

        if (!codec.IsAudioVE())
        {
            throw new NotSupportedException(
                $"Codec de audio desconocido: 0x{(uint)codec:x8}.");
        }

        return codec;
    }

    private async ValueTask<CodecProtocolVE> ReadCodecValueAsync(
        CancellationToken cancellationToken)
    {
        byte[] data = new byte[ConstantsProtocolVE.CodecIdSizeVE];

        await _stream.ReadExactlyAsync(
                data.AsMemory(),
                cancellationToken)
            .ConfigureAwait(false);

        uint raw = BinaryProtocolVE.ReadUInt32VE(data);
        return (CodecProtocolVE)raw;
    }

    public async ValueTask<HeaderProtocolVE> ReadHeaderAsync(
        CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[ConstantsProtocolVE.PacketHeaderSizeVE];

        await _stream.ReadExactlyAsync(
                header.AsMemory(),
                cancellationToken)
            .ConfigureAwait(false);

        bool isSession = (header[0] & 0x80) != 0;

        if (isSession)
        {
            int width = checked((int)BinaryProtocolVE.ReadUInt32VE(
                header.AsSpan(4, 4)));

            int height = checked((int)BinaryProtocolVE.ReadUInt32VE(
                header.AsSpan(8, 4)));

            bool clientResized = (header[3] & 0x01) != 0;

            SessionProtocolVE session =
                new(
                    Width: width,
                    Height: height,
                    ClientResized: clientResized);

            session.ValidateVE();

            return HeaderProtocolVE.CreateSessionVE(session);
        }

        ulong ptsFlags = BinaryProtocolVE.ReadUInt64VE(
            header.AsSpan(0, 8));

        uint rawLength = BinaryProtocolVE.ReadUInt32VE(
            header.AsSpan(8, 4));

        if (rawLength == 0 ||
            rawLength > ConstantsProtocolVE.MaxPacketLengthVE)
        {
            throw new InvalidDataException(
                $"Longitud de paquete VisionEngine inválida: {rawLength}.");
        }

        bool isConfiguration =
            (ptsFlags & ConstantsProtocolVE.PacketFlagConfigVE) != 0;

        bool isKeyFrame =
            (ptsFlags & ConstantsProtocolVE.PacketFlagKeyFrameVE) != 0;

        long? pts = isConfiguration
            ? null
            : checked((long)(ptsFlags & ConstantsProtocolVE.PacketPtsMaskVE));

        return HeaderProtocolVE.CreateMediaVE(
            presentationTimeUs: pts,
            isConfiguration: isConfiguration,
            isKeyFrame: isKeyFrame,
            packetLength: checked((int)rawLength));
    }

    public async ValueTask<byte[]> ReadPayloadAsync(
        int length,
        CancellationToken cancellationToken = default)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                "La longitud del payload debe ser mayor que cero.");
        }

        byte[] payload = new byte[length];

        await _stream.ReadExactlyAsync(
                payload.AsMemory(),
                cancellationToken)
            .ConfigureAwait(false);

        return payload;
    }
}
