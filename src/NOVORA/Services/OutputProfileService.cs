using NOVORA.Models;

namespace NOVORA.Services;

public sealed class OutputProfileService
{
    public OutputProfile Calculate(DeviceInfo device, MonitorInfo monitor, string bitrate = "10M", int? targetFps = null, int? maxSize = null)
    {
        ValidateDevice(device);
        if (monitor is null) throw new ArgumentNullException(nameof(monitor));
        var modes = GetSupportedModes(device, monitor);
        if (modes.Count == 0) return CreateFallbackProfile(monitor, bitrate, targetFps, maxSize);
        var selected = modes.OrderByDescending(m => m.Pixels).ThenByDescending(m => m.RefreshRateHz).First();
        var sourceMaxDimension = Math.Max(selected.Width, selected.Height);
        var monitorMaxDimension = Math.Max(monitor.Width, monitor.Height);
        var configuredMax = maxSize is > 0 ? maxSize.Value : sourceMaxDimension;
        var outputMaxDimension = Math.Min(sourceMaxDimension, Math.Min(monitorMaxDimension, configuredMax));
        var scale = outputMaxDimension / (double)sourceMaxDimension;
        var width = Math.Max(1, (int)Math.Round(selected.Width * scale));
        var height = Math.Max(1, (int)Math.Round(selected.Height * scale));
        var requestedFps = targetFps is > 0 ? targetFps.Value : (int)Math.Floor(selected.RefreshRateHz);
        var target = ClampFps(requestedFps, selected.RefreshRateHz, monitor.RefreshRateHz);
        var normalizedBitrate = NormalizeBitrate(bitrate);
        return new OutputProfile(width, height, selected.RefreshRateHz, target, normalizedBitrate, outputMaxDimension);
    }

    public IReadOnlyList<DisplayModeInfo> GetSupportedModes(DeviceInfo device, MonitorInfo monitor)
    {
        ValidateDevice(device);
        if (monitor is null) throw new ArgumentNullException(nameof(monitor));
        var modes = device.SupportedDisplayModes.Count > 0 ? device.SupportedDisplayModes : device.BestDisplayMode is null ? Array.Empty<DisplayModeInfo>() : new[] { device.BestDisplayMode };
        if (modes.Count == 0) return Array.Empty<DisplayModeInfo>();
        var monitorMaxDimension = Math.Max(monitor.Width, monitor.Height);
        return modes.Where(m => m.Width > 0 && m.Height > 0 && m.RefreshRateHz > 0)
            .Where(m => Math.Max(m.Width, m.Height) <= monitorMaxDimension)
            .OrderByDescending(m => m.Pixels).ThenByDescending(m => m.RefreshRateHz).ToArray();
    }

    public IReadOnlyList<int> GetSupportedFps(DeviceInfo device, MonitorInfo monitor)
    {
        var modes = GetSupportedModes(device, monitor);
        if (modes.Count == 0) return Array.Empty<int>();
        var monitorHz = Math.Max(1, (int)Math.Floor(monitor.RefreshRateHz));
        return modes.Select(m => Math.Min((int)Math.Floor(m.RefreshRateHz), monitorHz)).Where(fps => fps >= 15).Distinct().OrderBy(fps => fps).ToArray();
    }

    public IReadOnlyList<string> GetBitrateOptions() => new[] { "1M", "2M", "3M", "4M", "6M", "8M", "10M", "12M", "16M", "20M", "25M", "30M", "40M", "50M" };

    private static OutputProfile CreateFallbackProfile(MonitorInfo monitor, string bitrate, int? targetFps, int? maxSize)
    {
        var monitorMaxDimension = Math.Max(monitor.Width, monitor.Height);
        var outputMaxDimension = maxSize is > 0 ? Math.Min(maxSize.Value, monitorMaxDimension) : monitorMaxDimension;
        var scale = monitorMaxDimension > 0 ? outputMaxDimension / (double)monitorMaxDimension : 1.0;
        var width = Math.Max(1, (int)Math.Round(monitor.Width * scale));
        var height = Math.Max(1, (int)Math.Round(monitor.Height * scale));
        var monitorFps = Math.Max(15, (int)Math.Floor(monitor.RefreshRateHz));
        var requestedFps = targetFps is > 0 ? targetFps.Value : 60;
        var target = Math.Clamp(requestedFps, 15, Math.Min(monitorFps, 240));
        return new OutputProfile(width, height, monitor.RefreshRateHz, target, NormalizeBitrate(bitrate), outputMaxDimension);
    }

    private static int ClampFps(int requested, double sourceRefreshRate, double monitorRefreshRate)
    {
        var maximum = Math.Min((int)Math.Floor(sourceRefreshRate), (int)Math.Floor(monitorRefreshRate));
        if (maximum < 15) maximum = 15;
        return Math.Clamp(requested, 15, Math.Min(maximum, 240));
    }

    private static string NormalizeBitrate(string? bitrate)
    {
        if (string.IsNullOrWhiteSpace(bitrate)) return "10M";
        var value = bitrate.Trim().ToUpperInvariant().Replace("MB/S", "M").Replace("MBPS", "M").Replace("MBIT/S", "M").Replace("MBIT", "M");
        if (!value.EndsWith('M')) value += "M";
        return value;
    }

    private static void ValidateDevice(DeviceInfo device)
    {
        if (device is null) throw new ArgumentNullException(nameof(device));
        if (!device.Connected) throw new InvalidOperationException("No hay un dispositivo Android conectado.");
        if (string.IsNullOrWhiteSpace(device.Serial)) throw new InvalidOperationException("El dispositivo Android no tiene un Serial ADB válido.");
    }
}