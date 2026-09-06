using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Core;
using NOVORA.Services;

namespace NOVORA.LinkEngine.Device;

/// <summary>
/// Resultado del provisioning del componente Android de LinkEngine.
/// </summary>
public sealed record ProvisioningResultLE(
    bool Success,
    bool Installed,
    bool Launched,
    bool AlreadyInstalled,
    string Message,
    Exception? Exception = null)
{
    public bool IsInstalled =>
        Installed;

    public bool IsLaunched =>
        Launched;

    public bool WasAlreadyInstalled =>
        AlreadyInstalled;

    public bool InstalledNow =>
        Installed &&
        !AlreadyInstalled;

    public bool WasInstalled =>
        Installed;

    public bool WasLaunched =>
        Launched;

    public bool PackageInstalled =>
        Installed;

    public bool ApplicationLaunched =>
        Launched;

    public static ProvisioningResultLE Ok(
        bool installed,
        bool launched,
        bool alreadyInstalled,
        string message)
    {
        return new ProvisioningResultLE(
            Success: true,
            Installed: installed,
            Launched: launched,
            AlreadyInstalled: alreadyInstalled,
            Message: message,
            Exception: null);
    }

    public static ProvisioningResultLE Fail(
        string message,
        bool installed = false,
        bool launched = false,
        bool alreadyInstalled = false,
        Exception? exception = null)
    {
        return new ProvisioningResultLE(
            Success: false,
            Installed: installed,
            Launched: launched,
            AlreadyInstalled: alreadyInstalled,
            Message: message,
            Exception: exception);
    }
}

/// <summary>
/// Provisioning del componente Android de NOVORA LinkEngine.
///
/// Responsabilidades:
///
/// 1. Localizar el APK incluido con NOVORA.
/// 2. Comprobar si com.novora.linkengine está instalado.
/// 3. Instalarlo automáticamente si falta.
/// 4. Verificar la instalación real mediante PackageManager.
/// 5. Abrir la aplicación Android.
///
/// La autorización inicial de VpnService sigue perteneciendo
/// al usuario y a Android.
/// </summary>
public sealed class ProvisioningDeviceLE
{
    public const string PackageNameLE =
        "com.novora.linkengine";

    public const string ApkFileNameLE =
        "com.novora.linkengine-Signed.apk";

    private readonly AdbService _adb;

    public ProvisioningDeviceLE(
        AdbService adb)
    {
        _adb =
            adb ??
            throw new ArgumentNullException(
                nameof(adb));
    }

