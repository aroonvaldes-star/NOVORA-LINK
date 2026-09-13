namespace NOVORA.VisionEngine.Privacy;

/// <summary>
/// Identidad EFÍMERA de la sesión PrivacyVE actual.
///
/// Nunca se persiste por esta clase.
/// </summary>
public sealed record SessionPrivacyVE(
    Guid SessionIdVE,
    DateTimeOffset StartedAtUtcVE)
{
    public static SessionPrivacyVE CreateVE()
        => new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
}