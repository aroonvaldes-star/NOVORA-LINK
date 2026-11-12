namespace NOVORA.VisionEngine.Privacy;

/// <summary>
/// Administrador de paquetes sensibles.
/// No descubre aplicaciones mediante Accessibility.
/// </summary>
public sealed class VEPrivacySensitiveApp
{
    private readonly VEPrivacyManager
        _privacyVE;

    public VEPrivacySensitiveApp(
        VEPrivacyManager privacy)
    {
        _privacyVE =
            privacy ??
            throw new ArgumentNullException(
                nameof(privacy));
    }

    public void SetPackagesVE(
        IEnumerable<string> packageNames)
        =>
            _privacyVE
                .SetSensitivePackagesVE(
                    packageNames);

    public bool IsSensitiveVE(
        string? packageName)
        =>
            _privacyVE
                .PolicyVE
                .IsSensitivePackageVE(
                    packageName);
}