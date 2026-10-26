using System.Buffers.Binary;
using System.IO;

namespace NOVORA.VisionEngine.Protocol;

/// <summary>
/// Escribe el contrato multimedia que consume VEProtocolReader.
/// </summary>
public sealed class VEProtocolWriter
{
    private readonly Stream _stream;
    private bool _sessionWritten;
    private int _maxPacketLengthVE = VEProtocolConstants.MaxPacketLengthVE;

    public VEProtocolWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public async ValueTask WriteVideoSessionAsync(
        VEProtocolCodec codec,
        VEProtocolSession session,
        CancellationToken cancellationToken = default)
    {
        if (!codec.IsVideoVE())
            throw new ArgumentException("El codec debe ser de video.", nameof(codec));
        ArgumentNullException.ThrowIfNull(session);
        session.ValidateVE();

        byte[] codecId = new byte[VEProtocolConstants.CodecIdSizeVE];
        BinaryPrimitives.WriteUInt32BigEndian(codecId, (uint)codec);

        await _stream.WriteAsync(codecId, cancellationToken).ConfigureAwait(false);
        _maxPacketLengthVE =
            await WriteSessionHeaderAsync(session, cancellationToken).ConfigureAwait(false);
        _sessionWritten = true;
    }

    public async ValueTask WriteSessionUpdateAsync(
        VEProtocolSession session,
        CancellationToken cancellationToken = default)
    {
        if (!_sessionWritten)
            throw new InvalidOperationException("La sesión inicial de video todavía no fue escrita.");
        ArgumentNullException.ThrowIfNull(session);
        session.ValidateVE();
        _maxPacketLengthVE =
            await WriteSessionHeaderAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteVideoPacketAsync(
        ReadOnlyMemory<byte> payload,
        long? presentationTimeUs,
        bool isConfiguration,
        bool isKeyFrame,
        CancellationToken cancellationToken = default)
    {
        if (!_sessionWritten)
            throw new InvalidOperationException("La sesión de video debe escribirse antes que sus paquetes.");
        if (payload.IsEmpty || payload.Length > _maxPacketLengthVE)
            throw new ArgumentException(
                $"El payload de video supera el límite de sesión de " +
                $"{_maxPacketLengthVE} bytes.",
                nameof(payload));
        if (isConfiguration && presentationTimeUs is not null)
            throw new ArgumentException("Un paquete de configuración no debe incluir PTS.", nameof(presentationTimeUs));
        if (!isConfiguration && (presentationTimeUs is null or < 0 ||
                                 (ulong)presentationTimeUs.Value > VEProtocolConstants.PacketPtsMaskVE))
            throw new ArgumentOutOfRangeException(nameof(presentationTimeUs));

        ulong ptsFlags = isConfiguration
            ? VEProtocolConstants.PacketFlagConfigVE
            : (ulong)presentationTimeUs!.Value;
        if (isKeyFrame) ptsFlags |= VEProtocolConstants.PacketFlagKeyFrameVE;

        await WriteFramedAsync(ptsFlags, payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteAudioSessionAsync(
        VEProtocolCodec codec,
        CancellationToken cancellationToken = default)
    {
        if (!codec.IsAudioVE())
            throw new ArgumentException("El codec debe ser de audio.", nameof(codec));
        byte[] codecId = new byte[VEProtocolConstants.CodecIdSizeVE];
        BinaryPrimitives.WriteUInt32BigEndian(codecId, (uint)codec);
        await _stream.WriteAsync(codecId, cancellationToken).ConfigureAwait(false);
        _maxPacketLengthVE = VEProtocolConstants.MaxPacketLengthVE;
        _sessionWritten = true;
    }

    public ValueTask WriteAudioPacketAsync(
        ReadOnlyMemory<byte> payload,
        long presentationTimeUs,
        CancellationToken cancellationToken = default)
        => WriteMediaPacketAsync(payload, presentationTimeUs, false, cancellationToken);

    private async ValueTask WriteMediaPacketAsync(
        ReadOnlyMemory<byte> payload,
        long presentationTimeUs,
        bool keyFrame,
        CancellationToken cancellationToken)
    {
        if (!_sessionWritten)
            throw new InvalidOperationException("La sesión debe escribirse antes que sus paquetes.");
        if (payload.IsEmpty || payload.Length > _maxPacketLengthVE)
            throw new ArgumentException("El payload tiene una longitud inválida.", nameof(payload));
        if (presentationTimeUs < 0 || (ulong)presentationTimeUs > VEProtocolConstants.PacketPtsMaskVE)
            throw new ArgumentOutOfRangeException(nameof(presentationTimeUs));
        ulong ptsFlags = (ulong)presentationTimeUs;
        if (keyFrame) ptsFlags |= VEProtocolConstants.PacketFlagKeyFrameVE;
        await WriteFramedAsync(ptsFlags, payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Header and payload in one write: over TLS with NoDelay, two writes become two records
    /// and an extra small segment per frame.
    /// </summary>
    private async ValueTask WriteFramedAsync(
        ulong ptsFlags,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        int length = VEProtocolConstants.PacketHeaderSizeVE + payload.Length;
        byte[] frame = System.Buffers.ArrayPool<byte>.Shared.Rent(length);
        try
        {
            BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(0, 8), ptsFlags);
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8, 4), checked((uint)payload.Length));
            payload.Span.CopyTo(frame.AsSpan(VEProtocolConstants.PacketHeaderSizeVE));
            await _stream.WriteAsync(frame.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(frame);
        }
    }

    private async ValueTask<int> WriteSessionHeaderAsync(
        VEProtocolSession session,
        CancellationToken cancellationToken)
    {
        int wireMaxPacketLength = Math.Min(
            VEProtocolConstants.RoundUpToPacketUnitVE(session.MaxPacketLength),
            VEProtocolConstants.MaxPacketLengthForResolutionVE(
                session.Width,
                session.Height));

        byte[] header = new byte[VEProtocolConstants.PacketHeaderSizeVE];
        header[0] = 0x80;
        header[1] = checked((byte)(session.RotationDegrees / 90));
        header[2] = checked((byte)(
            wireMaxPacketLength / VEProtocolConstants.PacketLengthUnitVE));
        if (session.ClientResized) header[3] = 0x01;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), checked((uint)session.Width));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8, 4), checked((uint)session.Height));
        await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        return wireMaxPacketLength;
    }
}
