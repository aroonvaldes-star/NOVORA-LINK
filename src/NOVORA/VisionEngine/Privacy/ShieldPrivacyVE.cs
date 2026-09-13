namespace NOVORA.VisionEngine.Privacy;

public static class ShieldPrivacyVE
{
    public const string DefaultMessageVE =
        "Contenido protegido.`nCompleta esta operacion directamente en tu Android.";

    public static string GetMessageVE(
        StatusPrivacyVE status)
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