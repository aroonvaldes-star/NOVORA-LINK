using NOVORA.Contracts.Stability;

namespace NOVORA.ExInEngine;

public static class ExInStabilitySnapshotAdapter
{
    public static NLStabilityExInSnapshot? CaptureExIn(ExInStatus? status)
    {
        if (status is null) return null;
        return new NLStabilityExInSnapshot(
            status.State switch
            {
                ExInStates.Ready => NLStabilityEngineState.Starting,
                ExInStates.Running => NLStabilityEngineState.Running,
                ExInStates.Failed => NLStabilityEngineState.Failed,
                _ => NLStabilityEngineState.Stopped
            },
            status.ConnectedGamepads,
            status.LastError,
            status.Diagnostics.Any(value => !value.CalibrationCanCorrect),
            status.Batteries.Any(value => value.State == ExInBatteryState.OnBattery && value.Percent is >= 0 and <= 15));
    }
}
