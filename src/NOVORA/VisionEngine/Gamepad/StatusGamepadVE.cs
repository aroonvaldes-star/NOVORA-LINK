namespace NOVORA.VisionEngine.Gamepad;

public sealed record StatusGamepadVE(
    StatesGamepadVE State,
    int ConnectedGamepads,
    long ReportsSent,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public static StatusGamepadVE CreateInitialVE()
        => new(StatesGamepadVE.Stopped, 0, 0, DateTimeOffset.UtcNow, "Gamepad VisionEngine detenido.", null);
}
