using NOVORA.VisionEngine.Video;
using NOVORA.NVIDIA;

namespace NOVORA.VisionEngine.Performance;

public sealed record VEProfileOptions(
    VEProfile Profile,
    VEPerformanceProfile PerformanceProfile,
    VEAccelerationRequest AccelerationRequest)
{
    public static VEProfileOptions CreateVE(VEProfile profile) => profile switch
    {
        VEProfile.Automatic => new(profile, VEPerformanceProfile.Balanced, new(VEAccelerationBackend.Automatic)),
        VEProfile.Competitive => new(profile, VEPerformanceProfile.Gaming, new(VEAccelerationBackend.Automatic)),
        VEProfile.Balanced => new(profile, VEPerformanceProfile.Balanced, new(VEAccelerationBackend.Automatic)),
        VEProfile.Quality => new(profile, VEPerformanceProfile.Video, new(VEAccelerationBackend.Automatic)),
        VEProfile.Smooth => new(profile, VEPerformanceProfile.Gaming, new(VEAccelerationBackend.Automatic)),
        VEProfile.Streaming => new(profile, VEPerformanceProfile.Video, new(VEAccelerationBackend.Automatic)),
        VEProfile.Software => new(profile, VEPerformanceProfile.Balanced, new(VEAccelerationBackend.Software)),
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };

    public static VEProfile FromLegacyNvidiaVE(string? profile) => profile?.Trim() switch
    {
        "Competitive" => VEProfile.Competitive,
        "Balanced" => VEProfile.Balanced,
        "VisionPlus" => VEProfile.Quality,
        "Smooth" => VEProfile.Smooth,
        "Stream" => VEProfile.Streaming,
        "Disabled" => VEProfile.Software,
        _ => VEProfile.Automatic
    };

    public NLNVIDIAProfile ToLegacyNvidiaVE() =>
        AccelerationRequest.Backend == VEAccelerationBackend.Software
            ? NLNVIDIAProfile.Disabled
            : NLNVIDIAProfile.Automatic;
}
