namespace NOVORA.VisionEngine.Renderer;

public sealed record VERendererStatus(
    VERendererStates State,
    bool HostAttached,
    bool RendererEnabled,
    string? Backend,
    int TextureWidth,
    int TextureHeight,
    string? PixelFormat,
    int RotationDegrees,
    VERendererMetrics Metrics,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public static VERendererStatus CreateInitialVE()
        => new(
            State: VERendererStates.Stopped,
            HostAttached: false,
            RendererEnabled: false,
            Backend: null,
            TextureWidth: 0,
            TextureHeight: 0,
            PixelFormat: null,
            RotationDegrees: 0,
            Metrics: VERendererMetrics.EmptyVE,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Message: "Renderer VisionEngine detenido.",
            LastError: null);
}
