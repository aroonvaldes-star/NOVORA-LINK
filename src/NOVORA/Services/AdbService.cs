using NOVORA.Models;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

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

    /// <summary>
    /// Ejecuta un comando shell opcional. Si el fabricante no soporta el
    /// comando o ADB devuelve un error para esta consulta de metadatos,
    /// NOVORA continúa con un valor vacío en lugar de perder el dispositivo.
    /// </summary>
    private async Task<string> SafeShellAsync(
        string serial,
        string command,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ShellAsync(
                serial,
                command,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return string.Empty;
        }
    }

    public Task<string> ExecuteRawAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
        => RunAsync(
            arguments,
            cancellationToken);

    /// <summary>
    /// Ejecuta un comando ADB dirigido a un dispositivo concreto.
    /// El comando se envía a Android mediante "adb -s SERIAL shell ...".
    /// Esta sobrecarga evita que Recovery, NCP y otros servicios tengan
    /// que construir manualmente el prefijo -s/serial/shell.
    /// </summary>
    public Task<string> ExecuteRawAsync(
        string serial,
        string shellCommand,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException(
                "El serial ADB no puede estar vacío.",
                nameof(serial));

        if (string.IsNullOrWhiteSpace(shellCommand))
            throw new ArgumentException(
                "El comando ADB no puede estar vacío.",
                nameof(shellCommand));

        return RunAsync(
            new[]
            {
                "-s",
                serial.Trim(),
                "shell",
                shellCommand
            },
            cancellationToken);
    }

    /// <summary>
    /// Comprueba de forma ligera si ADB sigue viendo al dispositivo como
    /// conectado y autorizado. No refresca la lista completa de dispositivos.
    /// </summary>
    public async Task<bool> IsDeviceOnlineAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return false;

        try
        {
            var state = await GetStateAsync(
                serial.Trim(),
                cancellationToken);

            return string.Equals(
                state.Trim(),
                "device",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Comprueba conectividad IP desde Android sin convertir una pérdida
    /// de paquetes en una excepción de ADB.
    /// </summary>
    public async Task<bool> PingAsync(
        string serial,
        string host = "1.1.1.1",
        int timeoutSeconds = 2,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return false;

        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException(
                "El host no puede estar vacío.",
                nameof(host));

        timeoutSeconds = Math.Clamp(
            timeoutSeconds,
            1,
            10);

        try
        {
            var output = await RunAsync(
                new[]
                {
                    "-s",
                    serial.Trim(),
                    "shell",
                    "ping",
                    "-c",
                    "1",
                    "-W",
                    timeoutSeconds.ToString(
                        CultureInfo.InvariantCulture),
                    host.Trim()
                },
                cancellationToken);

            if (string.IsNullOrWhiteSpace(output))
                return false;

            return output.Contains(
                       "bytes from",
                       StringComparison.OrdinalIgnoreCase) ||
                   output.Contains(
                       "1 received",
                       StringComparison.OrdinalIgnoreCase) ||
                   output.Contains(
                       "1 packet received",
                       StringComparison.OrdinalIgnoreCase) ||
                   output.Contains(
                       "1 packets received",
                       StringComparison.OrdinalIgnoreCase) ||
                   output.Contains(
                       "0% packet loss",
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

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
        // No agrupamos los getprop dentro de printf/subshell. Algunos shells
        // de Android (especialmente builds Samsung) pueden devolver 255 con
        // expresiones complejas enviadas como un único argumento a adb shell.
        // Además, estos datos son metadatos opcionales: un fallo aquí nunca
        // debe impedir que el dispositivo aparezca en NOVORA.
        var model =
            await SafeShellAsync(
                serial,
                "getprop ro.product.model",
                cancellationToken);

        var android =
            await SafeShellAsync(
                serial,
                "getprop ro.build.version.release",
                cancellationToken);

        var build =
            await SafeShellAsync(
                serial,
                "getprop ro.build.display.id",
                cancellationToken);

        model = model.Trim();
        android = android.Trim();
        build = build.Trim();

        var display =
            await GetDisplayModesAsync(
                serial,
                cancellationToken);

        var capabilities =
            await GetDeviceCapabilitiesAsync(
                serial,
                display,
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
                    .FirstOrDefault(),

            Capabilities =
                capabilities
        };
    }

    /// <summary>
    /// Detecta los límites físicos del panel Android sin cruzarlos con
    /// las capacidades del monitor del PC.
    /// </summary>
    private async Task<DeviceCapabilities> GetDeviceCapabilitiesAsync(
        string serial,
        IReadOnlyList<DisplayModeInfo> detectedModes,
        CancellationToken cancellationToken)
    {
        int nativeWidth = 0;
        int nativeHeight = 0;

        var refreshRates =
            new HashSet<double>();

        // 1. Resolución física reportada por Android.
        try
        {
            var wmSize =
                await ShellAsync(
                    serial,
                    "wm size",
                    cancellationToken);

            var physical =
                Regex.Match(
                    wmSize,
                    @"Physical size:\s*(\d+)\s*x\s*(\d+)",
                    RegexOptions.IgnoreCase);

            if (!physical.Success)
            {
                physical =
                    Regex.Match(
                        wmSize,
                        @"(?<!\d)(\d{3,5})\s*x\s*(\d{3,5})(?!\d)",
                        RegexOptions.IgnoreCase);
            }

            if (physical.Success)
            {
                int.TryParse(
                    physical.Groups[1].Value,
                    out nativeWidth);

                int.TryParse(
                    physical.Groups[2].Value,
                    out nativeHeight);
            }
        }
        catch
        {
            // Se usa detectedModes como respaldo.
        }

        // 2. Frecuencias reales anunciadas por DisplayManager.
        try
        {
            var dumpsysDisplay =
                await ShellAsync(
                    serial,
                    "dumpsys display",
                    cancellationToken);

            CollectRefreshRates(
                dumpsysDisplay,
                refreshRates);
        }
        catch
        {
            // Continuamos con settings y modos detectados.
        }

        // 3. Peak/min definidos por Android. Peak es especialmente útil
        //    en teléfonos de 90/120/144/165 Hz.
        await TryAddRefreshSettingAsync(
            serial,
            "peak_refresh_rate",
            refreshRates,
            cancellationToken);

        await TryAddRefreshSettingAsync(
            serial,
            "min_refresh_rate",
            refreshRates,
            cancellationToken);

        // 4. Los modos que ya detectó NOVORA también son evidencia válida.
        foreach (var mode in detectedModes)
        {
            AddRefreshRate(
                refreshRates,
                mode.RefreshRateHz);
        }

        // 5. Si wm size no respondió, usamos la resolución de mayor área
        //    detectada en los modos del dispositivo.
        if ((nativeWidth < 320 || nativeHeight < 320) &&
            detectedModes.Count > 0)
        {
            var nativeFallback =
                detectedModes
                    .Where(mode =>
                        mode.Width >= 320 &&
                        mode.Height >= 320)
                    .OrderByDescending(mode => mode.Pixels)
                    .FirstOrDefault();

            if (nativeFallback is not null)
            {
                nativeWidth =
                    nativeFallback.Width;

                nativeHeight =
                    nativeFallback.Height;
            }
        }

        // No inventamos 120 Hz. Si Android no expone nada utilizable,
        // dejamos 60 Hz como fallback conservador.
        if (refreshRates.Count == 0)
        {
            refreshRates.Add(60d);
        }

        return new DeviceCapabilities
        {
            NativeWidth =
                nativeWidth,

            NativeHeight =
                nativeHeight,

            SupportedRefreshRatesHz =
                refreshRates
                    .OrderBy(value => value)
                    .ToArray()
        };
    }

    private async Task TryAddRefreshSettingAsync(
        string serial,
        string settingName,
        ISet<double> destination,
        CancellationToken cancellationToken)
    {
        try
        {
            var raw =
                await ShellAsync(
                    serial,
                    $"settings get system {settingName}",
                    cancellationToken);

            if (double.TryParse(
                    raw.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                AddRefreshRate(
                    destination,
                    value);
            }
        }
        catch
        {
            // La clave puede no existir en algunos fabricantes.
        }
    }

    private static void CollectRefreshRates(
        string text,
        ISet<double> destination)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string[] patterns =
        {
            @"(?:refreshRate|refresh_rate|fps|vsyncRate)\s*[=:]\s*(\d+(?:\.\d+)?)",
            @"(?<!\d)(\d+(?:\.\d+)?)\s*(?:Hz|fps)(?!\w)"
        };

        foreach (var pattern in patterns)
        {
            foreach (Match match in Regex.Matches(
                         text,
                         pattern,
                         RegexOptions.IgnoreCase))
            {
                if (!match.Success ||
                    match.Groups.Count < 2)
                {
                    continue;
                }

                if (!double.TryParse(
                        match.Groups[1].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var value))
                {
                    continue;
                }

                AddRefreshRate(
                    destination,
                    value);
            }
        }
    }

    private static void AddRefreshRate(
        ISet<double> destination,
        double value)
    {
        if (double.IsNaN(value) ||
            double.IsInfinity(value) ||
            value < 20 ||
            value > 360)
        {
            return;
        }

        // 59.94 y 60.00 deben representar la misma opción humana.
        double normalized =
            Math.Round(
                value,
                1,
                MidpointRounding.AwayFromZero);

        var equivalent =
            destination.FirstOrDefault(existing =>
                Math.Abs(existing - normalized) < 0.6);

        if (equivalent > 0)
        {
            if (normalized > equivalent)
            {
                destination.Remove(equivalent);
                destination.Add(normalized);
            }

            return;
        }

        destination.Add(normalized);
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