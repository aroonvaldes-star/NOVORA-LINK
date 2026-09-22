namespace NOVORA.ExInEngine;

public sealed record ExInStatus(
    ExInStates State,
    int ConnectedGamepads,
    long ReportsSent,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError,
    IReadOnlyList<ExInDiagnosticStatus> Diagnostics,
    IReadOnlyList<ExInBatteryStatus> Batteries)
{
    public static ExInStatus CreateInitialVE()
        => new(ExInStates.Stopped, 0, 0, DateTimeOffset.UtcNow, "ExInEngine detenido.", null, [], []);
}

public sealed record ExInLiveSnapshot(
    ExInDevice? Device,
    string DeviceName,
    ExInState State,
    ExInState CorrectedState,
    bool Calibrating,
    bool Calibrated,
    double Deadzone,
    string Message,
    ExInCalibrationDetails? Calibration,
    string? SdlMapping,
    string TranslationTrace);

public sealed record ExInCalibrationDetails(
    string ProfileId,
    string ConnectionType,
    ExInAxisDeadzone Deadzone,
    ExInState Center,
    ExInState Minimum,
    ExInState Maximum);
