using System.Buffers;
using System.IO;

namespace NOVORA.VisionEngine.Protocol;

/// <summary>
/// Lector del protocolo multimedia de App Control.
/// Sirve tanto para video como para audio y no crea superficies gráficas.
/// </summary>
public sealed class VEProtocolReader
{
    private readonly Stream _stream;
    private readonly ArrayPool<byte> _payloadPool;
    // The demuxer reads one packet at a time; a reusable header removes the
    // remaining small allocation from the per-frame path.
    private readonly byte[] _headerBufferVE =
        new byte[VEProtocolConstants.PacketHeaderSizeVE];
    private int _sessionMaxPacketLengthVE = VEProtocolConstants.MaxPacketLengthVE;

    public VEProtocolReader(Stream stream, ArrayPool<byte>? payloadPool = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _payloadPool = payloadPool ?? ArrayPool<byte>.Shared;
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
        byte[] header = _headerBufferVE;

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
            int rotationDegrees = header[1] * 90;

            if (header[2] > VEProtocolConstants.MaxPacketLengthUnitsVE)
                throw new InvalidDataException(
                    $"Límite de paquete VisionEngine inválido: {header[2]} unidades.");

            int advertisedMax = header[2] == 0
                ? VEProtocolConstants.MaxPacketLengthVE
                : header[2] * VEProtocolConstants.PacketLengthUnitVE;

            // The geometry is also a safety bound: a 720p session may not
            // announce the whole 16 MiB budget. Legacy senders use zero and
            // are capped here without requiring a new Android build.
            int maxPacketLength = Math.Min(
                advertisedMax,
                VEProtocolConstants.MaxPacketLengthForResolutionVE(width, height));

            VEProtocolSession session =
                new(
                     Width: width,
                     Height: height,
                     ClientResized: clientResized,
                     RotationDegrees: rotationDegrees,
                     MaxPacketLength: maxPacketLength);

            session.ValidateVE();
            _sessionMaxPacketLengthVE = session.MaxPacketLength;

            return VEProtocolHeader.CreateSessionVE(session);
        }

        ulong ptsFlags = VEProtocolBinary.ReadUInt64VE(
            header.AsSpan(0, 8));

        uint rawLength = VEProtocolBinary.ReadUInt32VE(
            header.AsSpan(8, 4));

        if (rawLength == 0 || rawLength > _sessionMaxPacketLengthVE)
        {
            throw new InvalidDataException(
                $"Longitud de paquete VisionEngine inválida: {rawLength}; " +
                $"límite de sesión {_sessionMaxPacketLengthVE}.");
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
        ValidatePayloadLengthVE(length);

        byte[] payload = new byte[length];

        await _stream.ReadExactlyAsync(
                payload.AsMemory(),
                cancellationToken)
            .ConfigureAwait(false);

        return payload;
    }

    /// <summary>
    /// Reads a media payload into a pooled buffer. The caller owns the lease
    /// and must dispose it after the decoder/recorder has copied the bytes.
    /// </summary>
    public async ValueTask<VEPooledPayload> ReadPayloadPooledAsync(
        int length,
        CancellationToken cancellationToken = default)
    {
        ValidatePayloadLengthVE(length);

        byte[] payload = _payloadPool.Rent(length);

        try
        {
            await _stream.ReadExactlyAsync(
                    payload.AsMemory(0, length),
                    cancellationToken)
                .ConfigureAwait(false);

            return new VEPooledPayload(payload, length, _payloadPool);
        }
        catch
        {
            _payloadPool.Return(payload);
            throw;
        }
    }

    private void ValidatePayloadLengthVE(int length)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                "La longitud del payload debe ser mayor que cero.");
        }

        if (length > _sessionMaxPacketLengthVE)
        {
            throw new InvalidDataException(
                $"Payload VisionEngine de {length} bytes supera el límite " +
                $"de sesión de {_sessionMaxPacketLengthVE} bytes.");
        }
    }
}
