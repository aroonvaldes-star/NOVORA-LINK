using NOVORA.Contracts.Stability;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Runtime;

namespace NOVORA.LinkEngine.Metrics;

public static class LEStabilitySnapshotAdapter
{
    public static IReadOnlyList<NLStabilityLinkSnapshot> CaptureLE(LERuntimeManager? runtime)
        => runtime?.EngineLE?.Metrics.GetAllSnapshots().Select(MapLE).ToArray()
            ?? Array.Empty<NLStabilityLinkSnapshot>();

    private static NLStabilityLinkSnapshot MapLE(LEMetricsDeviceMetricsSnapshot snapshot)
        => new(snapshot.Serial, snapshot.State switch
        {
            LECoreStates.Connecting => NLStabilityEngineState.Starting,
            LECoreStates.Online => NLStabilityEngineState.Running,
            LECoreStates.Degraded => NLStabilityEngineState.Degraded,
            LECoreStates.Recovering => NLStabilityEngineState.Recovering,
            LECoreStates.Failed => NLStabilityEngineState.Failed,
            _ => NLStabilityEngineState.Stopped
        }, snapshot.Healthy, snapshot.LatencyMs, snapshot.DnsFailures,
            snapshot.RecoveryAttempts, snapshot.SuccessfulRecoveries);
}
