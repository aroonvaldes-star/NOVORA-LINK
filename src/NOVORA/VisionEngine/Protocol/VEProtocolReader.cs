namespace NOVORA.VisionEngine.Protocol;

/// <summary>
/// Lector exacto del protocolo multimedia de scrcpy 4.1.
/// Sirve tanto para video como para audio y no crea superficies gráficas.
/// </summary>
public sealed class VEProtocolReader
{
    private readonly Stream _stream;

    public VEProtocolReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public async ValueTask<VEProtocolCodec> ReadCodecAsync(
        CancellationToken cancellationToken = default)
    {
        VEProtocolCodec codec = await ReadCodecValueAsync(cancellationToken)
            .ConfigureAwait(false);

        if (codec == VEProtocolCodec.Disabled)
        {
            throw new InvalidOperationException(
                "El servidor Android deshabilitó explícitamente el video.");
        }

        if (codec == VEProtocolCodec.ConfigurationError)
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

    public async ValueTask<VEProtocolCodec> ReadAudioCodecAsync(
        CancellationToken cancellationToken = default)
    {
        VEProtocolCodec codec = await ReadCodecValueAsync(cancellationToken)
            .ConfigureAwait(false);

        if (codec is VEProtocolCodec.Disabled or VEProtocolCodec.ConfigurationError)
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

    private async ValueTask<VEProtocolCodec> ReadCodecValueAsync(
        CancellationToken cancellationToken)
    {
        byte[] data = new byte[VEProtocolConstants.CodecIdSizeVE];

        await _stream.ReadExactlyAsync(
                data.AsMemory(),
                cancellationToken)
            .ConfigureAwait(false);

        uint raw = VEProtocolBinary.ReadUInt32VE(data);
        return (VEProtocolCodec)raw;
    }

    public async ValueTask<VEProtocolHeader> ReadHeaderAsync(
        CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[VEProtocolConstants.PacketHeaderSizeVE];

        await _stream.ReadExactlyAsync(
                header.AsMemory(),
                cancellationToken)
            .ConfigureAwait(false);

        bool isSession = (header[0] & 0x80) != 0;

        if (isSession)
        {
            int width = checked((int)VEProtocolBinary.ReadUInt32VE(
                header.AsSpan(4, 4)));

            int height = checked((int)VEProtocolBinary.ReadUInt32VE(
                header.AsSpan(8, 4)));

            bool clientResized = (header[3] & 0x01) != 0;

            VEProtocolSession session =
                new(
                    Width: width,
                    Height: height,
                    ClientResized: clientResized);

            session.ValidateVE();

            return VEProtocolHeader.CreateSessionVE(session);
        }

        ulong ptsFlags = VEProtocolBinary.ReadUInt64VE(
            header.AsSpan(0, 8));

        uint rawLength = VEProtocolBinary.ReadUInt32VE(
            header.AsSpan(8, 4));

        if (rawLength == 0 ||
            rawLength > VEProtocolConstants.MaxPacketLengthVE)
        {
            throw new InvalidDataException(
                $"Longitud de paquete VisionEngine inválida: {rawLength}.");
        }

        bool isConfiguration =
            (ptsFlags & VEProtocolConstants.PacketFlagConfigVE) != 0;

        bool isKeyFrame =
            (ptsFlags & VEProtocolConstants.PacketFlagKeyFrameVE) != 0;

        long? pts = isConfiguration
            ? null
            : checked((long)(ptsFlags & VEProtocolConstants.PacketPtsMaskVE));

        return VEProtocolHeader.CreateMediaVE(
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
