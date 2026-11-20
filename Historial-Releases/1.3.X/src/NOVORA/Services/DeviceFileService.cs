using NOVORA.Models;

namespace NOVORA.Services;

public sealed class DeviceFileService
{
    private readonly AdbService _adb;

    public DeviceFileService(AdbService adb)
    {
        _adb = adb ??
            throw new ArgumentNullException(nameof(adb));
    }

    public Task PullAsync(
        DeviceInfo device,
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
        DeviceInfo device,
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
        DeviceInfo device,
        string remotePath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        return _adb.ShellAsync(
            device.Serial,
            $"ls -la {Quote(remotePath)}",
            ct);
    }

    // Mantiene compatibilidad con MainWindow.xaml.cs
    public Task<string> ListFilesAsync(
        DeviceInfo device,
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
