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

    public FileExchangeVE(AdbService adb, ManagerControlVE control)
    {
        _adbVE = adb ?? throw new ArgumentNullException(nameof(adb));
        _controlVE = control ?? throw new ArgumentNullException(nameof(control));
    }

    public async Task<ResultExchangeVE> PushToDownloadsAsync(
        string serial,
        string localPath,
        string? destinationFileName = null,
        bool scanMedia = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        string fullLocal = Path.GetFullPath(localPath);
        if (!File.Exists(fullLocal)) return ResultExchangeVE.FailVE("El archivo local no existe.", fullLocal);
        string remote = PathExchangeVE.BuildAndroidDownloadPathVE(destinationFileName ?? Path.GetFileName(fullLocal));
        try
        {
            string output = await _adbVE.PushAsync(serial.Trim(), fullLocal, remote, cancellationToken).ConfigureAwait(false);
            if (scanMedia && _controlVE.IsReadyVE)
                await _controlVE.SendAsync(MessageControlVE.ScanFileVE(remote), cancellationToken).ConfigureAwait(false);
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
}
