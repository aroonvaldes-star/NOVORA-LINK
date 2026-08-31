using NOVORA.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NOVORA.Services;

public sealed record DeviceMetrics(double CpuPercent, long UsedMemoryKb, long TotalMemoryKb, int BatteryPercent, double BatteryTemperatureC);

public sealed class DeviceMetricsService
{
    private readonly AdbService _adb;
    public DeviceMetricsService(AdbService adb) => _adb = adb ?? throw new ArgumentNullException(nameof(adb));

    public async Task<DeviceMetrics> GetAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        if (device is null || !device.Connected || string.IsNullOrWhiteSpace(device.Serial))
            throw new InvalidOperationException("No hay un dispositivo Android conectado.");

        var totalLine = await SafeShellAsync(device.Serial, "dumpsys meminfo | grep -m1 'Total RAM'", cancellationToken);
        var usedLine = await SafeShellAsync(device.Serial, "dumpsys meminfo | grep -m1 'Used RAM'", cancellationToken);
        var batteryOutput = await SafeShellAsync(device.Serial, "dumpsys battery", cancellationToken);
        var topOutput = await SafeShellAsync(device.Serial, "top -n 1 -b 2>/dev/null | head -n 8", cancellationToken);
        if (string.IsNullOrWhiteSpace(topOutput))
            topOutput = await SafeShellAsync(device.Serial, "top -n 1 2>/dev/null | head -n 8", cancellationToken);

        if (string.IsNullOrWhiteSpace(totalLine) && string.IsNullOrWhiteSpace(usedLine) &&
            string.IsNullOrWhiteSpace(batteryOutput) && string.IsNullOrWhiteSpace(topOutput))
            throw new InvalidOperationException("Android no respondió a las consultas de rendimiento.");

        var total = ParseMemory(totalLine, "Total RAM");
        var used = ParseMemory(usedLine, "Used RAM");
        var battery = ParseInt(batteryOutput, @"level:\s*(\d+)");
        var temp = ParseDouble(batteryOutput, @"temperature:\s*(\d+)") / 10d;
        var cpu = ParseCpu(topOutput);
        return new DeviceMetrics(cpu, used, total, battery, temp);
    }

    private async Task<string> SafeShellAsync(string serial, string command, CancellationToken cancellationToken)
    {
        try { return await _adb.ShellAsync(serial, command, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch { return string.Empty; }
    }

    private static long ParseMemory(string text, string label)
    {
        var match = Regex.Match(text ?? string.Empty, $@"{Regex.Escape(label)}\s*[:=]?\s*([\d,]+)\s*([KMG]B)?", RegexOptions.IgnoreCase);
        if (!match.Success || !long.TryParse(match.Groups[1].Value.Replace(",", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return 0;
        return match.Groups[2].Value.ToUpperInvariant() switch { "GB" => value * 1024 * 1024, "MB" => value * 1024, _ => value };
    }

    private static int ParseInt(string text, string pattern)
        => int.TryParse(Regex.Match(text ?? string.Empty, pattern, RegexOptions.IgnoreCase).Groups[1].Value, out var value) ? value : 0;

    private static double ParseDouble(string text, string pattern)
        => double.TryParse(Regex.Match(text ?? string.Empty, pattern, RegexOptions.IgnoreCase).Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0d;

    private static double ParseCpu(string text)
    {
        text ??= string.Empty;
        var summary = Regex.Match(text, @"(?<total>\d+(?:\.\d+)?)%cpu\s+(?<user>\d+(?:\.\d+)?)%user\s+(?<nice>\d+(?:\.\d+)?)%nice\s+(?<sys>\d+(?:\.\d+)?)%sys\s+(?<idle>\d+(?:\.\d+)?)%idle", RegexOptions.IgnoreCase);
        if (summary.Success &&
            double.TryParse(summary.Groups["total"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var total) && total > 0 &&
            double.TryParse(summary.Groups["idle"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var idle))
            return Math.Clamp(((total - idle) / total) * 100d, 0d, 100d);

        var alternate = Regex.Match(text, @"CPU:\s*(?<user>\d+(?:\.\d+)?)%\s*usr\s+(?<sys>\d+(?:\.\d+)?)%\s*sys", RegexOptions.IgnoreCase);
        if (alternate.Success &&
            double.TryParse(alternate.Groups["user"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var user) &&
            double.TryParse(alternate.Groups["sys"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var sys))
            return Math.Clamp(user + sys, 0d, 100d);

        var processValue = Regex.Match(text, @"(?<cpu>\d+(?:\.\d+)?)%CPU", RegexOptions.IgnoreCase);
        return processValue.Success && double.TryParse(processValue.Groups["cpu"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0d, 100d)
            : 0d;
    }
}
