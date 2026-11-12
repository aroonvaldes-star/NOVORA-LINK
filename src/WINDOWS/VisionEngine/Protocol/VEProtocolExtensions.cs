namespace NOVORA.VisionEngine.Protocol;

/// <summary>
/// Extensiones de codec del protocolo VisionEngine.
/// </summary>
public static class VEProtocolExtensions
{
    public static bool IsVideoVE(this VEProtocolCodec codec)
        => codec is
            VEProtocolCodec.H264 or
            VEProtocolCodec.H265 or
            VEProtocolCodec.Av1 or
            VEProtocolCodec.Vp8 or
            VEProtocolCodec.Vp9;

    public static bool IsAudioVE(this VEProtocolCodec codec)
        => codec is
            VEProtocolCodec.Opus or
            VEProtocolCodec.Aac or
            VEProtocolCodec.Flac or
            VEProtocolCodec.Raw;

    public static bool RequiresConfigurationMergeVE(
        this VEProtocolCodec codec)
        => codec is
            VEProtocolCodec.H264 or
            VEProtocolCodec.H265;

    public static string GetFfmpegDecoderNameVE(
        this VEProtocolCodec codec)
        => codec switch
        {
            VEProtocolCodec.H264 => "h264",
            VEProtocolCodec.H265 => "hevc",
            VEProtocolCodec.Av1 => "av1",
            VEProtocolCodec.Vp8 => "vp8",
            VEProtocolCodec.Vp9 => "vp9",
            VEProtocolCodec.Opus => "opus",
            VEProtocolCodec.Aac => "aac",
            VEProtocolCodec.Flac => "flac",
            VEProtocolCodec.Raw => "pcm_s16le",
            _ => throw new NotSupportedException(
                $"Codec VisionEngine no soportado: {codec}.")
        };

    public static string GetServerNameVE(
        this VEProtocolCodec codec)
        => codec switch
        {
            VEProtocolCodec.H264 => "h264",
            VEProtocolCodec.H265 => "h265",
            VEProtocolCodec.Av1 => "av1",
            VEProtocolCodec.Vp8 => "vp8",
            VEProtocolCodec.Vp9 => "vp9",
            VEProtocolCodec.Opus => "opus",
            VEProtocolCodec.Aac => "aac",
            VEProtocolCodec.Flac => "flac",
            VEProtocolCodec.Raw => "raw",
            _ => throw new NotSupportedException(
                $"Codec VisionEngine no soportado: {codec}.")
        };
}
