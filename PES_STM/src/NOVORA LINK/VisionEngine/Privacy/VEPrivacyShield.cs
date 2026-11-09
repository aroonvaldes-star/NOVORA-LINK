namespace NOVORA.VisionEngine.Privacy;

public static class VEPrivacyShield
{
    public const string DefaultMessageVE =
        "Contenido protegido.`nCompleta esta operacion directamente en tu Android.";

    public static string GetMessageVE(
        VEPrivacyStatus status)
    {
        ArgumentNullException.ThrowIfNull(
            status);

        if (!status.IsProtectedVE)
        {
            return string.Empty;
        }

        return DefaultMessageVE;
    }
}