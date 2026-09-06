namespace NOVORA.VisionEngine.Control;

public sealed record StatusControlVE(
    StatesControlVE State,
    StatsControlVE Stats,
    DateTimeOffset UpdatedAtUtc,
    string Message,
    string? LastError)
{
    public static StatusControlVE CreateInitialVE()
        => new(
            StatesControlVE.Stopped,
            new StatsControlVE(0, 0, 0, 0, 0, 0, 0),
            DateTimeOffset.UtcNow,
            "Control VisionEngine detenido.",
            null);
}
