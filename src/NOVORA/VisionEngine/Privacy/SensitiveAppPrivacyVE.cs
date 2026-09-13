namespace NOVORA.VisionEngine.Privacy;

/// <summary>
/// Administrador de paquetes sensibles.
/// No descubre aplicaciones mediante Accessibility.
/// </summary>
public sealed class SensitiveAppPrivacyVE
{
    private readonly ManagerPrivacyVE
        _privacyVE;

    public SensitiveAppPrivacyVE(
        ManagerPrivacyVE privacy)
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