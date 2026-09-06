namespace NOVORA.VisionEngine.Protocol;

/// <summary>
/// Constantes del protocolo multimedia compatible con scrcpy 4.1.
/// Block B comparte estas constantes entre video y audio.
/// </summary>
public static class ConstantsProtocolVE
{
    public const int PacketHeaderSizeVE = 12;
    public const int CodecIdSizeVE = 4;
    public const int DeviceNameFieldLengthVE = 64;
    public const int MaxPacketLengthVE = 64 * 1024 * 1024;

    public const ulong PacketFlagSessionVE = 1UL << 63;
    public const ulong PacketFlagConfigVE = 1UL << 62;
    public const ulong PacketFlagKeyFrameVE = 1UL << 61;
    public const ulong PacketPtsMaskVE = PacketFlagKeyFrameVE - 1;

    public const int FfmpegInputPaddingSizeVE = 64;
    public const int FfmpegAgainVE = -11;
    public const int FfmpegEofVE = -541478725;

    public const string ScrcpyServerClassVE = "com.genymobile.scrcpy.Server";
    public const string ScrcpyCompatibilityVersionVE = "4.1";
    public const string ScrcpySocketPrefixVE = "scrcpy_";
    public const string RemoteServerPathVE = "/data/local/tmp/novora-vision-server.jar";
}
