namespace NOVORA.VisionEngine.Metrics;

public sealed record VEMetricsSnapshot(
    DateTimeOffset CapturedAtUtc,
    VEMetricsVideo Video,
    VEMetricsAudio Audio,
    VEMetricsControl Control,
    VEMetricsTransport Transport,
    long WorkingSetBytes,
    double ProcessCpuPercent,
    bool RendererEnabled)
{
    public static VEMetricsSnapshot EmptyVE()
        => new(
            DateTimeOffset.UtcNow,
            VEMetricsVideo.EmptyVE(),
            VEMetricsAudio.EmptyVE(),
            VEMetricsControl.EmptyVE(),
            VEMetricsTransport.EmptyVE(),
            0,
            0,
            false);
}
