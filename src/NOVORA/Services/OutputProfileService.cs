using NOVORA.Models;

namespace NOVORA.Services;

/// <summary>
/// Construye el perfil de salida de scrcpy utilizando las capacidades
/// detectadas del dispositivo Android.
///
/// El monitor del PC NO limita FPS ni resolución.
/// Se conserva el parámetro MonitorInfo por compatibilidad con el resto
/// de NOVORA.
/// </summary>
public sealed class OutputProfileService
{
    public OutputProfile Calculate(
        DeviceInfo device,
        MonitorInfo? monitor,
        string bitrate = "10M",
        int? targetFps = null,
        int? maxSize = null)
    {
        ValidateDevice(device);

        // El monitor no limita las capacidades del teléfono.
        _ = monitor;

        DeviceCapabilities capabilities =
            ResolveCapabilities(device);

        if (!capabilities.IsDetected)
        {
            return CreateFallbackProfile(
                device,
                bitrate,
                targetFps,
                maxSize);
        }

        int sourceWidth =
            capabilities.NativeWidth;

        int sourceHeight =
            capabilities.NativeHeight;

        int sourceMaxDimension =
            capabilities.MaxDimension;

        int configuredMax =
            maxSize is > 0
                ? maxSize.Value
                : sourceMaxDimension;

        int outputMaxDimension =
            Math.Clamp(
                configuredMax,
                1,
                sourceMaxDimension);

        double scale =
            outputMaxDimension /
            (double)sourceMaxDimension;

        int width =
            Math.Max(
                1,
                (int)Math.Round(
                    sourceWidth * scale));

        int height =
            Math.Max(
                1,
                (int)Math.Round(
                    sourceHeight * scale));

        int maximumFps =
            capabilities.MaxSelectableFps;

        int requestedFps =
            targetFps is > 0
                ? targetFps.Value
                : Math.Min(
                    60,
                    maximumFps);

        int target =
            Math.Clamp(
                requestedFps,
                15,
                Math.Max(
                    15,
                    maximumFps));

        return new OutputProfile(
            width,
            height,
            capabilities.MaxRefreshRateHz,
            target,
            NormalizeBitrate(bitrate),
            outputMaxDimension);
    }

    /// <summary>
    /// Obtiene los modos detectados del teléfono.
    /// El monitor del PC no filtra estos modos.
    /// </summary>
    public IReadOnlyList<DisplayModeInfo> GetSupportedModes(
        DeviceInfo device,
        MonitorInfo? monitor)
    {
        ValidateDevice(device);

        _ = monitor;

        if (device.SupportedDisplayModes.Count > 0)
        {
            return device
                .SupportedDisplayModes
                .Where(mode =>
                    mode.Width > 0 &&
                    mode.Height > 0 &&
                    mode.RefreshRateHz > 0)
                .OrderByDescending(
                    mode => mode.Pixels)
                .ThenByDescending(
                    mode => mode.RefreshRateHz)
                .ToArray();
        }

        if (device.BestDisplayMode is not null)
        {
            return new[]
            {
                device.BestDisplayMode
            };
        }

        return Array.Empty<DisplayModeInfo>();
    }

    /// <summary>
    /// Devuelve los FPS que NOVORA puede ofrecer según las capacidades
    /// detectadas del panel del teléfono.
    ///
    /// El monitor del PC no limita esta lista.
    /// </summary>
    public IReadOnlyList<int> GetSupportedFps(
        DeviceInfo device,
        MonitorInfo? monitor)
    {
        ValidateDevice(device);

        _ = monitor;

        DeviceCapabilities capabilities =
            ResolveCapabilities(device);

        if (!capabilities.IsDetected)
        {
            return Array.Empty<int>();
        }

        int maximum =
            capabilities.MaxSelectableFps;

        var values =
            new HashSet<int>();

        if (maximum >= 30)
        {
            values.Add(30);
        }

        if (maximum >= 60)
        {
            values.Add(60);
        }

        foreach (double refreshRate in
                 capabilities.SupportedRefreshRatesHz)
        {
            int fps =
                Math.Max(
                    1,
                    (int)Math.Round(
                        refreshRate,
                        MidpointRounding.AwayFromZero));

            if (fps >= 15 &&
                fps <= maximum)
            {
                values.Add(fps);
            }
        }

        values.Add(maximum);

        return values
            .Where(value =>
                value >= 15 &&
                value <= maximum)
            .OrderBy(value => value)
            .ToArray();
    }

