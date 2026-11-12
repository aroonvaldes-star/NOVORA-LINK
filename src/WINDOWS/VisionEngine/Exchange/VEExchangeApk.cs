using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Instalación de APK soltados o pegados sobre VisionEngine.
/// </summary>
internal static class VEExchangeApk
{
    public static async Task InstallAsync(
        string serial,
        string apkPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            serial);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            apkPath);

        string fullPath =
            Path.GetFullPath(
                apkPath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "El APK ya no existe.",
                fullPath);
        }

        if (
            !Path.GetExtension(fullPath)
                .Equals(
                    ".apk",
                    StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "VEExchangeApk sólo acepta archivos .apk.");
        }

        VEExchangeResultADB result =
            await VEExchangeADB
                .RunAsync(
                    serial,
                    cancellationToken,
                    "install",
                    "-r",
                    fullPath)
                .ConfigureAwait(false);

        if (!result.SuccessVE)
        {
            string error =
                string.IsNullOrWhiteSpace(
                    result.ErrorVE)
                    ? result.OutputVE
                    : result.ErrorVE;

            throw new InvalidOperationException(
                $"No se pudo instalar el APK: {error.Trim()}");
        }
    }
}
