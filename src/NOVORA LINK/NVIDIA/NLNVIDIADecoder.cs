using NOVORA.VisionEngine.Protocol;

namespace NOVORA.NVIDIA;

/// <summary>Decoders NVDEC de FFmpeg. La salida NV12 vive en CPU; no es zero-copy.</summary>
public static class NLNVIDIADecoder
{
    public static string? GetNameVE(VEProtocolCodec codec) => codec switch
    {
        VEProtocolCodec.H264 => "h264_cuvid",
        VEProtocolCodec.H265 => "hevc_cuvid",
        VEProtocolCodec.Av1 => "av1_cuvid",
        VEProtocolCodec.Vp8 => "vp8_cuvid",
        VEProtocolCodec.Vp9 => "vp9_cuvid",
        _ => null
    };
}
