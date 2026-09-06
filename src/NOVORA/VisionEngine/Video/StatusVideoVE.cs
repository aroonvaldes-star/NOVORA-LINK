using NOVORA.VisionEngine.Protocol;

namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Snapshot del video sin renderer.
/// </summary>
public sealed record StatusVideoVE(
    StatesVideoVE State,
    CodecProtocolVE? Codec,
    SessionProtocolVE? Session,
    StatsVideoVE Stats,
    bool RendererEnabled,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public static StatusVideoVE CreateInitialVE()
        => new(
            State: StatesVideoVE.Stopped,
            Codec: null,
            Session: null,
            Stats: new StatsVideoVE(0, 0, 0, 0, 0, 0),
            RendererEnabled: false,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Message: "Video VisionEngine detenido.",
            LastError: null);
}
