using NOVORA.VisionEngine.Protocol;

namespace NOVORA.VisionEngine.Audio;

public sealed record VEAudioStatus(
    VEAudioStates State,
    VEProtocolCodec? Codec,
    VEAudioStats Stats,
    bool PlaybackEnabled,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public static VEAudioStatus CreateInitialVE()
        => new(
            VEAudioStates.Stopped,
            null,
            new VEAudioStats(0, 0, 0, 0, 0, 0),
            false,
            DateTimeOffset.UtcNow,
            "Audio VisionEngine detenido.",
            null);
}
