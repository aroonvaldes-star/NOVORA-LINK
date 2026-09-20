namespace NOVORA.VisionEngine.Renderer;

public sealed record VERendererMetrics(
    long FramesQueued,
    long FramesPresented,
    long FramesDropped,
    long RenderErrors,
    int QueueDepth,
    double LastFrameAgeMs,
    double LastRenderLatencyMs)
{
    public static VERendererMetrics EmptyVE { get; } =
        new(0, 0, 0, 0, 0, 0, 0);
}
