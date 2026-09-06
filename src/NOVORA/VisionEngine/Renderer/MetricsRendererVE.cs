namespace NOVORA.VisionEngine.Renderer;

public sealed record MetricsRendererVE(
    long FramesQueued,
    long FramesPresented,
    long FramesDropped,
    long RenderErrors,
    int QueueDepth,
    double LastFrameAgeMs,
    double LastRenderLatencyMs)
{
    public static MetricsRendererVE EmptyVE { get; } =
        new(0, 0, 0, 0, 0, 0, 0);
}
