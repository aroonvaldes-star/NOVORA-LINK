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
            "printf '%s\\n' \"$(dumpsys meminfo | grep -m1 'Total RAM')\" \"$(dumpsys meminfo | grep -m1 'Used RAM')\" \"$(dumpsys battery | grep -m1 'level:')\" \"$(dumpsys battery | grep -m1 'temperature:')\" \"$(top -n 1 -b 2>/dev/null | grep -m1 -E '^[[:space:]]*[0-9]+.*%CPU')\"",
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
        var m = Regex.Match(text, @"(\d+(?:\.\d+)?)%CPU", RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, 100) : 0;
    }
}
