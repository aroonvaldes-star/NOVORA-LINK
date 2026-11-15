namespace NOVORA.VisionEngine.Control;

public sealed record VEControlStatus(
    VEControlStates State,
    VEControlStats Stats,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public static VEControlStatus CreateInitialVE()
        => new(
            VEControlStates.Stopped,
            new VEControlStats(0, 0, 0, 0, 0, 0, 0),
            DateTimeOffset.UtcNow,
            "Control VisionEngine detenido.",
            null);
}
