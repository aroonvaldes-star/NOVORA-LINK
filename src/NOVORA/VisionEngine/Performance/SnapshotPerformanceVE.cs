namespace NOVORA.VisionEngine.Performance;

public sealed record SnapshotPerformanceVE(
    DateTimeOffset EvaluatedAtUtc,
    CongestionPerformanceVE Congestion,
    int RecommendedVideoBitrate,
    bool ShouldReduceTelemetry,
    bool RendererEnabled,
    string Reason);