    public IReadOnlyList<string> GetBitrateOptions()
    {
        return new[]
        {
            "1M",
            "2M",
            "3M",
            "4M",
            "6M",
            "8M",
            "10M",
            "12M",
            "16M",
            "20M",
            "25M",
            "30M",
            "40M",
            "50M"
        };
    }

    private static DeviceCapabilities ResolveCapabilities(
        DeviceInfo device)
    {
        if (device.Capabilities.IsDetected)
        {
            return device.Capabilities;
        }

        IReadOnlyList<DisplayModeInfo> modes =
            device.SupportedDisplayModes.Count > 0
                ? device.SupportedDisplayModes
                : device.BestDisplayMode is null
                    ? Array.Empty<DisplayModeInfo>()
                    : new[]
                    {
                        device.BestDisplayMode
                    };

        if (modes.Count == 0)
        {
            return DeviceCapabilities.Unknown;
        }

        DisplayModeInfo? native =
            modes
                .Where(mode =>
                    mode.Width > 0 &&
                    mode.Height > 0)
                .OrderByDescending(
                    mode => mode.Pixels)
                .ThenByDescending(
                    mode => mode.RefreshRateHz)
                .FirstOrDefault();

        if (native is null)
        {
            return DeviceCapabilities.Unknown;
        }

        double[] refreshRates =
            modes
                .Where(mode =>
                    mode.RefreshRateHz >= 20 &&
                    mode.RefreshRateHz <= 360)
                .Select(mode =>
                    Math.Round(
                        mode.RefreshRateHz,
                        1,
                        MidpointRounding.AwayFromZero))
                .Distinct()
                .OrderBy(value => value)
                .ToArray();

        return new DeviceCapabilities
        {
            NativeWidth =
                native.Width,

            NativeHeight =
                native.Height,

            SupportedRefreshRatesHz =
                refreshRates.Length > 0
                    ? refreshRates
                    : new[]
                    {
                        60d
                    }
        };
    }

    private static OutputProfile CreateFallbackProfile(
        DeviceInfo device,
        string bitrate,
        int? targetFps,
        int? maxSize)
    {
        DisplayModeInfo? mode =
            device.BestDisplayMode;

        int width =
            mode?.Width > 0
                ? mode.Width
                : 1080;

        int height =
            mode?.Height > 0
                ? mode.Height
                : 1920;

        int sourceMaxDimension =
            Math.Max(
                width,
                height);

        int outputMaxDimension =
            maxSize is > 0
                ? Math.Min(
                    maxSize.Value,
                    sourceMaxDimension)
                : sourceMaxDimension;

        double scale =
            outputMaxDimension /
            (double)sourceMaxDimension;

        int outputWidth =
            Math.Max(
                1,
                (int)Math.Round(
                    width * scale));

        int outputHeight =
            Math.Max(
                1,
                (int)Math.Round(
                    height * scale));

        double sourceRefreshRate =
            mode?.RefreshRateHz is >= 20 and <= 360
                ? mode.RefreshRateHz
                : 60d;

        int maximumFps =
            Math.Max(
                15,
                (int)Math.Round(
                    sourceRefreshRate,
                    MidpointRounding.AwayFromZero));

        int requestedFps =
            targetFps is > 0
                ? targetFps.Value
                : Math.Min(
                    60,
                    maximumFps);

        int target =
            Math.Clamp(
                requestedFps,
                15,
                maximumFps);

        return new OutputProfile(
            outputWidth,
            outputHeight,
            sourceRefreshRate,
            target,
            NormalizeBitrate(bitrate),
            outputMaxDimension);
    }

    private static string NormalizeBitrate(
        string? bitrate)
    {
        if (string.IsNullOrWhiteSpace(bitrate))
        {
            return "10M";
        }

        string value =
            bitrate
                .Trim()
                .ToUpperInvariant()
                .Replace(
                    "MB/S",
                    "M")
                .Replace(
                    "MBPS",
                    "M")
                .Replace(
                    "MBIT/S",
                    "M")
                .Replace(
                    "MBIT",
                    "M");

        if (!value.EndsWith('M'))
        {
            value += "M";
        }

        return value;
    }

    private static void ValidateDevice(
        DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!device.Connected)
        {
            throw new InvalidOperationException(
                "No hay un dispositivo Android conectado.");
        }
    }
}