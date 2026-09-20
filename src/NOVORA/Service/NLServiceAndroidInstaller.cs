using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace NOVORA.Service;

public enum NLServiceAndroidInstallAction { Install, Update, Current }

public sealed record NLServiceAndroidInstallState(string Serial, int DeviceSdk,
    long? InstalledVersionCode, string? InstalledVersionName,
    NLServiceAndroidInstallAction Action, string Description);

/// <summary>Instalación explícita, sin desinstalar ni borrar datos. El ejecutor debe lanzar ante errores ADB.</summary>
public sealed class NLServiceAndroidInstaller(Func<string[], CancellationToken, Task<string>> executeAdb)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly TimeSpan ParseTimeout = TimeSpan.FromSeconds(1);

    public async Task<NLServiceAndroidInstallState> InspectAsync(string serial, NLServiceAndroidPackageInfo package,
        CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        if (package.PackageId != "com.novora.appcontrol" || package.VersionCode <= 0)
            throw new InvalidOperationException("El paquete no corresponde a NOVORA Android.");
        string devices = await Run(["devices", "-l"], cancellationToken);
        string[] rows = devices.Split('\n').Select(x => x.Trim()).Where(x => x.StartsWith(serial + " ", StringComparison.Ordinal)
            || x.StartsWith(serial + "\t", StringComparison.Ordinal)).ToArray();
        if (rows.Length != 1 || !Regex.IsMatch(rows[0], @"^" + Regex.Escape(serial) + @"\s+device(?:\s|$)", RegexOptions.CultureInvariant, ParseTimeout))
            throw new InvalidOperationException("Selecciona un teléfono conectado por USB y autoriza la depuración USB. No se instalará por Wi-Fi.");
        string usbSerial;
        try { usbSerial = (await Run(["-d", "get-serialno"], cancellationToken)).Trim(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Conecta un solo teléfono por USB y autoriza su depuración antes de instalar NOVORA.", ex);
        }
        if (usbSerial != serial)
            throw new InvalidOperationException("Conecta únicamente el teléfono seleccionado por USB. ADB no confirmó que ese sea el único dispositivo USB.");
        string state = (await Device(serial, ["get-state"], cancellationToken)).Trim();
        if (state != "device") throw new InvalidOperationException("El teléfono USB dejó de estar autorizado.");
        string sdkText = (await Device(serial, ["shell", "getprop", "ro.build.version.sdk"], cancellationToken)).Trim();
        if (!int.TryParse(sdkText, NumberStyles.None, CultureInfo.InvariantCulture, out int sdk) || sdk is < 1 or > 1000)
            throw new InvalidOperationException("No se pudo comprobar la versión de Android del teléfono.");
        if (sdk < package.MinSdk) throw new InvalidOperationException($"Esta app requiere Android API {package.MinSdk} o posterior; el teléfono informa API {sdk}.");
        string currentUser = (await Device(serial, ["shell", "am", "get-current-user"], cancellationToken)).Trim();
        if (currentUser != "0") throw new InvalidOperationException("Cambia al usuario principal del teléfono antes de instalar NOVORA. Esta entrega instala para el usuario 0.");
        string listed = (await Device(serial, ["shell", "pm", "list", "packages", "-u", "--user", "0", package.PackageId], cancellationToken)).Trim();
        string dump = await Device(serial, ["shell", "dumpsys", "package", package.PackageId], cancellationToken);
        long? installedCode = null;
        string? installedName = null;
        if (listed.Length == 0)
        {
            if (dump.Trim() != "Unable to find package: " + package.PackageId)
                throw new InvalidOperationException("Android no confirmó si NOVORA está instalada. No se hará ningún cambio.");
        }
        else
        {
            if (listed != "package:" + package.PackageId)
                throw new InvalidOperationException("La consulta de paquetes Android produjo una respuesta inesperada.");
            string path = (await Device(serial, ["shell", "pm", "path", "--user", "0", package.PackageId], cancellationToken)).Trim();
            if (!path.Split('\n').All(x => Regex.IsMatch(x.Trim(), @"^package:/[^\r\n]+\.apk$", RegexOptions.CultureInvariant, ParseTimeout)))
                throw new InvalidOperationException("NOVORA está registrada, pero Android no confirmó su instalación para el usuario principal. No se borrarán sus datos.");
            MatchCollection versions = Regex.Matches(dump, @"^[ \t]*versionCode=(\d+)(?:[ \t]+[^\r\n]*)?\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant, ParseTimeout);
            MatchCollection names = Regex.Matches(dump, @"^[ \t]*versionName=([^\r\n]+)\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant, ParseTimeout);
            var users = Regex.Matches(dump, @"^[ \t]*User 0:[ \t]*([^\r\n]*)", RegexOptions.Multiline | RegexOptions.CultureInvariant, ParseTimeout)
                .Cast<Match>()
                .Where(m => Regex.IsMatch(m.Groups[1].Value, @"(?:^|[ \t])installed=", RegexOptions.CultureInvariant, ParseTimeout))
                .ToList();
            if (versions.Count != 1 || names.Count != 1 || users.Count != 1
                || !long.TryParse(versions[0].Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long code) || code <= 0
                || !Regex.IsMatch(users[0].Groups[1].Value, @"(?:^|\s)installed=true(?:\s|$)", RegexOptions.CultureInvariant, ParseTimeout)
                || Regex.IsMatch(users[0].Groups[1].Value, @"(?:^|\s)(?:hidden|suspended)=true(?:\s|$)", RegexOptions.CultureInvariant, ParseTimeout)
                || Regex.IsMatch(users[0].Groups[1].Value, @"(?:^|\s)enabled=[234](?:\s|$)", RegexOptions.CultureInvariant, ParseTimeout))
                throw new InvalidOperationException("No se pudo comprobar una instalación activa de NOVORA para el usuario principal. Revisa su estado en Ajustes de Android; no se borrarán datos.");
            installedCode = code;
            installedName = names[0].Groups[1].Value.Trim();
            if (installedName.Length > 128) throw new InvalidOperationException("La versión instalada contiene información inválida.");
            if (code > package.VersionCode) throw new InvalidOperationException("El teléfono ya tiene una versión más reciente. NOVORA no permite instalar una versión anterior.");
        }
        var action = installedCode is null ? NLServiceAndroidInstallAction.Install
            : installedCode < package.VersionCode ? NLServiceAndroidInstallAction.Update : NLServiceAndroidInstallAction.Current;
        return new(serial, sdk, installedCode, installedName, action, action switch
        {
            NLServiceAndroidInstallAction.Install => $"Instalar NOVORA {package.VersionName} para el usuario principal.",
            NLServiceAndroidInstallAction.Update => $"Actualizar NOVORA {installedName} a {package.VersionName}, conservando sus datos.",
            _ => installedName == package.VersionName ? "La versión disponible ya está instalada." : "El código de versión ya está instalado, aunque su nombre difiere. No se reemplazará."
        });
    }

    public async Task<NLServiceAndroidInstallState> InstallAsync(string serial, NLServiceAndroidPackageInfo package,
        NLServiceAndroidInstallState expectedState, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // ADB puede leer el archivo; ningún escritor puede cambiarlo durante la instalación.
            using var lease = new FileStream(package.ApkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(lease, cancellationToken));
            if (!string.Equals(hash, package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("El APK cambió después de comprobarlo. Se canceló la instalación.");
            var before = await InspectAsync(serial, package, cancellationToken);
            if (before.Serial != expectedState.Serial || before.DeviceSdk != expectedState.DeviceSdk
                || before.InstalledVersionCode != expectedState.InstalledVersionCode
                || before.InstalledVersionName != expectedState.InstalledVersionName || before.Action != expectedState.Action)
                throw new InvalidOperationException("El teléfono o su versión cambió desde la confirmación. Vuelve a comprobarlo antes de instalar.");
            if (before.Action == NLServiceAndroidInstallAction.Current) return before;
            cancellationToken.ThrowIfCancellationRequested();
            string result;
            try
            {
                result = await Device(serial, ["install", "--user", "0", "-r", package.ApkPath], cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                throw new InvalidOperationException("Se canceló la espera mientras Android instalaba. El resultado es desconocido; vuelve a comprobar la versión. No se intentó desinstalar ni revertir.", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(InstallationError(ex.Message), ex);
            }
            if (!Regex.IsMatch(result, @"^Success\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant, ParseTimeout)
                || result.Contains("Failure", StringComparison.OrdinalIgnoreCase) || result.Contains("error:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(InstallationError(result));
            try
            {
                var after = await InspectAsync(serial, package, cancellationToken);
                if (after.InstalledVersionCode != package.VersionCode || after.InstalledVersionName != package.VersionName)
                    throw new InvalidOperationException("La versión leída no coincide con el paquete enviado.");
                return after;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Android informó instalación correcta, pero no se pudo verificar la versión final. Vuelve a comprobar el teléfono; no se realizó ninguna reversión.", ex);
            }
        }
        finally { _gate.Release(); }
    }

    private static string InstallationError(string output) => output.Contains("UPDATE_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase)
        || output.Contains("signatures do not match", StringComparison.OrdinalIgnoreCase)
        ? "Android rechazó la actualización porque la firma no coincide. Conserva la app instalada y sus datos; necesitas un APK firmado con la misma clave. NOVORA no la desinstalará."
        : "No se confirmó la instalación. Vuelve a comprobar la versión del teléfono. NOVORA no desinstaló la app ni borró sus datos.";

    private Task<string> Device(string serial, string[] arguments, CancellationToken ct) => Run(["-s", serial, .. arguments], ct);
    private async Task<string> Run(string[] arguments, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(arguments.Length > 2 && arguments[2] == "install" ? TimeSpan.FromMinutes(3) : TimeSpan.FromSeconds(15));
        string result = await executeAdb(arguments, timeout.Token);
        if (result is null || result.Length > 262144) throw new InvalidOperationException("La respuesta de ADB no es válida o excede el límite.");
        return result;
    }
    private static void ValidateSerial(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial) || serial.Length > 128 || !Regex.IsMatch(serial, @"^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant, ParseTimeout))
            throw new InvalidOperationException("El identificador USB del teléfono no es válido.");
    }
}
