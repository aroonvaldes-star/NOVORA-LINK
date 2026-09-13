using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Performance;

namespace NOVORA.STEngine.Core;

public sealed record SnapshotCoreST(
    DateTimeOffset CapturedAtUtc,
    SnapshotMetricsVE Vision,
    SnapshotPerformanceVE Performance,
    StateCoreST State,
    bool ShouldReduceNonCriticalWork,
    string Summary,
    IReadOnlyList<string> Observations)
{
    public static SnapshotCoreST EmptyST()
    {
        SnapshotMetricsVE metrics =
            SnapshotMetricsVE.EmptyVE();

        return new(
            DateTimeOffset.UtcNow,
            metrics,
            new SnapshotPerformanceVE(
                DateTimeOffset.UtcNow,
                CongestionPerformanceVE.Healthy,
                4_000_000,
                false,
                false,
                "STEngine sin medición."),
            StateCoreST.Watch,
            false,
            "STEngine esperando datos de VisionEngine.",
            Array.Empty<string>());
    }
}
