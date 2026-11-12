namespace NOVORA.VisionEngine.Protocol;

/// <summary>
/// Constantes del protocolo multimedia compartido por App Control y VisionEngine.
/// </summary>
public static class VEProtocolConstants
{
    public const int PacketHeaderSizeVE = 12;
    public const int CodecIdSizeVE = 4;

    /// <summary>
    /// Hard ceiling for a single multiplexed packet. Interactive H.264/H.265
    /// frames larger than this are already far outside the negotiated bitrate
    /// and would only create avoidable Large Object Heap pressure.
    /// </summary>
    public const int MaxPacketLengthVE = 16 * 1024 * 1024;

    /// <summary>
    /// Session headers advertise their packet bound in this unit. The byte
    /// field is zero for legacy senders, which the reader maps to the 16 MiB
    /// hard ceiling. 256 KiB keeps enough precision for 720p/1080p while the
    /// whole 16 MiB range still fits in one byte.
    /// </summary>
    public const int PacketLengthUnitVE = 256 * 1024;
    public const int MaxPacketLengthUnitsVE = MaxPacketLengthVE / PacketLengthUnitVE;
    public const int MinimumSessionPacketLengthVE = PacketLengthUnitVE;

    /// <summary>
    /// Conservative per-frame budget derived from the announced geometry.
    /// Four bytes per pixel covers high-detail keyframes without allowing the
    /// full 16 MiB packet on a 720p session. The result is rounded up to the wire unit.
    /// </summary>
    public static int MaxPacketLengthForResolutionVE(int width, int height)
    {
        if (width <= 0 || height <= 0) return MinimumSessionPacketLengthVE;
        long pixels = (long)width * height;
        // Avoid overflowing pixels * 4 for hostile geometry; anything above the
        // hard ceiling is clamped before the multiplication.
        long resolutionBytes = pixels > MaxPacketLengthVE / 4L
            ? MaxPacketLengthVE
            : pixels * 4L;
        long bytes = Math.Max((long)2 * 1024 * 1024, resolutionBytes);
        return RoundUpToPacketUnitVE((int)Math.Min(MaxPacketLengthVE, bytes));
    }

    /// <summary>
    /// Bounds one packet by a short burst of the announced bitrate plus
    /// keyframe headroom, then by the resolution budget. The sender writes
    /// this value into the session header and enforces it for every frame.
    /// </summary>
    public static int NegotiateMaxPacketLengthVE(int width, int height, int bitrate, int fps)
    {
        long safeBitrate = Math.Max(1, bitrate);
        long safeFps = Math.Clamp(fps, 1, 240);
        long frameBytes = Math.Max(1, safeBitrate / 8 / safeFps);
        long burst = frameBytes * 32 + 4L * 1024 * 1024;
        long resolution = MaxPacketLengthForResolutionVE(width, height);
        return RoundUpToPacketUnitVE((int)Math.Clamp(
            Math.Min(burst, resolution),
            (long)MinimumSessionPacketLengthVE,
            (long)MaxPacketLengthVE));
    }

    public static int RoundUpToPacketUnitVE(int bytes)
    {
        if (bytes <= 0) return PacketLengthUnitVE;
        long units = (bytes + PacketLengthUnitVE - 1L) / PacketLengthUnitVE;
        return (int)Math.Min(MaxPacketLengthVE, units * PacketLengthUnitVE);
    }

    public const int RawAudioSampleRateVE = 48_000;
    public const int RawAudioChannelsVE = 2;
    public const int RawAudioBytesPerSampleVE = 2;
    public const int RawAudioPacketMillisecondsVE = 20;
    public const int RawAudioPacketBytesVE =
        RawAudioSampleRateVE * RawAudioChannelsVE * RawAudioBytesPerSampleVE *
        RawAudioPacketMillisecondsVE / 1000;

    public const ulong PacketFlagSessionVE = 1UL << 63;
    public const ulong PacketFlagConfigVE = 1UL << 62;
    public const ulong PacketFlagKeyFrameVE = 1UL << 61;
    public const ulong PacketPtsMaskVE = PacketFlagKeyFrameVE - 1;

    public const int FfmpegInputPaddingSizeVE = 64;
    public const int FfmpegAgainVE = -11;
    public const int FfmpegEofVE = -541478725;

}
