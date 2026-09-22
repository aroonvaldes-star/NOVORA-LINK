using NOVORA.VisionEngine.Protocol;

namespace NOVORA.VisionEngine.Performance;

public sealed record VEPerformanceOptions(
    VEPerformanceProfile Profile,
    VEProtocolCodec PreferredCodec,
    int RecommendedBitrate,
    int MaxSize,
    double MaxFps)
{
    // La lista contiene formatos compatibles con ambos extremos de la sesión.
    // Sin información fiable se conserva H.264, independiente del fabricante.
    public static VEPerformanceOptions CreateVE(VEPerformanceProfile profile,
        IEnumerable<VEProtocolCodec>? supportedCodecs = null)
    {
        VEProtocolCodec codec = VEProtocolCodec.H264;
        if (supportedCodecs is not null)
        {
            var supported = supportedCodecs.ToHashSet();
            if (supported.Count > 0)
            {
                VEProtocolCodec[] preference = profile == VEPerformanceProfile.Video
                    ? [VEProtocolCodec.Av1, VEProtocolCodec.H265, VEProtocolCodec.H264]
                    : [VEProtocolCodec.H264, VEProtocolCodec.H265, VEProtocolCodec.Av1];
                var match = preference.Where(supported.Contains).ToArray();
                if (match.Length == 0)
                    throw new ArgumentException("No hay un formato de video compatible.", nameof(supportedCodecs));
                codec = match[0];
            }
        }
        return profile switch
        {
            VEPerformanceProfile.Gaming => new(profile, codec, 8_000_000, 1280, 45),
            VEPerformanceProfile.Balanced => new(profile, codec, 5_000_000, 1280, 45),
            VEPerformanceProfile.Video => new(profile, codec, 6_000_000, 1600, 45),
            VEPerformanceProfile.Battery => new(profile, codec, 3_000_000, 960, 30),
            _ => throw new ArgumentOutOfRangeException(nameof(profile))
        };
    }
}
