using NOVORA.Services;
using NOVORA.VisionEngine.Control;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Transferencia bidireccional de archivos sin usar scrcpy.exe.
/// ADB mueve los bytes y ControlVE solicita MediaScanner cuando corresponde.
/// </summary>
public sealed class FileExchangeVE
{
    private readonly AdbService _adbVE;
    private readonly ManagerControlVE _controlVE;
    private readonly Func<bool>? _canExchangeFilesVE;

    public FileExchangeVE(
        AdbService adb,
        ManagerControlVE control,
        Func<bool>? canExchangeFiles = null)
    {
        _adbVE = adb ?? throw new ArgumentNullException(nameof(adb));
        _controlVE = control ?? throw new ArgumentNullException(nameof(control));
        _canExchangeFilesVE = canExchangeFiles;
    }

    public async Task<ResultExchangeVE> PushToDownloadsAsync(
        string serial,
        string localPath,
        string? destinationFileName = null,
        bool scanMedia = true,
        CancellationToken cancellationToken = default)
    {
        EnsurePrivacyVE();
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        string fullLocal = Path.GetFullPath(localPath);
        if (!File.Exists(fullLocal)) return ResultExchangeVE.FailVE("El archivo local no existe.", fullLocal);
        string remote = PathExchangeVE.BuildCategorizedDestinationFromNameVE(destinationFileName ?? Path.GetFileName(fullLocal));
        try
        {
            await _adbVE.ShellAsync(
                    serial.Trim(),
                    PathExchangeVE.BuildEnsureRootCommandVE(),
                    cancellationToken)
                .ConfigureAwait(false);

            string output = await _adbVE.PushAsync(serial.Trim(), fullLocal, remote, cancellationToken).ConfigureAwait(false);
            if (scanMedia && _controlVE.IsReadyVE)
                await _controlVE.SendAsync(MessageControlVE.ScanFileVE(remote), cancellationToken).ConfigureAwait(false);
            if (scanMedia)
                await RequestMediaScanAsync(serial.Trim(), remote, cancellationToken).ConfigureAwait(false);
            return ResultExchangeVE.OkVE("Archivo enviado a Android.", fullLocal, remote, output.Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ResultExchangeVE.FailVE("No fue posible enviar el archivo a Android.", fullLocal, remote, ex.Message);
        }
    }

    public async Task<ResultExchangeVE> PullFromAndroidAsync(
        string serial,
        string remotePath,
        string localDestination,
        CancellationToken cancellationToken = default)
    {
        EnsurePrivacyVE();
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localDestination);
        string local = Path.GetFullPath(localDestination);
        string? parent = Path.GetDirectoryName(local);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        try
        {
            string output = await _adbVE.PullAsync(serial.Trim(), remotePath.Trim(), local, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(local)) return ResultExchangeVE.FailVE("ADB terminó pero el archivo local no apareció.", remotePath, local, output.Trim());
            return ResultExchangeVE.OkVE("Archivo recibido desde Android.", remotePath, local, output.Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ResultExchangeVE.FailVE("No fue posible recibir el archivo desde Android.", remotePath, local, ex.Message);
        }
    }

    public async Task<ResultExchangeVE> PullNovoraFolderAsync(
        string serial,
        string localDestinationDirectory,
        CancellationToken cancellationToken = default)
    {
        EnsurePrivacyVE();
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(localDestinationDirectory);

        string local = Path.GetFullPath(localDestinationDirectory);
        Directory.CreateDirectory(local);

        try
        {
            await _adbVE.ShellAsync(
                    serial.Trim(),
                    PathExchangeVE.BuildEnsureRootCommandVE(),
                    cancellationToken)
                .ConfigureAwait(false);

            string output = await _adbVE.PullAsync(
                    serial.Trim(),
                    PathExchangeVE.RootAndroidVE,
                    local,
                    cancellationToken)
                .ConfigureAwait(false);

            return Directory.EnumerateFileSystemEntries(local).Any()
                ? ResultExchangeVE.OkVE("Carpeta NOVORA recibida desde Android.", PathExchangeVE.RootAndroidVE, local, output.Trim())
                : ResultExchangeVE.FailVE("ADB terminó pero no aparecieron archivos locales.", PathExchangeVE.RootAndroidVE, local, output.Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ResultExchangeVE.FailVE("No fue posible recibir la carpeta NOVORA desde Android.", PathExchangeVE.RootAndroidVE, local, ex.Message);
        }
    }

    private async Task RequestMediaScanAsync(
        string serial,
        string remote,
        CancellationToken cancellationToken)
    {
        string fileUri = $"file://{remote}";
        string rootUri = $"file://{PathExchangeVE.RootAndroidVE}";
        string folderUri =
            $"file://{PathExchangeVE.NormalizeAndroidPathVE(Path.GetDirectoryName(remote)?.Replace('\\', '/') ?? PathExchangeVE.RootAndroidVE)}";

        try
        {
            string quotedFileUri =
                PathExchangeVE.QuoteShellArgumentVE(
                    fileUri);

            string quotedRootUri =
                PathExchangeVE.QuoteShellArgumentVE(
                    rootUri);

            string quotedFolderUri =
                PathExchangeVE.QuoteShellArgumentVE(
                    folderUri);

            string quotedRemote =
                PathExchangeVE.QuoteShellArgumentVE(
                    remote);

            await _adbVE.ShellAsync(
                    serial,
                    $"am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d {quotedFileUri}",
                    cancellationToken)
                .ConfigureAwait(false);

            await _adbVE.ShellAsync(
                    serial,
                    $"am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d {quotedFolderUri}",
                    cancellationToken)
                .ConfigureAwait(false);

            await _adbVE.ShellAsync(
                    serial,
                    $"am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d {quotedRootUri}",
                    cancellationToken)
                .ConfigureAwait(false);

            await _adbVE.ShellAsync(
                    serial,
                    $"cmd media scan-file {quotedRemote}",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // El escaneo multimedia es una mejora de visibilidad; no debe marcar la copia como fallida.
        }
    }

    private void EnsurePrivacyVE()
    {
        if (_canExchangeFilesVE is not null && !_canExchangeFilesVE())
            throw new InvalidOperationException("PrivacyVE bloqueó la transferencia de archivos.");
    }
}
