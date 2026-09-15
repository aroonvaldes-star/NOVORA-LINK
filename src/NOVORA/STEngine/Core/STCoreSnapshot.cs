using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Performance;

namespace NOVORA.STEngine.Core;

public sealed record STCoreSnapshot(
    DateTimeOffset CapturedAtUtc,
    VEMetricsSnapshot Vision,
    VEPerformanceSnapshot Performance,
    STCoreState State,
    bool ShouldReduceNonCriticalWork,
    string Summary,
    IReadOnlyList<string> Observations)
{
    public static STCoreSnapshot EmptyST()
    {
        VEMetricsSnapshot metrics =
            VEMetricsSnapshot.EmptyVE();

        return new(
            DateTimeOffset.UtcNow,
            metrics,
            new VEPerformanceSnapshot(
                DateTimeOffset.UtcNow,
                VEPerformanceCongestion.Healthy,
                4_000_000,
                false,
                false,
                "STEngine sin medición."),
            STCoreState.Watch,
            false,
            "STEngine esperando datos de VisionEngine.",
            Array.Empty<string>());
    }
}
