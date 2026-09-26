namespace NOVORA.Contracts.Stability;

public enum NLStabilityEngineState { Stopped, Starting, Running, Degraded, Recovering, Failed }
public enum NLStabilitySeverity { Healthy, Watch, Degraded, Critical }

public sealed record NLStabilityVisionSnapshot(
    DateTimeOffset CapturedAtUtc,
    bool IsRunning,
    bool IsStreaming,
    long FramesDecoded,
    double FramesPerSecond,
    long VideoDecodeErrors,
    long AudioDecodeErrors,
    long AudioPlaybackErrors,
    long ControlErrors,
    bool VideoConnected,
    long WorkingSetBytes,
    double ProcessCpuPercent,
    bool RendererEnabled,
    NLStabilitySeverity PerformanceSeverity,
    int RecommendedVideoBitrate,
    bool ShouldReduceTelemetry,
    string PerformanceReason)
{
    public static NLStabilityVisionSnapshot Empty()
        => new(DateTimeOffset.UtcNow, false, false, 0, 0, 0, 0, 0, 0, false, 0, 0, false,
            NLStabilitySeverity.Healthy, 4_000_000, false, "Sin medicion de VisionEngine.");
}

public sealed record NLStabilityLinkSnapshot(
    string Serial,
    NLStabilityEngineState State,
    bool Healthy,
    double LatencyMs,
    int DnsFailures,
    int RecoveryAttempts,
    int SuccessfulRecoveries);

public sealed record NLStabilityExInSnapshot(
    NLStabilityEngineState State,
    int ConnectedGamepads,
    string? LastError,
    bool HasUncorrectableCalibration,
    bool HasLowBattery);
