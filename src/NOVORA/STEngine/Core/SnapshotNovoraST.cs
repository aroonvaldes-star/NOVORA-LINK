using NOVORA.LinkEngine.Metrics;

namespace NOVORA.STEngine.Core;

public sealed record SnapshotNovoraST(
    DateTimeOffset CapturedAtUtc,
    SnapshotCoreST Vision,
    IReadOnlyList<DeviceMetricsSnapshotLE> LinkDevices,
    StateCoreST State,
    bool ShouldReduceNonCriticalWork,
    string Summary,
    IReadOnlyList<string> Observations)
{
    public int LinkDeviceCount => LinkDevices.Count;

    public static SnapshotNovoraST EmptyST()
    {
        SnapshotCoreST vision =
            SnapshotCoreST.EmptyST();

        return new SnapshotNovoraST(
            DateTimeOffset.UtcNow,
            vision,
            Array.Empty<DeviceMetricsSnapshotLE>(),
            StateCoreST.Watch,
            false,
            "STEngine esperando datos de NOVORA.",
            Array.Empty<string>());
    }
}
