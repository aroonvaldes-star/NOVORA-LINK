using NOVORA.LinkEngine.Metrics;

namespace NOVORA.STEngine.Core;

public sealed record STCoreSnapshotNovora(
    DateTimeOffset CapturedAtUtc,
    STCoreSnapshot Vision,
    IReadOnlyList<LEMetricsDeviceMetricsSnapshot> LinkDevices,
    STCoreState State,
    bool ShouldReduceNonCriticalWork,
    string Summary,
    IReadOnlyList<string> Observations)
{
    public int LinkDeviceCount => LinkDevices.Count;

    public static STCoreSnapshotNovora EmptyST()
    {
        STCoreSnapshot vision =
            STCoreSnapshot.EmptyST();

        return new STCoreSnapshotNovora(
            DateTimeOffset.UtcNow,
            vision,
            Array.Empty<LEMetricsDeviceMetricsSnapshot>(),
            STCoreState.Watch,
            false,
            "STEngine esperando datos de NOVORA.",
            Array.Empty<string>());
    }
}
