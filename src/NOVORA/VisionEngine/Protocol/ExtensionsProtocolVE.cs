namespace NOVORA.VisionEngine.Protocol;

/// <summary>
/// Extensiones de codec del protocolo VisionEngine.
/// </summary>
public static class ExtensionsProtocolVE
{
    public static bool IsVideoVE(this CodecProtocolVE codec)
        => codec is
            CodecProtocolVE.H264 or
            CodecProtocolVE.H265 or
            CodecProtocolVE.Av1 or
            CodecProtocolVE.Vp8 or
            CodecProtocolVE.Vp9;

    public static bool IsAudioVE(this CodecProtocolVE codec)
        => codec is
            CodecProtocolVE.Opus or
            CodecProtocolVE.Aac or
            CodecProtocolVE.Flac or
            CodecProtocolVE.Raw;

    public static bool RequiresConfigurationMergeVE(
        this CodecProtocolVE codec)
        => codec is
            CodecProtocolVE.H264 or
            CodecProtocolVE.H265;

    public static string GetFfmpegDecoderNameVE(
        this CodecProtocolVE codec)
        => codec switch
        {
            CodecProtocolVE.H264 => "h264",
            CodecProtocolVE.H265 => "hevc",
            CodecProtocolVE.Av1 => "av1",
            CodecProtocolVE.Vp8 => "vp8",
            CodecProtocolVE.Vp9 => "vp9",
            CodecProtocolVE.Opus => "opus",
            CodecProtocolVE.Aac => "aac",
            CodecProtocolVE.Flac => "flac",
            CodecProtocolVE.Raw => "pcm_s16le",
            _ => throw new NotSupportedException(
                $"Codec VisionEngine no soportado: {codec}.")
        };

    public static string GetServerNameVE(
        this CodecProtocolVE codec)
        => codec switch
        {
            CodecProtocolVE.H264 => "h264",
            CodecProtocolVE.H265 => "h265",
            CodecProtocolVE.Av1 => "av1",
            CodecProtocolVE.Vp8 => "vp8",
            CodecProtocolVE.Vp9 => "vp9",
            CodecProtocolVE.Opus => "opus",
            CodecProtocolVE.Aac => "aac",
            CodecProtocolVE.Flac => "flac",
            CodecProtocolVE.Raw => "raw",
            _ => throw new NotSupportedException(
                $"Codec VisionEngine no soportado: {codec}.")
        };
}
