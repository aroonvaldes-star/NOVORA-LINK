using NOVORA.VisionEngine.Protocol;

namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Snapshot del video sin renderer.
/// </summary>
public sealed record VEVideoStatus(
    VEVideoStates State,
    VEProtocolCodec? Codec,
    VEProtocolSession? Session,
    VEVideoStats Stats,
    bool RendererEnabled,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public string DecoderName { get; init; } = "Sin decoder";
    public bool NvdecActive { get; init; }
    public string? DecoderFallbackReason { get; init; }

    public static VEVideoStatus CreateInitialVE()
        => new(
            State: VEVideoStates.Stopped,
            Codec: null,
            Session: null,
            Stats: new VEVideoStats(0, 0, 0, 0, 0, 0),
            RendererEnabled: false,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Message: "Video VisionEngine detenido.",
            LastError: null);
}
