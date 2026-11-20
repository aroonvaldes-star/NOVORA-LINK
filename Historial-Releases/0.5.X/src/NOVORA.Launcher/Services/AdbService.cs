using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using NOVORA.Launcher.Models;

namespace NOVORA.Launcher.Services;

public sealed class AdbService
{
    private readonly NovoraPaths _paths;

    public AdbService(NovoraPaths paths) => _paths = paths;

    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        await StartServerAsync(cancellationToken);
        var output = await RunAsync(new[] { "devices" }, cancellationToken);
        var devices = new List<DeviceInfo>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("List of devices attached", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || !string.Equals(parts[1], "device", StringComparison.OrdinalIgnoreCase)) continue;

            var serial = parts[0];
            var model = (await GetPropertyAsync(serial, "ro.product.model", cancellationToken)).Trim();
            var android = (await GetPropertyAsync(serial, "ro.build.version.release", cancellationToken)).Trim();
            var build = (await GetPropertyAsync(serial, "ro.build.display.id", cancellationToken)).Trim();
            var display = await GetDisplayModesAsync(serial, cancellationToken);

            devices.Add(new DeviceInfo
            {
                Serial = serial,
                Model = string.IsNullOrWhiteSpace(model) ? "Dispositivo Android" : model.Replace('_', ' '),
                AndroidVersion = android,
                Build = build,
                Connected = true,
                SupportedDisplayModes = display,
                BestDisplayMode = display.OrderByDescending(m => m.Pixels).ThenByDescending(m => m.RefreshRateHz).FirstOrDefault()
            });
        }

        return devices;
    }

    public Task StartServerAsync(CancellationToken cancellationToken = default)
        => RunAsync(new[] { "start-server" }, cancellationToken);

    public async Task<IReadOnlyList<string>> GetConnectedSerialsAsync(CancellationToken cancellationToken = default)
    {
        var output = await RunAsync(new[] { "devices" }, cancellationToken);
        var serials = new List<string>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("List of devices attached", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2 && string.Equals(parts[1], "device", StringComparison.OrdinalIgnoreCase))
                serials.Add(parts[0]);
        }

        return serials;
    }

    public async Task StopServerIfNoOtherDevicesAsync(string? excludedSerial = null, CancellationToken cancellationToken = default)
    {
        using var cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cleanupCts.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            var serials = await GetConnectedSerialsAsync(cleanupCts.Token);
            var otherDeviceExists = serials.Any(s => !string.Equals(s, excludedSerial, StringComparison.OrdinalIgnoreCase));
            if (!otherDeviceExists)
                await RunAsync(new[] { "kill-server" }, cleanupCts.Token);
        }
        catch
        {
            // Cleanup must never prevent NOVORA from closing.
        }
    }

    public Task<string> GetStateAsync(string serial, CancellationToken cancellationToken = default)
        => RunAsync(new[] { "-s", serial, "get-state" }, cancellationToken);

    public Task<string> InstallAsync(string serial, string apkPath, CancellationToken cancellationToken = default)
        => RunAsync(new[] { "-s", serial, "install", "-r", apkPath }, cancellationToken);

    public Task<string> ShellAsync(string serial, string command, CancellationToken cancellationToken = default)
        => RunAsync(new[] { "-s", serial, "shell", command }, cancellationToken);

    private Task<string> GetPropertyAsync(string serial, string property, CancellationToken cancellationToken)
        => RunAsync(new[] { "-s", serial, "shell", "getprop", property }, cancellationToken);

    private async Task<IReadOnlyList<DisplayModeInfo>> GetDisplayModesAsync(string serial, CancellationToken cancellationToken)
    {
        var dumpsys = await ShellAsync(serial, "dumpsys display", cancellationToken);
        var modes = new HashSet<DisplayModeInfo>();

        // Android releases/vendor builds format dumpsys differently. Support the common forms.
        foreach (var line in dumpsys.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        foreach (Match m in Regex.Matches(line, @"(?:width[=:]\s*)(\d+).*?(?:height[=:]\s*)(\d+).*?(?:fps|refreshRate|refresh_rate)[=:]\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase))
        {
            if (int.TryParse(m.Groups[1].Value, out var w) && int.TryParse(m.Groups[2].Value, out var h) && double.TryParse(m.Groups[3].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hz))
            {
                if (w >= 320 && h >= 320 && hz >= 20 && hz <= 360) modes.Add(new DisplayModeInfo(w, h, hz));
            }
        }

        // Fallback to current physical size + peak refresh rate.
        if (modes.Count == 0)
        {
            var size = await ShellAsync(serial, "wm size", cancellationToken);
            var match = Regex.Match(size, @"Physical size:\s*(\d+)x(\d+)", RegexOptions.IgnoreCase);
            if (!match.Success) match = Regex.Match(size, @"Override size:\s*(\d+)x(\d+)", RegexOptions.IgnoreCase);
            var refresh = await ShellAsync(serial, "settings get system peak_refresh_rate", cancellationToken);
            if (!double.TryParse(refresh.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hz) || hz <= 0) hz = 60;
            if (match.Success && int.TryParse(match.Groups[1].Value, out var w) && int.TryParse(match.Groups[2].Value, out var h))
                modes.Add(new DisplayModeInfo(w, h, hz));
        }

        return modes.OrderByDescending(m => m.Pixels).ThenByDescending(m => m.RefreshRateHz).ToArray();
    }

    private async Task<string> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        _paths.ValidateRequiredTools();
        var info = new ProcessStartInfo
        {
            FileName = _paths.Adb,
            WorkingDirectory = _paths.ToolBundleDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("No fue posible iniciar ADB.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
        return output;
    }
}
