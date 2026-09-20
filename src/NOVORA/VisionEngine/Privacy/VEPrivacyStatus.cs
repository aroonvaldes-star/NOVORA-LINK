namespace NOVORA.VisionEngine.Privacy;

/// <summary>
/// Estado público de PrivacyVE.
///
/// IsProtectedVE conserva el contrato de Privacy/Integration V1.
/// IsProtected conserva compatibilidad con consumidores nuevos.
/// </summary>
public sealed record VEPrivacyStatus(
    VEPrivacyStates State,
    VEPrivacyClassification Classification,
    DateTimeOffset UpdatedAtUtc,
    string Message)
{
    public bool IsProtectedVE =>
        State ==
        VEPrivacyStates.Protected;

    public bool IsProtected =>
        IsProtectedVE;

    public static VEPrivacyStatus CreateInitialVE()
        => new(
            VEPrivacyStates.Normal,
            VEPrivacyClassification.Normal,
            DateTimeOffset.UtcNow,
            "PrivacyVE listo.");

    public static VEPrivacyStatus NormalVE()
        => CreateInitialVE();

    public static VEPrivacyStatus ProtectedVE(
        VEPrivacyClassification classification,
        string message)
        => new(
            VEPrivacyStates.Protected,
            classification,
            DateTimeOffset.UtcNow,
            message);
}