    /// <summary>
    /// Devuelve la ruta del APK distribuido con NOVORA.
    /// </summary>
    public string GetApkPathLE()
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            "Tools",
            ApkFileNameLE);
    }

    /// <summary>
    /// Comprueba físicamente que NOVORA tenga disponible
    /// el APK que debe instalar.
    /// </summary>
    public bool ApkExistsLE()
    {
        return File.Exists(
            GetApkPathLE());
    }

    /// <summary>
    /// Comprueba mediante Android PackageManager si el
    /// componente LinkEngine está instalado.
    /// </summary>
    public async Task<bool> IsInstalledAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ValidateSerialLE(
            serial);

        serial =
            serial.Trim();

        try
        {
            string output =
                await _adb
                    .ShellAsync(
                        serial,
                        $"pm path {PackageNameLE}",
                        cancellationToken)
                    .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(
                    output))
            {
                return false;
            }

            return output.Contains(
                "package:",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            /*
             * Si PackageManager no encuentra el paquete,
             * ADB puede terminar con error.
             *
             * Para provisioning solamente significa:
             *
             * instalado = false
             */
            return false;
        }
    }

    /// <summary>
    /// Instala o actualiza el APK Android.
    /// </summary>
    public async Task<ResultCoreLE> InstallAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ValidateSerialLE(
            serial);

        serial =
            serial.Trim();

        string apkPath =
            GetApkPathLE();

        if (!File.Exists(
                apkPath))
        {
            return ResultCoreLE.Fail(
                "No se encontró el APK firmado de NOVORA LinkEngine." +
                Environment.NewLine +
                Environment.NewLine +
                apkPath);
        }

        try
        {
            string installOutput =
                await _adb
                    .InstallAsync(
                        serial,
                        apkPath,
                        cancellationToken)
                    .ConfigureAwait(false);

            /*
             * No confiamos únicamente en la salida de adb install.
             *
             * Confirmamos que PackageManager tenga registrado
             * com.novora.linkengine.
             */

            bool installed =
                await IsInstalledAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!installed)
            {
                return ResultCoreLE.Fail(
                    "ADB ejecutó la instalación pero Android no reportó " +
                    $"{PackageNameLE} como instalado." +
                    Environment.NewLine +
                    Environment.NewLine +
                    $"ADB: {NormalizeOutputLE(installOutput)}");
            }

            return ResultCoreLE.Ok(
                "NOVORA LinkEngine Android instalado correctamente.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ResultCoreLE.Fail(
                "No fue posible instalar NOVORA LinkEngine Android. " +
                ex.Message,
                ex);
        }
    }

    /// <summary>
    /// Comprueba si LinkEngine está instalado.
    /// Si falta, instala el APK.
    /// </summary>
    public async Task<ResultCoreLE> EnsureInstalledAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ValidateSerialLE(
            serial);

        serial =
            serial.Trim();

        bool installed =
            await IsInstalledAsync(
                    serial,
                    cancellationToken)
                .ConfigureAwait(false);

        if (installed)
        {
            return ResultCoreLE.Ok(
                "NOVORA LinkEngine Android ya está instalado.");
        }

        return await InstallAsync(
                serial,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Abre la Activity MAIN/LAUNCHER de com.novora.linkengine.
    ///
    /// No hardcodeamos el nombre Java generado por .NET Android.
    /// Actualmente puede verse como:
    ///
    /// crc64...MainActivity
    ///
    /// pero ese nombre puede cambiar entre builds.
    ///
    /// "monkey" localiza la Activity launcher declarada
    /// en el APK.
    /// </summary>
    public async Task<ResultCoreLE> LaunchAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ValidateSerialLE(
            serial);

        serial =
            serial.Trim();

        try
        {
            bool installed =
                await IsInstalledAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!installed)
            {
                return ResultCoreLE.Fail(
                    "NOVORA LinkEngine Android no está instalado.");
            }

            string command =
                $"monkey -p {PackageNameLE} " +
                "-c android.intent.category.LAUNCHER 1";

            string output =
                await _adb
                    .ShellAsync(
                        serial,
                        command,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (ContainsLaunchFailureLE(
                    output))
            {
                return ResultCoreLE.Fail(
                    "Android encontró NOVORA LinkEngine pero no pudo " +
                    "abrir su Activity principal." +
                    Environment.NewLine +
                    Environment.NewLine +
                    NormalizeOutputLE(output));
            }

            return ResultCoreLE.Ok(
                "NOVORA LinkEngine Android abierto correctamente.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ResultCoreLE.Fail(
                "No fue posible abrir NOVORA LinkEngine Android. " +
                ex.Message,
                ex);
        }
    }


    /// <summary>
    /// Detiene de forma explícita el VpnService de LinkEngine en Android.
    ///
    /// Esto es importante porque detener únicamente el runtime de Windows
    /// no desmonta por sí mismo la VPN Android. Si la VPN queda viva, Android
    /// mantiene la ruta 0.0.0.0/0 dentro de LinkEngine y el usuario puede
    /// quedarse sin salida normal por Wi-Fi aunque NOVORA ya esté detenido.
    /// </summary>
    public async Task<ResultCoreLE> StopVpnAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ValidateSerialLE(
            serial);

        serial =
            serial.Trim();

        const string serviceComponentLE =
            "com.novora.linkengine/com.novora.linkengine.VpnNetworkLE";

        const string stopActionLE =
            "com.novora.linkengine.action.STOP_VPN";

        try
        {
            string command =
                $"am startservice -n {serviceComponentLE} -a {stopActionLE}";

            string output =
                await _adb
                    .ShellAsync(
                        serial,
                        command,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (ContainsServiceFailureLE(
                    output))
            {
                /*
                 * Fallback defensivo:
                 *
                 * si Android rechaza el Intent de STOP, force-stop desmonta
                 * el VpnService y devuelve el routing normal al dispositivo.
                 */
                string fallbackOutput =
                    await _adb
                        .ShellAsync(
                            serial,
                            $"am force-stop {PackageNameLE}",
                            cancellationToken)
                        .ConfigureAwait(false);

                if (ContainsServiceFailureLE(
                        fallbackOutput))
                {
                    return ResultCoreLE.Fail(
                        "No fue posible desmontar la VPN Android de LinkEngine." +
                        Environment.NewLine +
                        Environment.NewLine +
                        $"STOP: {NormalizeOutputLE(output)}" +
                        Environment.NewLine +
                        $"FALLBACK: {NormalizeOutputLE(fallbackOutput)}");
                }
            }

            /*
             * Damos tiempo a VpnService para cerrar el descriptor TUN antes
             * de que Windows termine de desmontar sus puertos/reverse.
             */
            await Task.Delay(
                    300,
                    cancellationToken)
                .ConfigureAwait(false);

            return ResultCoreLE.Ok(
                "VPN Android de LinkEngine detenida correctamente.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ResultCoreLE.Fail(
                "No fue posible detener la VPN Android de LinkEngine. " +
                ex.Message,
                ex);
        }
    }

    private static bool ContainsServiceFailureLE(
        string? output)
    {
        if (string.IsNullOrWhiteSpace(
                output))
        {
            return false;
        }

        return
            output.Contains(
                "Error:",
                StringComparison.OrdinalIgnoreCase) ||
            output.Contains(
                "Exception",
                StringComparison.OrdinalIgnoreCase) ||
            output.Contains(
                "not found",
                StringComparison.OrdinalIgnoreCase) ||
            output.Contains(
                "does not exist",
                StringComparison.OrdinalIgnoreCase) ||
            output.Contains(
                "unable to start service",
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// API utilizada actualmente por MainWindow.EngineCoreLE.
    ///
    /// Flujo:
    ///
    /// ADB
    ///  ↓
    /// comprobar paquete
    ///  ↓
    /// instalar si falta
    ///  ↓
    /// verificar paquete
    ///  ↓
    /// abrir aplicación
    /// </summary>
    public async Task<ProvisioningResultLE>
        EnsureInstalledAndLaunchAsync(
            string serial,
            CancellationToken cancellationToken = default)
    {
        ValidateSerialLE(
            serial);

        serial =
            serial.Trim();

        try
        {
            /*
             * ====================================================
             * 1. ESTADO ORIGINAL
             * ====================================================
             */

            bool alreadyInstalled =
                await IsInstalledAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

            /*
             * ====================================================
             * 2. INSTALACIÓN
             * ====================================================
             */

            if (!alreadyInstalled)
            {
                ResultCoreLE install =
                    await InstallAsync(
                            serial,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (!install.Success)
                {
                    return ProvisioningResultLE.Fail(
                        message:
                            install.Message,
                        installed:
                            false,
                        launched:
                            false,
                        alreadyInstalled:
                            false,
                        exception:
                            install.Exception);
                }
            }

            /*
             * ====================================================
             * 3. VERIFICACIÓN REAL
             * ====================================================
             */

            bool installedAfterProvisioning =
                await IsInstalledAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!installedAfterProvisioning)
            {
                return ProvisioningResultLE.Fail(
                    message:
                        "NOVORA LinkEngine no aparece instalado después " +
                        "del proceso de provisioning.",
                    installed:
                        false,
                    launched:
                        false,
                    alreadyInstalled:
                        alreadyInstalled);
            }

            /*
             * ====================================================
             * 4. LAUNCH
             * ====================================================
             */

            ResultCoreLE launch =
                await LaunchAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!launch.Success)
            {
                return ProvisioningResultLE.Fail(
                    message:
                        launch.Message,
                    installed:
                        true,
                    launched:
                        false,
                    alreadyInstalled:
                        alreadyInstalled,
                    exception:
                        launch.Exception);
            }

            /*
             * ====================================================
             * 5. OK
             * ====================================================
             */

            string message =
                alreadyInstalled
                    ? "NOVORA LinkEngine Android ya estaba instalado y fue abierto correctamente."
                    : "NOVORA LinkEngine Android fue instalado y abierto correctamente.";

            return ProvisioningResultLE.Ok(
                installed:
                    true,
                launched:
                    true,
                alreadyInstalled:
                    alreadyInstalled,
                message:
                    message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProvisioningResultLE.Fail(
                message:
                    "Falló el provisioning de NOVORA LinkEngine Android. " +
                    ex.Message,
                installed:
                    false,
                launched:
                    false,
                alreadyInstalled:
                    false,
                exception:
                    ex);
        }
    }

    /// <summary>
    /// API de compatibilidad para componentes nuevos de LinkEngine.
    /// </summary>
    public async Task<ResultCoreLE> EnsureReadyAsync(
        string serial,
        bool launchApplication = true,
        CancellationToken cancellationToken = default)
    {
        ValidateSerialLE(
            serial);

        serial =
            serial.Trim();

        if (!launchApplication)
        {
            return await EnsureInstalledAsync(
                    serial,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        ProvisioningResultLE provisioning =
            await EnsureInstalledAndLaunchAsync(
                    serial,
                    cancellationToken)
                .ConfigureAwait(false);

        if (provisioning.Success)
        {
            return ResultCoreLE.Ok(
                provisioning.Message);
        }

        return ResultCoreLE.Fail(
            provisioning.Message,
            provisioning.Exception);
    }

    private static void ValidateSerialLE(
        string serial)
    {
        if (string.IsNullOrWhiteSpace(
                serial))
        {
            throw new ArgumentException(
                "El serial ADB es obligatorio.",
                nameof(serial));
        }
    }

    private static bool ContainsLaunchFailureLE(
        string? output)
    {
        if (string.IsNullOrWhiteSpace(
                output))
        {
            return false;
        }

        return
            output.Contains(
                "No activities found",
                StringComparison.OrdinalIgnoreCase) ||

            output.Contains(
                "monkey aborted",
                StringComparison.OrdinalIgnoreCase) ||

            output.Contains(
                "unable to resolve",
                StringComparison.OrdinalIgnoreCase) ||

            output.Contains(
                "error:",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeOutputLE(
        string? output)
    {
        if (string.IsNullOrWhiteSpace(
                output))
        {
            return string.Empty;
        }

        return output
            .Replace(
                "\r",
                " ",
                StringComparison.Ordinal)
            .Replace(
                "\n",
                " ",
                StringComparison.Ordinal)
            .Trim();
    }
}