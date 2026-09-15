namespace NOVORA.VisionEngine.Privacy;

/// <summary>
/// Estado sensible EN MEMORIA de la sesión actual.
///
/// No mantiene historial.
/// No contiene passwords.
/// No contiene URLs.
/// No contiene contenido de formularios.
/// </summary>
public sealed record VEPrivacyContext(
    bool SystemSecure,
    bool SecureInput,
    string? ForegroundPackage,
    bool ManualShield)
{
    public static VEPrivacyContext CreateDefaultVE()
        => new(
            SystemSecure: false,
            SecureInput: false,
            ForegroundPackage: null,
            ManualShield: false);
}