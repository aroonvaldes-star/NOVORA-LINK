using NOVORA.Contracts.Stability;

namespace NOVORA.STEngine.Core;

public sealed record STCoreSnapshot(
    DateTimeOffset CapturedAtUtc,
    NLStabilityVisionSnapshot Vision,
    STCoreState State,
    bool ShouldReduceNonCriticalWork,
    string Summary,
    IReadOnlyList<string> Observations)
{
    public static STCoreSnapshot EmptyST()
        => new(DateTimeOffset.UtcNow, NLStabilityVisionSnapshot.Empty(), STCoreState.Watch, false,
            "STEngine esperando datos de VisionEngine.", Array.Empty<string>());
}
