using NOVORA.VisionEngine.Protocol;

namespace NOVORA.VisionEngine.Audio;

public sealed record StatusAudioVE(
    StatesAudioVE State,
    CodecProtocolVE? Codec,
    StatsAudioVE Stats,
    bool PlaybackEnabled,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public static StatusAudioVE CreateInitialVE()
        => new(
            StatesAudioVE.Stopped,
            null,
            new StatsAudioVE(0, 0, 0, 0, 0, 0),
            false,
            DateTimeOffset.UtcNow,
            "Audio VisionEngine detenido.",
            null);
}
