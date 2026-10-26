namespace NOVORA.VisionEngine.Renderer;

public sealed record VERendererMetrics(
    long FramesQueued,
    long FramesPresented,
    long FramesDropped,
    long RenderErrors,
    int QueueDepth,
    double LastFrameAgeMs,
    double LastRenderLatencyMs,
    bool DynamicPresentationEnabled = false,
    double TargetRefreshRateHz = 60,
    double PresentedFps = 0,
    double LastPresentIntervalMs = 0,
    double PresentationJitterMs = 0,
    long CompositionFallbacks = 0,
    double LastReceiveToPresentMs = 0,
    long LastPresentedSourcePtsUs = 0,
    long LastPresentedAtTimestamp = 0)
{
    public static VERendererMetrics EmptyVE { get; } =
        new(0, 0, 0, 0, 0, 0, 0);
}
