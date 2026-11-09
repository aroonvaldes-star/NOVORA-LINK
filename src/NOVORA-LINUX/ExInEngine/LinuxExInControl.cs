namespace NOVORA.Linux.ExIn;

// Same names, order and defaults as Windows NLControlExIn* so LINK Control reads the Linux
// snapshot exactly as it reads the Windows one (System.Text.Json, PascalCase).
internal sealed record LinuxControlExInTelemetry(long Sequence, DateTimeOffset SampledAtUtc,
    double PollingHz, double PcProcessingMs, double JitterMs);

internal sealed record LinuxControlExInCalibration(int LeftStick, int RightStick,
    int LeftTrigger, int RightTrigger, int Overall, bool Complete, string Phase);

internal sealed record LinuxControlExIn(bool Detected, string DeviceName, string VidPid,
    int LeftX, int LeftY, int RightX, int RightY, int LeftTrigger, int RightTrigger,
    string[] Buttons, bool Calibrating, bool Calibrated, double Deadzone, string Message,
    string Family = "Unknown", string Mode = "Game", bool Transitioning = false,
    bool CanSetMode = false, bool CanReactivate = false,
    int CorrectedLeftX = 0, int CorrectedLeftY = 0, int CorrectedRightX = 0, int CorrectedRightY = 0,
    int CorrectedLeftTrigger = 0, int CorrectedRightTrigger = 0,
    string Health = "Unknown", bool Correctable = false, string DiagnosticMessage = "",
    string BatteryState = "Unknown", int BatteryPercent = -1,
    string Identity = "", string ConnectionType = "Unknown",
    bool SupportsGamepad = true, bool SupportsPointer = false, bool SupportsTouchpad = false,
    bool SupportsNavigation = false, bool CanCalibrate = false, string CalibrationDetails = "",
    string BatteryAlert = "", long BatteryAlertSequence = 0,
    LinuxControlExInTelemetry? Telemetry = null, LinuxControlExInCalibration? CalibrationProgress = null);

/// <summary>ExIn engine row of ControlEngines (ExInState, ExInMessage, ExInCanStart).</summary>
internal sealed record LinuxExInEngineState(string State, string Message, bool CanStart);

/// <summary>Lets the control server publish ExIn and run exin.* actions without owning the engine.</summary>
internal interface ILinuxExInControl
{
    LinuxExInEngineState EngineState { get; }
    LinuxControlExIn? CaptureSnapshot();
    Task<(bool Success, string Message)> HandleAsync(string action, string? value, CancellationToken cancellationToken);
}
