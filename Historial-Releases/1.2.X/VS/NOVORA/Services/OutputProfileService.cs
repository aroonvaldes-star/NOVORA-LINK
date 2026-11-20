using System.Globalization;
using NOVORA.Models;
namespace NOVORA.Services;
public sealed class OutputProfileService
{
    public OutputProfile Calculate(DeviceInfo device, MonitorInfo monitor, string bitrate = "10 Mbps", int? targetFps = null, int? maxSize = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(monitor);
        var modes = device.SupportedDisplayModes.Count > 0 ? device.SupportedDisplayModes : device.BestDisplayMode is null ? Array.Empty<DisplayModeInfo>() : new[] { device.BestDisplayMode! };
        if (modes.Count == 0) throw new InvalidOperationException("NOVORA no pudo determinar los modos de pantalla del dispositivo.");
        var monitorMax = Math.Max(monitor.Width, monitor.Height);
        var selected = modes.Where(m => Math.Max(m.Width, m.Height) <= monitorMax).OrderByDescending(m => m.Pixels).ThenByDescending(m => m.RefreshRateHz).FirstOrDefault() ?? modes.OrderByDescending(m => m.Pixels).ThenByDescending(m => m.RefreshRateHz).First();
        var sourceMax = Math.Max(selected.Width, selected.Height);
        var configuredMax = maxSize is > 0 ? maxSize.Value : sourceMax;
        var outputMax = Math.Min(sourceMax, Math.Min(monitorMax, configuredMax));
        var scale = outputMax / (double)sourceMax;
        var width = Math.Max(1, (int)Math.Round(selected.Width * scale));
        var height = Math.Max(1, (int)Math.Round(selected.Height * scale));
        var fps = targetFps is > 0 ? targetFps.Value : (int)Math.Floor(Math.Min(selected.RefreshRateHz, monitor.RefreshRateHz));
        return new OutputProfile(width, height, selected.RefreshRateHz, Math.Clamp(fps, 15, 240), NormalizeBitrate(bitrate), outputMax);
    }
    public static string NormalizeBitrateForTest(string bitrate) => NormalizeBitrate(bitrate);
    private static string NormalizeBitrate(string bitrate)
    {
        if (string.IsNullOrWhiteSpace(bitrate)) return "10M";
        var value = bitrate.Trim().Replace(',', '.');
        var upper = value.ToUpperInvariant().Replace("MEGABITS/SECOND", "MBPS").Replace("MEGABITS/S", "MBPS").Replace("MBIT/S", "MBPS");
        if (upper.Contains("MB/S", StringComparison.Ordinal))
        {
            var number = upper.Replace("MB/S", "", StringComparison.Ordinal).Trim();
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var mbPerSecond))
                return $"{mbPerSecond * 8:0.###}M";
        }
        upper = upper.Replace("MBPS", "").Replace("M", "").Trim();
        if (double.TryParse(upper, NumberStyles.Float, CultureInfo.InvariantCulture, out var mbps))
            return $"{mbps:0.###}M";
        return "10M";
    }
}
