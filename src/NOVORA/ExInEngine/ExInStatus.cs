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
    string TranslationTrace,
    ExInCalibrationProgress? CalibrationProgress,
    long Sequence = 0,
    DateTimeOffset? SampledAtUtc = null,
    double PollingHz = 0,
    double PcProcessingMs = 0,
    double JitterMs = 0);

public sealed record ExInCalibrationProgress(
    int LeftStick,
    int RightStick,
    int LeftTrigger,
    int RightTrigger)
{
    public int Overall => Math.Min(Math.Min(LeftStick, RightStick), Math.Min(LeftTrigger, RightTrigger));
    public bool Complete => LeftStick >= 90 && RightStick >= 90 && LeftTrigger >= 90 && RightTrigger >= 90;
}

public sealed record ExInCalibrationDetails(
    string ProfileId,
    string ConnectionType,
    ExInAxisDeadzone Deadzone,
    ExInState Center,
    ExInState Minimum,
    ExInState Maximum);
