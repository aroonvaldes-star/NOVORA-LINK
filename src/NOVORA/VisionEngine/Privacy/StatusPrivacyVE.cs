namespace NOVORA.VisionEngine.Privacy;

/// <summary>
/// Estado público de PrivacyVE.
///
/// IsProtectedVE conserva el contrato de Privacy/Integration V1.
/// IsProtected conserva compatibilidad con consumidores nuevos.
/// </summary>
public sealed record StatusPrivacyVE(
    StatesPrivacyVE State,
    ClassificationPrivacyVE Classification,
    DateTimeOffset UpdatedAtUtc,
    string Message)
{
    public bool IsProtectedVE =>
        State ==
        StatesPrivacyVE.Protected;

    public bool IsProtected =>
        IsProtectedVE;

    public static StatusPrivacyVE CreateInitialVE()
        => new(
            StatesPrivacyVE.Normal,
            ClassificationPrivacyVE.Normal,
            DateTimeOffset.UtcNow,
            "PrivacyVE listo.");

    public static StatusPrivacyVE NormalVE()
        => CreateInitialVE();

    public static StatusPrivacyVE ProtectedVE(
        ClassificationPrivacyVE classification,
        string message)
        => new(
            StatesPrivacyVE.Protected,
            classification,
            DateTimeOffset.UtcNow,
            message);
}