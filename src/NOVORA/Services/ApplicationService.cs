using NOVORA.Models;

namespace NOVORA.Services;

public sealed class ApplicationService
{
    private readonly AdbService _adb;
    public ApplicationService(AdbService adb) => _adb = adb;

    public async Task<IReadOnlyList<string>> ListPackagesAsync(DeviceInfo device, CancellationToken ct = default)
    {
        var output = await _adb.ShellAsync(device.Serial, "pm list packages", ct);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.StartsWith("package:", StringComparison.OrdinalIgnoreCase) ? x[8..] : x)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToArray();
    }

    public Task<string> LaunchAsync(DeviceInfo device, string packageName, CancellationToken ct = default)
        => _adb.ShellAsync(device.Serial, $"monkey -p {Quote(packageName)} -c android.intent.category.LAUNCHER 1", ct);

    public Task<string> StopAsync(DeviceInfo device, string packageName, CancellationToken ct = default)
        => _adb.ShellAsync(device.Serial, $"am force-stop {Quote(packageName)}", ct);

    public Task<string> UninstallAsync(DeviceInfo device, string packageName, CancellationToken ct = default)
        => _adb.ShellAsync(device.Serial, $"pm uninstall {Quote(packageName)}", ct);

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}