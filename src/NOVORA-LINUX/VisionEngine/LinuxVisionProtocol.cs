using System.Buffers.Binary;

namespace NOVORA.Linux.VisionEngine;

internal enum VideoCodec : uint
{
    H264 = 0x68323634,
    H265 = 0x68323635,
    Av1 = 0x00617631,
    Vp8 = 0x00767038,
    Vp9 = 0x00767039
}

internal sealed record VideoSession(int Width, int Height, int RotationDegrees, int MaxPacketLength);

internal sealed record VideoPacket(
    VideoSession Session,
    byte[] Data,
    long? PresentationTimeUs,
    bool IsConfiguration,
    bool IsKeyFrame);

internal sealed class LinuxVisionProtocolReader(Stream stream)
{
    private const int MaxPacketLength = 16 * 1024 * 1024;
    private const int PacketLengthUnit = 256 * 1024;
    private const int MinimumPacketLength = PacketLengthUnit;
    private const ulong ConfigFlag = 1UL << 62;
    private const ulong KeyFrameFlag = 1UL << 61;
    private const ulong PtsMask = KeyFrameFlag - 1;
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private readonly byte[] _header = new byte[12];
    private int _sessionMaxPacketLength = MaxPacketLength;

    public async Task<(VideoCodec Codec, VideoSession Session)> OpenAsync(
        CancellationToken cancellationToken)
    {
        byte[] codecBytes = new byte[sizeof(uint)];
        await _stream.ReadExactlyAsync(codecBytes, cancellationToken).ConfigureAwait(false);
        uint rawCodec = BinaryPrimitives.ReadUInt32BigEndian(codecBytes);
        if (!Enum.IsDefined(typeof(VideoCodec), rawCodec))
            throw new NotSupportedException($"Codec de video VE no soportado: 0x{rawCodec:x8}.");

        VideoSession session = await ReadSessionAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("VisionEngine esperaba un encabezado de sesión de video.");
        _sessionMaxPacketLength = session.MaxPacketLength;
        return ((VideoCodec)rawCodec, session);
    }

    public async Task<VideoPacket?> ReadPacketAsync(
        VideoSession currentSession,
        Action<VideoSession> sessionChanged,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await _stream.ReadExactlyAsync(_header, cancellationToken).ConfigureAwait(false);
            if ((_header[0] & 0x80) != 0)
            {
                VideoSession session = ParseSession(_header);
                _sessionMaxPacketLength = session.MaxPacketLength;
                currentSession = session;
                sessionChanged(session);
                continue;
            }

            ulong ptsFlags = BinaryPrimitives.ReadUInt64BigEndian(_header.AsSpan(0, 8));
            uint packetLength = BinaryPrimitives.ReadUInt32BigEndian(_header.AsSpan(8, 4));
            if (packetLength == 0 || packetLength > _sessionMaxPacketLength)
                throw new InvalidDataException($"Longitud de paquete VE inválida: {packetLength}.");

            var payload = new byte[checked((int)packetLength)];
            await _stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            bool isConfiguration = (ptsFlags & ConfigFlag) != 0;
            return new VideoPacket(
                currentSession,
                payload,
                isConfiguration ? null : checked((long)(ptsFlags & PtsMask)),
                isConfiguration,
                (ptsFlags & KeyFrameFlag) != 0);
        }
    }

    private async Task<VideoSession?> ReadSessionAsync(CancellationToken cancellationToken)
    {
        await _stream.ReadExactlyAsync(_header, cancellationToken).ConfigureAwait(false);
        return (_header[0] & 0x80) != 0 ? ParseSession(_header) : null;
    }

    private static VideoSession ParseSession(ReadOnlySpan<byte> header)
    {
        int width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4)));
        int height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.Slice(8, 4)));
        int rotation = header[1] * 90;
        if (header[2] > MaxPacketLength / PacketLengthUnit)
            throw new InvalidDataException("Límite de paquete VE inválido.");
        int advertised = header[2] == 0
            ? MaxPacketLength
            : header[2] * PacketLengthUnit;
        long resolutionBound = Math.Max(2L * 1024 * 1024, (long)width * height * 4);
        int maxPacketLength = (int)Math.Min(advertised, Math.Min(MaxPacketLength, resolutionBound));
        maxPacketLength = (int)Math.Min(MaxPacketLength,
            ((long)maxPacketLength + PacketLengthUnit - 1) / PacketLengthUnit * PacketLengthUnit);
        if (width is <= 0 or > 16384 || height is <= 0 or > 16384 ||
            rotation is not (0 or 90 or 180 or 270) ||
            maxPacketLength < MinimumPacketLength)
            throw new InvalidDataException($"Encabezado de sesión VE inválido: {width}x{height}, {rotation}°.");
        return new VideoSession(width, height, rotation, maxPacketLength);
    }

    public static string GetFfmpegDemuxer(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "h264",
        VideoCodec.H265 => "hevc",
        VideoCodec.Av1 => "av1",
        VideoCodec.Vp8 => "ivf",
        VideoCodec.Vp9 => "ivf",
        _ => throw new NotSupportedException($"Codec de video VE no soportado: {codec}.")
    };
}
