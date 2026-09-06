namespace NOVORA.VisionEngine.Metrics;

public sealed record SnapshotMetricsVE(
    DateTimeOffset CapturedAtUtc,
    VideoMetricsVE Video,
    AudioMetricsVE Audio,
    ControlMetricsVE Control,
    TransportMetricsVE Transport,
    long WorkingSetBytes,
    double ProcessCpuPercent,
    bool RendererEnabled)
{
    public static SnapshotMetricsVE EmptyVE()
        => new(
            DateTimeOffset.UtcNow,
            VideoMetricsVE.EmptyVE(),
            AudioMetricsVE.EmptyVE(),
            ControlMetricsVE.EmptyVE(),
            TransportMetricsVE.EmptyVE(),
            0,
            0,
            false);
}
