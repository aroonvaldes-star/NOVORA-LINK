using NOVORA.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NOVORA.Services;

public sealed record DeviceMetrics(double CpuPercent, long UsedMemoryKb, long TotalMemoryKb, int BatteryPercent, double BatteryTemperatureC);

public sealed class DeviceMetricsService
{
    private readonly AdbService _adb;
    public DeviceMetricsService(AdbService adb) => _adb = adb;

    public async Task<DeviceMetrics> GetAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        if (device is null || !device.Connected || string.IsNullOrWhiteSpace(device.Serial))
            throw new InvalidOperationException("No hay un dispositivo Android conectado.");

        var output = await _adb.ShellAsync(device.Serial,
            "printf '%s\\n' \"$(dumpsys meminfo | grep -m1 'Total RAM')\" \"$(dumpsys meminfo | grep -m1 'Used RAM')\" \"$(dumpsys battery | grep -m1 'level:')\" \"$(dumpsys battery | grep -m1 'temperature:')\" \"$(top -n 1 -b 2>/dev/null | head -n 8)\"",
            cancellationToken);

        long total = ParseMemory(output, "Total RAM");
        long used = ParseMemory(output, "Used RAM");
        var battery = ParseInt(output, @"level:\s*(\d+)");
        var temp = ParseDouble(output, @"temperature:\s*(\d+)") / 10d;
        var cpu = ParseCpu(output);
        return new DeviceMetrics(cpu, used, total, battery, temp);
    }

    private static long ParseMemory(string text, string label)
    {
        var m = Regex.Match(text, $@"{Regex.Escape(label)}\s*[:=]?\s*([\d,]+)\s*([KMG]B)?", RegexOptions.IgnoreCase);
        if (!m.Success || !long.TryParse(m.Groups[1].Value.Replace(",", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return 0;
        return m.Groups[2].Value.ToUpperInvariant() switch { "GB" => value * 1024 * 1024, "MB" => value * 1024, _ => value };
    }

    private static int ParseInt(string text, string pattern) => int.TryParse(Regex.Match(text, pattern, RegexOptions.IgnoreCase).Groups[1].Value, out var v) ? v : 0;
    private static double ParseDouble(string text, string pattern) => double.TryParse(Regex.Match(text, pattern, RegexOptions.IgnoreCase).Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double ParseCpu(string text)
    {
        var summary = Regex.Match(text, @"(?<total>\d+(?:\.\d+)?)%cpu\s+(?<user>\d+(?:\.\d+)?)%user\s+(?<nice>\d+(?:\.\d+)?)%nice\s+(?<sys>\d+(?:\.\d+)?)%sys\s+(?<idle>\d+(?:\.\d+)?)%idle", RegexOptions.IgnoreCase);
        if (summary.Success &&
            double.TryParse(summary.Groups["total"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var total) && total > 0 &&
            double.TryParse(summary.Groups["idle"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var idle))
        {
            return Math.Clamp(((total - idle) / total) * 100d, 0d, 100d);
        }

        var alternate = Regex.Match(text, @"CPU:\s*(?<user>\d+(?:\.\d+)?)%\s*usr\s+(?<sys>\d+(?:\.\d+)?)%\s*sys", RegexOptions.IgnoreCase);
        if (alternate.Success &&
            double.TryParse(alternate.Groups["user"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var user) &&
            double.TryParse(alternate.Groups["sys"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var sys))
        {
            return Math.Clamp(user + sys, 0d, 100d);
        }

        return 0d;
    }
}
