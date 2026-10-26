namespace NOVORA.ExInEngine;

public enum ExInHealth
{
    Healthy,
    CorrectableByCalibration,
    CalibrationIncomplete,
    ReviewRecommended,
    ProbableHardwareFault
}

public enum ExInDiagnosticPhase
{
    Rest,
    Travel,
    Complete
}

public sealed record ExInDiagnosticStatus(
    string ProfileKey,
    ExInHealth Classification,
    string[] AffectedControls,
    ExInState RawState,
    ExInState CorrectedState,
    int Severity,
    string Explanation,
    bool CalibrationCanCorrect,
    DateTimeOffset EvaluatedAtUtc)
{
    public ExInDiagnosticStatus ProtectVE()
        => this with { RawState = default, CorrectedState = default };
}
