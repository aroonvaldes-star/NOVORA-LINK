using NOVORA.Models;

namespace NOVORA.Services;

/// <summary>Calcula el stream según el teléfono; el monitor sólo posiciona la ventana.</summary>
public sealed class OutputProfileService
{
    public OutputProfile Calculate(DeviceInfo device, MonitorInfo? monitor, string bitrate = "10M", int? targetFps = null, int? maxSize = null)
    {
        ValidateDevice(device);
        _ = monitor;
        var capabilities = ResolveCapabilities(device);
        if (!capabilities.IsDetected) return CreateFallbackProfile(device, bitrate, targetFps, maxSize);

        var sourceMax = capabilities.MaxDimension;
        var configuredMax = maxSize is > 0 ? maxSize.Value : sourceMax;
        var outputMax = Math.Clamp(configuredMax, 1, sourceMax);
        var scale = outputMax / (double)sourceMax;
        var width = Math.Max(1, (int)Math.Round(capabilities.NativeWidth * scale));
        var height = Math.Max(1, (int)Math.Round(capabilities.NativeHeight * scale));
        var maximumFps = capabilities.MaxSelectableFps;
        var requested = targetFps is > 0 ? targetFps.Value : Math.Min(60, maximumFps);
        var target = Math.Clamp(requested, 15, Math.Max(15, maximumFps));
        return new OutputProfile(width, height, capabilities.MaxRefreshRateHz, target, NormalizeBitrate(bitrate), outputMax);
    }

    public IReadOnlyList<DisplayModeInfo> GetSupportedModes(DeviceInfo device, MonitorInfo? monitor)
    {
        ValidateDevice(device);
        _ = monitor;
        if (device.SupportedDisplayModes.Count > 0)
            return device.SupportedDisplayModes.Where(mode => mode.Width > 0 && mode.Height > 0 && mode.RefreshRateHz > 0)
                .OrderByDescending(mode => mode.Pixels).ThenByDescending(mode => mode.RefreshRateHz).ToArray();
        return device.BestDisplayMode is null ? Array.Empty<DisplayModeInfo>() : new[] { device.BestDisplayMode };
    }

    public IReadOnlyList<int> GetSupportedFps(DeviceInfo device, MonitorInfo? monitor)
    {
        ValidateDevice(device);
        _ = monitor;
        var capabilities = ResolveCapabilities(device);
        if (!capabilities.IsDetected) return Array.Empty<int>();
        var maximum = capabilities.MaxSelectableFps;
        var values = new HashSet<int>();
        if (maximum >= 30) values.Add(30);
        if (maximum >= 60) values.Add(60);
        foreach (var rate in capabilities.SupportedRefreshRatesHz)
        {
            var fps = Math.Max(1, (int)Math.Round(rate, MidpointRounding.AwayFromZero));
            if (fps is >= 15 && fps <= maximum) values.Add(fps);
        }
        values.Add(maximum);
        return values.Where(value => value is >= 15 && value <= maximum).OrderBy(value => value).ToArray();
    }

    public IReadOnlyList<string> GetBitrateOptions() => new[] { "1M", "2M", "3M", "4M", "6M", "8M", "10M", "12M", "16M", "20M", "25M", "30M", "40M", "50M" };

    private static DeviceCapabilities ResolveCapabilities(DeviceInfo device)
    {
        if (device.Capabilities.IsDetected) return device.Capabilities;
        var modes = device.SupportedDisplayModes.Count > 0
            ? device.SupportedDisplayModes
            : device.BestDisplayMode is null ? Array.Empty<DisplayModeInfo>() : new[] { device.BestDisplayMode };
        if (modes.Count == 0) return DeviceCapabilities.Unknown;
        var native = modes.Where(mode => mode.Width > 0 && mode.Height > 0).OrderByDescending(mode => mode.Pixels).ThenByDescending(mode => mode.RefreshRateHz).FirstOrDefault();
        if (native is null) return DeviceCapabilities.Unknown;
        var rates = modes.Where(mode => mode.RefreshRateHz is >= 20 and <= 360)
            .Select(mode => Math.Round(mode.RefreshRateHz, 1, MidpointRounding.AwayFromZero)).Distinct().OrderBy(value => value).ToArray();
        return new DeviceCapabilities
        {
            NativeWidth = native.Width,
            NativeHeight = native.Height,
            SupportedRefreshRatesHz = rates.Length > 0 ? rates : new[] { 60d }
        };
    }

    private static OutputProfile CreateFallbackProfile(DeviceInfo device, string bitrate, int? targetFps, int? maxSize)
    {
        var mode = device.BestDisplayMode;
        var width = mode?.Width > 0 ? mode.Width : 1080;
        var height = mode?.Height > 0 ? mode.Height : 1920;
        var sourceMax = Math.Max(width, height);
        var outputMax = maxSize is > 0 ? Math.Min(maxSize.Value, sourceMax) : sourceMax;
        var scale = outputMax / (double)sourceMax;
        var outputWidth = Math.Max(1, (int)Math.Round(width * scale));
        var outputHeight = Math.Max(1, (int)Math.Round(height * scale));
        var refresh = mode?.RefreshRateHz is >= 20 and <= 360 ? mode.RefreshRateHz : 60d;
        var maximumFps = Math.Max(15, (int)Math.Round(refresh, MidpointRounding.AwayFromZero));
        var requested = targetFps is > 0 ? targetFps.Value : Math.Min(60, maximumFps);
        return new OutputProfile(outputWidth, outputHeight, refresh, Math.Clamp(requested, 15, maximumFps), NormalizeBitrate(bitrate), outputMax);
    }

    private static string NormalizeBitrate(string? bitrate)
    {
        if (string.IsNullOrWhiteSpace(bitrate)) return "10M";
        var value = bitrate.Trim().ToUpperInvariant().Replace("MB/S", "M").Replace("MBPS", "M").Replace("MBIT/S", "M").Replace("MBIT", "M");
        return value.EndsWith('M') ? value : value + "M";
    }

    private static void ValidateDevice(DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!device.Connected) throw new InvalidOperationException("No hay un dispositivo Android conectado.");
        if (string.IsNullOrWhiteSpace(device.Serial)) throw new InvalidOperationException("El dispositivo Android no tiene un serial ADB válido.");
    }
}
