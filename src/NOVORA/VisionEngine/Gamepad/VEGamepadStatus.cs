namespace NOVORA.VisionEngine.Gamepad;

public sealed record VEGamepadStatus(
    VEGamepadStates State,
    int ConnectedGamepads,
    long ReportsSent,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public static VEGamepadStatus CreateInitialVE()
        => new(VEGamepadStates.Stopped, 0, 0, DateTimeOffset.UtcNow, "Gamepad VisionEngine detenido.", null);
}
