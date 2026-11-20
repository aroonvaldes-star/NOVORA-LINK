using NOVORA.Launcher.Models;

namespace NOVORA.Launcher.Services;

public sealed class OutputProfileService
{
    public OutputProfile Calculate(DeviceInfo device, MonitorInfo monitor)
    {
        if (monitor is null)
            throw new ArgumentNullException(nameof(monitor));

        var modes = device.SupportedDisplayModes.Count > 0
            ? device.SupportedDisplayModes
            : device.BestDisplayMode is null
                ? Array.Empty<DisplayModeInfo>()
                : new[] { device.BestDisplayMode };

        if (modes.Count == 0)
            throw new InvalidOperationException("NOVORA no pudo determinar los modos de pantalla del dispositivo.");

        var monitorMaxDimension = Math.Max(monitor.Width, monitor.Height);
        var selected = modes
            .Where(m => Math.Max(m.Width, m.Height) <= monitorMaxDimension)
            .OrderByDescending(m => m.Pixels)
            .ThenByDescending(m => m.RefreshRateHz)
            .FirstOrDefault()
            ?? modes.OrderByDescending(m => m.Pixels).ThenByDescending(m => m.RefreshRateHz).First();

        var sourceMaxDimension = Math.Max(selected.Width, selected.Height);
        var outputMaxDimension = Math.Min(sourceMaxDimension, monitorMaxDimension);
        var scale = outputMaxDimension / (double)sourceMaxDimension;
        var width = Math.Max(1, (int)Math.Round(selected.Width * scale));
        var height = Math.Max(1, (int)Math.Round(selected.Height * scale));
        var targetFps = Math.Clamp((int)Math.Floor(Math.Min(selected.RefreshRateHz, monitor.RefreshRateHz)), 30, 240);
        var bitrate = CalculateBitrate(width, height, targetFps);

        return new OutputProfile(width, height, selected.RefreshRateHz, targetFps, bitrate, outputMaxDimension);
    }

    private static string CalculateBitrate(int width, int height, int fps)
    {
        // NOVORA v0.5: bitrate fijo para mantener un comportamiento estable.
        return "10M";
    }
}
