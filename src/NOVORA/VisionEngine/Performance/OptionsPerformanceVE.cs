using NOVORA.VisionEngine.Protocol;

namespace NOVORA.VisionEngine.Performance;

public sealed record OptionsPerformanceVE(
    ProfilePerformanceVE Profile,
    CodecProtocolVE PreferredCodec,
    int RecommendedBitrate,
    int MaxSize,
    double MaxFps)
{
    // La lista contiene formatos compatibles con ambos extremos de la sesión.
    // Sin información fiable se conserva H.264, independiente del fabricante.
    public static OptionsPerformanceVE CreateVE(ProfilePerformanceVE profile,
        IEnumerable<CodecProtocolVE>? supportedCodecs = null)
    {
        CodecProtocolVE codec = CodecProtocolVE.H264;
        if (supportedCodecs is not null)
        {
            var supported = supportedCodecs.ToHashSet();
            if (supported.Count > 0)
            {
                CodecProtocolVE[] preference = profile == ProfilePerformanceVE.Video
                    ? [CodecProtocolVE.Av1, CodecProtocolVE.H265, CodecProtocolVE.H264]
                    : [CodecProtocolVE.H264, CodecProtocolVE.H265, CodecProtocolVE.Av1];
                var match = preference.Where(supported.Contains).ToArray();
                if (match.Length == 0)
                    throw new ArgumentException("No hay un formato de video compatible.", nameof(supportedCodecs));
                codec = match[0];
            }
        }
        return profile switch
        {
            ProfilePerformanceVE.Gaming => new(profile, codec, 4_000_000, 1280, 45),
            ProfilePerformanceVE.Balanced => new(profile, codec, 5_000_000, 1280, 45),
            ProfilePerformanceVE.Video => new(profile, codec, 6_000_000, 1600, 45),
            ProfilePerformanceVE.Battery => new(profile, codec, 3_000_000, 960, 30),
            _ => throw new ArgumentOutOfRangeException(nameof(profile))
        };
    }
}
