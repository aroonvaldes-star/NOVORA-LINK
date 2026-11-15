namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Traduce el estado de PackageInstaller de Android al resultado Exchange.
/// Solo SUCCESS cuenta como instalado; el resto se distingue para la UI.
/// </summary>
public static class VEExchangeApkInstallMapper
{
    private const int StatusSuccess = 0;
    private const int StatusPendingUserAction = -1;
    private const int StatusFailureAborted = 3;

    public static VEExchangeApkInstallResult FromPackageInstallerStatus(int status, string? message)
    {
        return status switch
        {
            StatusSuccess => new(VEExchangeApkInstallState.Confirmed, "APK instalado por Android."),
            StatusPendingUserAction => new(VEExchangeApkInstallState.Pending, "Android espera tu confirmación para instalar el APK."),
            StatusFailureAborted => new(VEExchangeApkInstallState.Denied, "La instalación fue cancelada o denegada en Android."),
            _ => new(VEExchangeApkInstallState.Failed, string.IsNullOrWhiteSpace(message)
                ? "Android no pudo instalar el APK."
                : "Android no pudo instalar el APK: " + message)
        };
    }
}
