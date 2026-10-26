using NOVORA.Service;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Implementación de <see cref="IVEExchangeUsbBackend"/> sobre el ADB existente.
/// Recibe solo seriales ADB; un endpoint LAN nunca llega hasta aquí.
/// </summary>
public sealed class VEExchangeUsbBackend(NLServiceADB adb) : IVEExchangeUsbBackend
{
    private readonly NLServiceADB _adb = adb ?? throw new ArgumentNullException(nameof(adb));

    public async Task<string> PushAsync(string serial, string localPath, CancellationToken cancellationToken)
    {
        string validSerial = VEExchangeUsbSerialGuard.Validate(serial);
        string full = Path.GetFullPath(localPath);
        if (!File.Exists(full) && !Directory.Exists(full))
            throw new FileNotFoundException("El elemento local no existe.", full);

        await _adb.ShellAsync(validSerial, VEExchangePath.BuildEnsureRootCommandVE(), cancellationToken).ConfigureAwait(false);
        string remote = VEExchangePath.BuildCategorizedDestinationFromNameVE(Path.GetFileName(full));
        bool directory = Directory.Exists(full);
        return await VEExchangePush.SendAsync(validSerial, full, remote, directory, cancellationToken).ConfigureAwait(false);
    }

    public async Task PullAsync(string serial, string remotePath, string localPath, CancellationToken cancellationToken)
    {
        string validSerial = VEExchangeUsbSerialGuard.Validate(serial);
        await _adb.PullAsync(validSerial, remotePath, localPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task PullFolderAsync(string serial, string remotePath, string localPath, CancellationToken cancellationToken)
    {
        string validSerial = VEExchangeUsbSerialGuard.Validate(serial);
        await _adb.ShellAsync(validSerial, VEExchangePath.BuildEnsureRootCommandVE(), cancellationToken).ConfigureAwait(false);
        await _adb.PullAsync(validSerial, remotePath, localPath, cancellationToken).ConfigureAwait(false);
    }
}
