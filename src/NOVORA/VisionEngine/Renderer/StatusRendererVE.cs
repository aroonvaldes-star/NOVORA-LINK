namespace NOVORA.VisionEngine.Renderer;

public sealed record StatusRendererVE(
    StatesRendererVE State,
    bool HostAttached,
    bool RendererEnabled,
    string? Backend,
    int TextureWidth,
    int TextureHeight,
    string? PixelFormat,
    int RotationDegrees,
    MetricsRendererVE Metrics,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public static StatusRendererVE CreateInitialVE()
        => new(
            State: StatesRendererVE.Stopped,
            HostAttached: false,
            RendererEnabled: false,
            Backend: null,
            TextureWidth: 0,
            TextureHeight: 0,
            PixelFormat: null,
            RotationDegrees: 0,
            Metrics: MetricsRendererVE.EmptyVE,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Message: "Renderer VisionEngine detenido.",
            LastError: null);
}
