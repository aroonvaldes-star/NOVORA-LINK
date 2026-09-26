using NOVORA.Contracts.Stability;

namespace NOVORA.STEngine.Core;

public sealed record STCoreSnapshotNovora(
    DateTimeOffset CapturedAtUtc,
    STCoreSnapshot Vision,
    IReadOnlyList<NLStabilityLinkSnapshot> LinkDevices,
    NLStabilityExInSnapshot? ExIn,
    STCoreState State,
    bool ShouldReduceNonCriticalWork,
    string Summary,
    IReadOnlyList<string> Observations)
{
    public int LinkDeviceCount => LinkDevices.Count;

    public static STCoreSnapshotNovora EmptyST()
        => new(DateTimeOffset.UtcNow, STCoreSnapshot.EmptyST(), Array.Empty<NLStabilityLinkSnapshot>(), null,
            STCoreState.Watch, false, "STEngine esperando datos de NOVORA.", Array.Empty<string>());
}
