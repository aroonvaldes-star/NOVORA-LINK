using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NOVORA.Models;

namespace NOVORA.Services;

/// <summary>
/// Punto único de acceso de NOVORA al servidor ADB.
/// Mantiene el servidor vivo, evita refrescos simultáneos y reutiliza el último estado breve.
/// </summary>
public sealed class AdbService
{
    private static readonly TimeSpan DeviceCacheLifetime =
        TimeSpan.FromSeconds(2);

    private readonly NovoraPaths _paths;
    private readonly SemaphoreSlim _serverGate = new(1, 1);
    private readonly SemaphoreSlim _deviceGate = new(1, 1);

    private IReadOnlyList<DeviceInfo>? _deviceCache;
    private DateTimeOffset _deviceCacheAt =
        DateTimeOffset.MinValue;

    private bool _serverStarted;

    public AdbService(NovoraPaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        if (!forceRefresh &&
            _deviceCache is not null &&
            DateTimeOffset.UtcNow - _deviceCacheAt <
            DeviceCacheLifetime)
        {
            return _deviceCache;
        }

        await _deviceGate.WaitAsync(
            cancellationToken);

        try
        {
            if (!forceRefresh &&
                _deviceCache is not null &&
                DateTimeOffset.UtcNow - _deviceCacheAt <
                DeviceCacheLifetime)
            {
                return _deviceCache;
            }

            await StartServerAsync(
                cancellationToken);

            var serials =
                await QueryConnectedSerialsAsync(
                    cancellationToken);

            var devices =
                new List<DeviceInfo>(
                    serials.Count);

            foreach (var serial in serials)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                devices.Add(
                    await ReadDeviceAsync(
                        serial,
                        cancellationToken));
            }

            _deviceCache = devices;
            _deviceCacheAt =
                DateTimeOffset.UtcNow;

            return devices;
        }
        finally
        {
            _deviceGate.Release();
        }
    }

    public async Task StartServerAsync(
        CancellationToken cancellationToken = default)
    {
        if (_serverStarted)
            return;

        await _serverGate.WaitAsync(
            cancellationToken);

        try
        {
            if (_serverStarted)
                return;

            await RunAsync(
                new[]
                {
                    "start-server"
                },
                cancellationToken);

            _serverStarted = true;
        }
        finally
        {
            _serverGate.Release();
        }
    }

    public async Task<IReadOnlyList<string>>
        GetConnectedSerialsAsync(
            CancellationToken cancellationToken = default)
    {
        await StartServerAsync(
            cancellationToken);

        return await QueryConnectedSerialsAsync(
            cancellationToken);
    }

    public async Task<string> ConnectOverWifiAsync(
        string usbSerial,
        int port = 5555,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                usbSerial))
        {
            throw new ArgumentException(
                "No hay un dispositivo ADB USB seleccionado.",
                nameof(usbSerial));
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port));
        }

        var addrOutput =
            await ShellAsync(
                usbSerial,
                "ip -4 -o addr show wlan0 scope global",
                cancellationToken);

        var ipMatch =
            Regex.Match(
                addrOutput,
                @"\binet\s+(\d{1,3}(?:\.\d{1,3}){3})/");

        if (!ipMatch.Success)
        {
            var routeOutput =
                await ShellAsync(
                    usbSerial,
                    "ip -4 route get 1.1.1.1",
                    cancellationToken);

            ipMatch =
                Regex.Match(
                    routeOutput,
                    @"\bsrc\s+(\d{1,3}(?:\.\d{1,3}){3})\b");
        }

        if (!ipMatch.Success)
        {
            throw new InvalidOperationException(
                "NOVORA no pudo obtener la IP Wi-Fi del teléfono. " +
                "Conéctalo a una red Wi-Fi y vuelve a intentarlo.");
        }

        var ip =
            ipMatch.Groups[1].Value;

        await RunAsync(
            new[]
            {
                "-s",
                usbSerial,
                "tcpip",
                port.ToString(
                    CultureInfo.InvariantCulture)
            },
            cancellationToken);

        var endpoint =
            $"{ip}:{port}";

        var lastOutput =
            string.Empty;

        for (var attempt = 0;
             attempt < 7;
             attempt++)
        {
            await Task.Delay(
                attempt == 0
                    ? 1200
                    : 700,
                cancellationToken);

            try
            {
                lastOutput =
                    await RunAsync(
                        new[]
                        {
                            "connect",
                            endpoint
                        },
                        cancellationToken);

                if (lastOutput.Contains(
                        "connected to",
                        StringComparison.OrdinalIgnoreCase) ||
                    lastOutput.Contains(
                        "already connected",
                        StringComparison.OrdinalIgnoreCase))
                {
                    InvalidateDeviceCache();

                    return endpoint;
                }
            }
            catch (Exception ex)
            {
                lastOutput =
                    ex.Message;
            }
        }

        throw new InvalidOperationException(
            $"ADB no pudo conectar por Wi-Fi a {endpoint}. " +
            $"{lastOutput.Trim()}");
    }

    public async Task StopServerIfNoOtherDevicesAsync(
        string? excludedSerial = null,
        CancellationToken cancellationToken = default)
    {
        using var cleanupCts =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        cleanupCts.CancelAfter(
            TimeSpan.FromSeconds(3));

        try
        {
            var serials =
                await GetConnectedSerialsAsync(
                    cleanupCts.Token);

            if (!serials.Any(
                    s => !string.Equals(
                        s,
                        excludedSerial,
                        StringComparison.OrdinalIgnoreCase)))
            {
                await RunAsync(
                    new[]
                    {
                        "kill-server"
                    },
                    cleanupCts.Token);

                _serverStarted = false;

                InvalidateDeviceCache();
            }
        }
        catch
        {
            // El cierre no debe fallar por la limpieza de ADB.
        }
    }

    public Task<string> GetStateAsync(
        string serial,
        CancellationToken cancellationToken = default)
        => RunAsync(
            new[]
            {
                "-s",
                serial,
                "get-state"
            },
            cancellationToken);

    public Task<string> InstallAsync(
        string serial,
        string apkPath,
        CancellationToken cancellationToken = default)
        => RunAsync(
            new[]
            {
                "-s",
                serial,
                "install",
                "-r",
                apkPath
            },
            cancellationToken);

    public Task<string> PullAsync(
        string serial,
        string remotePath,
        string localPath,
        CancellationToken cancellationToken = default)
        => RunAsync(
            new[]
            {
                "-s",
                serial,
                "pull",
                remotePath,
                localPath
            },
            cancellationToken);

    public Task<string> PushAsync(
        string serial,
        string localPath,
        string remotePath,
        CancellationToken cancellationToken = default)
        => RunAsync(
            new[]
            {
                "-s",
                serial,
                "push",
                localPath,
                remotePath
            },
            cancellationToken);

    public async Task<byte[]> CaptureScreenAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        _paths.ValidateRequiredTools();

        var info =
            CreateStartInfo(
                new[]
                {
                    "-s",
                    serial,
                    "exec-out",
                    "screencap",
                    "-p"
                });

        using var process =
            Process.Start(info)
            ?? throw new InvalidOperationException(
                "No fue posible iniciar ADB.");

        await using var output =
            new MemoryStream();

        // Iniciamos la lectura de stdout y stderr ANTES de esperar al proceso.
        var copyTask =
            process.StandardOutput
                .BaseStream
                .CopyToAsync(
                    output,
                    cancellationToken);

        var errorTask =
            process.StandardError
                .ReadToEndAsync(
                    cancellationToken);

        // CAMBIO AQUÍ: Usamos Task.WhenAll para esperar a que terminen 
        // las lecturas y el proceso, garantizando que nunca haya un deadlock.
        await Task.WhenAll(
            copyTask,
            errorTask,
            process.WaitForExitAsync(
                cancellationToken));

        // Ahora es seguro leer los resultados.
        var error =
            await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? "ADB no pudo capturar la pantalla."
                    : error.Trim());
        }

        return output.ToArray();
    }

    public Task<string> ShellAsync(
        string serial,
        string command,
        CancellationToken cancellationToken = default)
        => RunAsync(
            new[]
            {
                "-s",
                serial,
                "shell",
                command
            },
            cancellationToken);

    public Task<string> ExecuteRawAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
        => RunAsync(
            arguments,
            cancellationToken);

    public void InvalidateDeviceCache()
    {
        _deviceCacheAt =
            DateTimeOffset.MinValue;
    }

    private async Task<IReadOnlyList<string>>
        QueryConnectedSerialsAsync(
            CancellationToken cancellationToken)
    {
        var output =
            await RunAsync(
                new[]
                {
                    "devices"
                },
                cancellationToken);

        var serials =
            new List<string>();

        foreach (var line in output.Split(
                     '\n',
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith(
                    "List of devices attached",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts =
                line.Split(
                    '\t',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

            if (parts.Length >= 2 &&
                string.Equals(
                    parts[1],
                    "device",
                    StringComparison.OrdinalIgnoreCase))
            {
                serials.Add(
                    parts[0]);
            }
        }

        return serials;
    }

    private async Task<DeviceInfo> ReadDeviceAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        var props =
            await ShellAsync(
                serial,
                "printf '%s\\n' " +
                "\"$(getprop ro.product.model)\" " +
                "\"$(getprop ro.build.version.release)\" " +
                "\"$(getprop ro.build.display.id)\"",
                cancellationToken);

        var values =
            props.Split(
                '\n',
                StringSplitOptions.TrimEntries);

        var model =
            values.ElementAtOrDefault(0)
            ?? string.Empty;

        var android =
            values.ElementAtOrDefault(1)
            ?? string.Empty;

        var build =
            values.ElementAtOrDefault(2)
            ?? string.Empty;

        var display =
            await GetDisplayModesAsync(
                serial,
                cancellationToken);

        return new DeviceInfo
        {
            Serial = serial,

            Model =
                string.IsNullOrWhiteSpace(model)
                    ? "Dispositivo Android"
                    : model.Replace('_', ' '),

            AndroidVersion =
                android,

            Build =
                build,

            Connected =
                true,

            SupportedDisplayModes =
                display,

            BestDisplayMode =
                display
                    .OrderByDescending(
                        m => m.Pixels)
                    .ThenByDescending(
                        m => m.RefreshRateHz)
                    .FirstOrDefault()
        };
    }

    private async Task<IReadOnlyList<DisplayModeInfo>>
    GetDisplayModesAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        var modes =
            new HashSet<DisplayModeInfo>();

        // ============================================================
        // 1. DUMPSYS DISPLAY
        // ============================================================

        try
        {
            var dumpsys =
                await ShellAsync(
                    serial,
                    "dumpsys display",
                    cancellationToken);

            foreach (var line in dumpsys.Split(
                         '\n',
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var resolutionMatch =
                    Regex.Match(
                        line,
                        @"(?<!\d)(\d{3,5})\s*[xX×]\s*(\d{3,5})(?!\d)",
                        RegexOptions.IgnoreCase);

                if (!resolutionMatch.Success)
                    continue;

                if (!int.TryParse(
                        resolutionMatch.Groups[1].Value,
                        out var width))
                {
                    continue;
                }

                if (!int.TryParse(
                        resolutionMatch.Groups[2].Value,
                        out var height))
                {
                    continue;
                }

                if (width < 320 ||
                    height < 320 ||
                    width > 10000 ||
                    height > 10000)
                {
                    continue;
                }

                double hz = 60;

                var hzMatch =
                    Regex.Match(
                        line,
                        @"(?<!\d)(\d+(?:\.\d+)?)\s*(?:Hz|fps)(?!\w)",
                        RegexOptions.IgnoreCase);

                if (hzMatch.Success)
                {
                    double.TryParse(
                        hzMatch.Groups[1].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out hz);
                }
                else
                {
                    var refreshMatch =
                        Regex.Match(
                            line,
                            @"(?:refreshRate|refresh_rate|fps)\s*[=:]\s*(\d+(?:\.\d+)?)",
                            RegexOptions.IgnoreCase);

                    if (refreshMatch.Success)
                    {
                        double.TryParse(
                            refreshMatch.Groups[1].Value,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out hz);
                    }
                }

                if (hz < 20 ||
                    hz > 360)
                {
                    hz = 60;
                }

                modes.Add(
                    new DisplayModeInfo(
                        width,
                        height,
                        hz));
            }
        }
        catch
        {
            // Continuamos con los métodos de respaldo.
        }

        // ============================================================
        // 2. SURFACEFLINGER
        // ============================================================

        if (modes.Count == 0)
        {
            try
            {
                var surfaceFlinger =
                    await ShellAsync(
                        serial,
                        "dumpsys SurfaceFlinger",
                        cancellationToken);

                foreach (var line in surfaceFlinger.Split(
                             '\n',
                             StringSplitOptions.RemoveEmptyEntries |
                             StringSplitOptions.TrimEntries))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var resolutionMatch =
                        Regex.Match(
                            line,
                            @"(?<!\d)(\d{3,5})\s*[xX×]\s*(\d{3,5})(?!\d)",
                            RegexOptions.IgnoreCase);

                    if (!resolutionMatch.Success)
                        continue;

                    if (!int.TryParse(
                            resolutionMatch.Groups[1].Value,
                            out var width))
                    {
                        continue;
                    }

                    if (!int.TryParse(
                            resolutionMatch.Groups[2].Value,
                            out var height))
                    {
                        continue;
                    }

                    if (width < 320 ||
                        height < 320 ||
                        width > 10000 ||
                        height > 10000)
                    {
                        continue;
                    }

                    double hz = 60;

                    var hzMatch =
                        Regex.Match(
                            line,
                            @"(?<!\d)(\d+(?:\.\d+)?)\s*(?:Hz|fps)(?!\w)",
                            RegexOptions.IgnoreCase);

                    if (hzMatch.Success)
                    {
                        double.TryParse(
                            hzMatch.Groups[1].Value,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out hz);
                    }

                    if (hz < 20 ||
                        hz > 360)
                    {
                        hz = 60;
                    }

                    modes.Add(
                        new DisplayModeInfo(
                            width,
                            height,
                            hz));
                }
            }
            catch
            {
                // Continuamos con wm size.
            }
        }

        // ============================================================
        // 3. WM SIZE
        // ============================================================

        int physicalWidth = 0;
        int physicalHeight = 0;

        try
        {
            var size =
                await ShellAsync(
                    serial,
                    "wm size",
                    cancellationToken);

            var match =
                Regex.Match(
                    size,
                    @"Physical size:\s*(\d+)\s*x\s*(\d+)",
                    RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                match =
                    Regex.Match(
                        size,
                        @"Override size:\s*(\d+)\s*x\s*(\d+)",
                        RegexOptions.IgnoreCase);
            }

            if (match.Success)
            {
                int.TryParse(
                    match.Groups[1].Value,
                    out physicalWidth);

                int.TryParse(
                    match.Groups[2].Value,
                    out physicalHeight);
            }
        }
        catch
        {
            // Continuamos.
        }

        // ============================================================
        // 4. REFRESH RATE
        // ============================================================

        double refreshRate = 60;

        try
        {
            var refresh =
                await ShellAsync(
                    serial,
                    "settings get system peak_refresh_rate",
                    cancellationToken);

            if (!double.TryParse(
                    refresh.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out refreshRate) ||
                refreshRate <= 0)
            {
                refreshRate = 60;
            }
        }
        catch
        {
            refreshRate = 60;
        }

        if (refreshRate < 20 ||
            refreshRate > 360)
        {
            refreshRate = 60;
        }

        // ============================================================
        // 5. SI NO ENCONTRAMOS MODOS, USAMOS WM SIZE
        // ============================================================

        if (modes.Count == 0 &&
            physicalWidth >= 320 &&
            physicalHeight >= 320)
        {
            modes.Add(
                new DisplayModeInfo(
                    physicalWidth,
                    physicalHeight,
                    refreshRate));
        }

        // ============================================================
        // 6. AÑADIR LA RESOLUCIÓN ACTUAL COMO MODO
        // ============================================================

        if (physicalWidth >= 320 &&
            physicalHeight >= 320)
        {
            modes.Add(
                new DisplayModeInfo(
                    physicalWidth,
                    physicalHeight,
                    refreshRate));
        }

        // ============================================================
        // 7. FILTRADO FINAL
        // ============================================================

        return modes
            .Where(
                m =>
                    m.Width >= 320 &&
                    m.Height >= 320 &&
                    m.Width <= 10000 &&
                    m.Height <= 10000 &&
                    m.RefreshRateHz >= 20 &&
                    m.RefreshRateHz <= 360)
            .OrderByDescending(
                m => m.Pixels)
            .ThenByDescending(
                m => m.RefreshRateHz)
            .ToArray();
    }

    private ProcessStartInfo CreateStartInfo(
        IEnumerable<string> arguments)
    {
        var info =
            new ProcessStartInfo
            {
                FileName =
                    _paths.Adb,

                WorkingDirectory =
                    _paths.ToolsDirectory,

                UseShellExecute =
                    false,

                CreateNoWindow =
                    true,

                RedirectStandardOutput =
                    true,

                RedirectStandardError =
                    true,

                StandardOutputEncoding =
                    Encoding.UTF8,

                StandardErrorEncoding =
                    Encoding.UTF8
            };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(
                argument);
        }

        return info;
    }

    private async Task<string> RunAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        _paths.ValidateRequiredTools();

        var argumentList =
            arguments.ToArray();

        var info =
            CreateStartInfo(
                argumentList);

        using var process =
            Process.Start(info)
            ?? throw new InvalidOperationException(
                "No fue posible iniciar ADB.");

        var stdout =
            process.StandardOutput
                .ReadToEndAsync(
                    cancellationToken);

        var stderr =
            process.StandardError
                .ReadToEndAsync(
                    cancellationToken);

        await process.WaitForExitAsync(
            cancellationToken);

        var output =
            await stdout;

        var error =
            await stderr;

        /*
         * ping puede devolver código 1 cuando el destino
         * no responde. Eso representa falta de conectividad,
         * no un fallo de ADB.
         */
        var isPingCommand =
            argumentList.Any(a =>
                Regex.IsMatch(
                    a,
                    @"(?:^|\s)ping(?:\s|$)",
                    RegexOptions.IgnoreCase));

        if (process.ExitCode != 0 &&
            !isPingCommand)
        {
            var message =
                string.IsNullOrWhiteSpace(error)
                    ? output.Trim()
                    : error.Trim();

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(message)
                    ? $"ADB terminó con código {process.ExitCode}."
                    : message);
        }

        /*
         * Para ping devolvemos igualmente la salida,
         * incluso cuando hubo pérdida de paquetes.
         */
        if (!string.IsNullOrWhiteSpace(output))
            return output;

        return error;
    }
}