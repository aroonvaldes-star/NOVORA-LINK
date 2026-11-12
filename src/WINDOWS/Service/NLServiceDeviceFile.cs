using NOVORA.Model;

namespace NOVORA.Service;

public sealed class NLServiceDeviceFile
{
    private readonly NLServiceADB _adb;

    public NLServiceDeviceFile(NLServiceADB adb)
    {
        _adb = adb ??
            throw new ArgumentNullException(nameof(adb));
    }

    public Task PullAsync(
        NLModelDeviceInfo device,
        string remotePath,
        string localPath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        return _adb.PullAsync(
            device.Serial,
            remotePath,
            localPath,
            ct);
    }

    public Task PushAsync(
        NLModelDeviceInfo device,
        string localPath,
        string remotePath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        return _adb.PushAsync(
            device.Serial,
            localPath,
            remotePath,
            ct);
    }

    public Task<string> ListAsync(
        NLModelDeviceInfo device,
        string remotePath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        return _adb.ShellAsync(
            device.Serial,
            $"ls -la {Quote(remotePath)}",
            ct);
    }

    // Mantiene compatibilidad con NLUIWindowMain.xaml.cs
    public Task<string> ListFilesAsync(
        NLModelDeviceInfo device,
        string remotePath,
        CancellationToken ct = default)
    {
        return ListAsync(
            device,
            remotePath,
            ct);
    }

    private static string Quote(string value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        return "'" +
               value.Replace(
                   "'",
                   "'\\''",
                   StringComparison.Ordinal) +
               "'";
    }
}
