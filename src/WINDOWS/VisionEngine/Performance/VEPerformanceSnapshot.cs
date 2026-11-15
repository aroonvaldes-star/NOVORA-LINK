namespace NOVORA.VisionEngine.Performance;

public sealed record VEPerformanceSnapshot(
    DateTimeOffset EvaluatedAtUtc,
    VEPerformanceCongestion Congestion,
    int RecommendedVideoBitrate,
    bool ShouldReduceTelemetry,
    bool RendererEnabled,
    string Reason);
