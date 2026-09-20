using NOVORA.Service;
using NOVORA.VisionEngine.Control;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Transferencia bidireccional de archivos sin usar scrcpy.exe.
/// ADB mueve los bytes y ControlVE solicita MediaScanner cuando corresponde.
/// </summary>
public sealed class VEExchangeFile
{
    private readonly NLServiceADB _adbVE;
    private readonly VEControlManager _controlVE;
    private readonly Func<bool>? _canExchangeFilesVE;

    public VEExchangeFile(
        NLServiceADB adb,
        VEControlManager control,
        Func<bool>? canExchangeFiles = null)
    {
        _adbVE = adb ?? throw new ArgumentNullException(nameof(adb));
        _controlVE = control ?? throw new ArgumentNullException(nameof(control));
        _canExchangeFilesVE = canExchangeFiles;
    }

    public async Task<VEExchangeResult> PushToDownloadsAsync(
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
        if (!File.Exists(fullLocal)) return VEExchangeResult.FailVE("El archivo local no existe.", fullLocal);
        string remote = VEExchangePath.BuildCategorizedDestinationFromNameVE(destinationFileName ?? Path.GetFileName(fullLocal));
        try
        {
            await _adbVE.ShellAsync(
                    serial.Trim(),
                    VEExchangePath.BuildEnsureRootCommandVE(),
                    cancellationToken)
                .ConfigureAwait(false);

            remote = await VEExchangePush.SendAsync(serial.Trim(), fullLocal, remote, false, cancellationToken).ConfigureAwait(false);
            if (scanMedia && _controlVE.IsReadyVE)
                await _controlVE.SendAsync(VEControlMessage.ScanFileVE(remote), cancellationToken).ConfigureAwait(false);
            if (scanMedia)
                await RequestMediaScanAsync(serial.Trim(), remote, cancellationToken).ConfigureAwait(false);
            return VEExchangeResult.OkVE("Archivo enviado a Android.", fullLocal, remote);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return VEExchangeResult.FailVE("No fue posible enviar el archivo a Android.", fullLocal, remote, ex.Message);
        }
    }

    public async Task<VEExchangeResult> PullFromAndroidAsync(
        string serial,
        string remotePath,
        string localDestination,
        CancellationToken cancellationToken = default)
    {
        EnsurePrivacyVE();
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localDestination);
        string name = NOVORA.Control.NLControlFileStorage.SafeName(VEExchangePath.GetFileNameVE(remotePath));
        string root = NOVORA.Control.NLControlFileStorage.PcRoot;
        Directory.CreateDirectory(root);
        string local = Path.Combine(root, ".novora-" + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            string output = await _adbVE.PullAsync(serial.Trim(), remotePath.Trim(), local, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(local)) return VEExchangeResult.FailVE("ADB terminó pero el archivo local no apareció.", remotePath, local, output.Trim());
            string committed = NOVORA.Control.NLControlFileStorage.Commit(local, root, name);
            return VEExchangeResult.OkVE("Archivo recibido desde Android.", remotePath, committed, output.Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return VEExchangeResult.FailVE("No fue posible recibir el archivo desde Android.", remotePath, local, ex.Message);
        }
        finally { try { File.Delete(local); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    public async Task<VEExchangeResult> PullNovoraFolderAsync(
        string serial,
        string localDestinationDirectory,
        CancellationToken cancellationToken = default)
    {
        EnsurePrivacyVE();
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(localDestinationDirectory);

        string root = NOVORA.Control.NLControlFileStorage.PcRoot;
        Directory.CreateDirectory(root);
        string local = Path.Combine(root, ".novora-recepcion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(local);

        try
        {
            await _adbVE.ShellAsync(
                    serial.Trim(),
                    VEExchangePath.BuildEnsureRootCommandVE(),
                    cancellationToken)
                .ConfigureAwait(false);

            string output = await _adbVE.PullAsync(
                    serial.Trim(),
                    VEExchangePath.RootAndroidVE,
                    local,
                    cancellationToken)
                .ConfigureAwait(false);

            string receivedRoot = Directory.Exists(Path.Combine(local, "NOVORA")) ? Path.Combine(local, "NOVORA") : local;
            int count = NOVORA.Control.NLControlFileStorage.ImportDirectory(receivedRoot, root);
            return count > 0
                ? VEExchangeResult.OkVE($"{count} archivo(s)/carpeta(s) recibidos y organizados por tipo.", VEExchangePath.RootAndroidVE, root, output.Trim())
                : VEExchangeResult.FailVE("ADB terminó pero no aparecieron archivos locales.", VEExchangePath.RootAndroidVE, root, output.Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return VEExchangeResult.FailVE("No fue posible recibir la carpeta NOVORA desde Android.", VEExchangePath.RootAndroidVE, local, ex.Message);
        }
        finally { try { Directory.Delete(local, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private async Task RequestMediaScanAsync(
        string serial,
        string remote,
        CancellationToken cancellationToken)
    {
        string fileUri = $"file://{remote}";
        string rootUri = $"file://{VEExchangePath.RootAndroidVE}";
        string folderUri =
            $"file://{VEExchangePath.NormalizeAndroidPathVE(Path.GetDirectoryName(remote)?.Replace('\\', '/') ?? VEExchangePath.RootAndroidVE)}";

        try
        {
            string quotedFileUri =
                VEExchangePath.QuoteShellArgumentVE(
                    fileUri);

            string quotedRootUri =
                VEExchangePath.QuoteShellArgumentVE(
                    rootUri);

            string quotedFolderUri =
                VEExchangePath.QuoteShellArgumentVE(
                    folderUri);

            string quotedRemote =
                VEExchangePath.QuoteShellArgumentVE(
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
