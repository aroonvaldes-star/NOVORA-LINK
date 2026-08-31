using NOVORA.Models;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NOVORA.Services;

/// <summary>Punto único de acceso de NOVORA a ADB.</summary>
public sealed class AdbService
{
    private static readonly TimeSpan DeviceCacheLifetime = TimeSpan.FromSeconds(2);

    private readonly NovoraPaths _paths;
    private readonly SemaphoreSlim _serverGate = new(1, 1);
    private readonly SemaphoreSlim _deviceGate = new(1, 1);
    private IReadOnlyList<DeviceInfo>? _deviceCache;
    private DateTimeOffset _deviceCacheAt = DateTimeOffset.MinValue;
    private bool _serverStarted;

    public AdbService(NovoraPaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        if (!forceRefresh && _deviceCache is not null && DateTimeOffset.UtcNow - _deviceCacheAt < DeviceCacheLifetime)
            return _deviceCache;

        await _deviceGate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _deviceCache is not null && DateTimeOffset.UtcNow - _deviceCacheAt < DeviceCacheLifetime)
                return _deviceCache;

            await StartServerAsync(cancellationToken);
            var serials = await QueryConnectedSerialsAsync(cancellationToken);
            var devices = new List<DeviceInfo>(serials.Count);
            foreach (var serial in serials)
            {
                cancellationToken.ThrowIfCancellationRequested();
                devices.Add(await ReadDeviceAsync(serial, cancellationToken));
            }

            _deviceCache = devices;
            _deviceCacheAt = DateTimeOffset.UtcNow;
            return devices;
        }
        finally
        {
            _deviceGate.Release();
        }
    }

    public async Task StartServerAsync(CancellationToken cancellationToken = default)
    {
        if (_serverStarted) return;
        await _serverGate.WaitAsync(cancellationToken);
        try
        {
            if (_serverStarted) return;
            await RunAsync(new[] { "start-server" }, cancellationToken);
            _serverStarted = true;
        }
        finally
        {
            _serverGate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetConnectedSerialsAsync(CancellationToken cancellationToken = default)
    {
        await StartServerAsync(cancellationToken);
        return await QueryConnectedSerialsAsync(cancellationToken);
    }

    public async Task<string> ConnectOverWifiAsync(string usbSerial, int port = 5555, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usbSerial)) throw new ArgumentException("No hay un dispositivo ADB USB seleccionado.", nameof(usbSerial));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));

        var addrOutput = await ShellAsync(usbSerial, "ip -4 -o addr show wlan0 scope global", cancellationToken);
        var ipMatch = Regex.Match(addrOutput, @"\binet\s+(\d{1,3}(?:\.\d{1,3}){3})/");
        if (!ipMatch.Success)
        {
            var routeOutput = await ShellAsync(usbSerial, "ip -4 route get 1.1.1.1", cancellationToken);
            ipMatch = Regex.Match(routeOutput, @"\bsrc\s+(\d{1,3}(?:\.\d{1,3}){3})\b");
        }
        if (!ipMatch.Success)
            throw new InvalidOperationException("NOVORA no pudo obtener la IP Wi-Fi del teléfono. Conéctalo a una red Wi-Fi y vuelve a intentarlo.");

        var endpoint = $"{ipMatch.Groups[1].Value}:{port}";
        await RunAsync(new[] { "-s", usbSerial, "tcpip", port.ToString(CultureInfo.InvariantCulture) }, cancellationToken);

        var lastOutput = string.Empty;
        for (var attempt = 0; attempt < 7; attempt++)
        {
            await Task.Delay(attempt == 0 ? 1200 : 700, cancellationToken);
            try
            {
                lastOutput = await RunAsync(new[] { "connect", endpoint }, cancellationToken);
                if (lastOutput.Contains("connected to", StringComparison.OrdinalIgnoreCase) ||
                    lastOutput.Contains("already connected", StringComparison.OrdinalIgnoreCase))
                {
                    InvalidateDeviceCache();
                    return endpoint;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastOutput = ex.Message;
            }
        }

        throw new InvalidOperationException($"ADB no pudo conectar por Wi-Fi a {endpoint}. {lastOutput.Trim()}");
    }

    public async Task StopServerIfNoOtherDevicesAsync(string? excludedSerial = null, CancellationToken cancellationToken = default)
    {
        using var cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cleanupCts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var serials = await GetConnectedSerialsAsync(cleanupCts.Token);
            if (!serials.Any(s => !string.Equals(s, excludedSerial, StringComparison.OrdinalIgnoreCase)))
            {
                await RunAsync(new[] { "kill-server" }, cleanupCts.Token);
                _serverStarted = false;
                InvalidateDeviceCache();
            }
        }
        catch { }
    }

    public Task<string> GetStateAsync(string serial, CancellationToken cancellationToken = default)
        => RunAsync(new[] { "-s", serial, "get-state" }, cancellationToken);

    public Task<string> InstallAsync(string serial, string apkPath, CancellationToken cancellationToken = default)
        => RunAsync(new[] { "-s", serial, "install", "-r", apkPath }, cancellationToken);

    public Task<string> PullAsync(string serial, string remotePath, string localPath, CancellationToken cancellationToken = default)
        => RunAsync(new[] { "-s", serial, "pull", remotePath, localPath }, cancellationToken);

    public Task<string> PushAsync(string serial, string localPath, string remotePath, CancellationToken cancellationToken = default)
        => RunAsync(new[] { "-s", serial, "push", localPath, remotePath }, cancellationToken);

    public async Task<byte[]> CaptureScreenAsync(string serial, CancellationToken cancellationToken = default)
    {
        _paths.ValidateAdbTools();
        var info = CreateStartInfo(new[] { "-s", serial, "exec-out", "screencap", "-p" });
        using var process = Process.Start(info) ?? throw new InvalidOperationException("No fue posible iniciar ADB.");
        await using var output = new MemoryStream();
        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(copyTask, errorTask, process.WaitForExitAsync(cancellationToken));
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "ADB no pudo capturar la pantalla." : error.Trim());
        return output.ToArray();
    }

    public Task<string> ShellAsync(string serial, string command, CancellationToken cancellationToken = default)
        => RunAsync(new[] { "-s", serial, "shell", command }, cancellationToken);

    public Task<string> ExecuteRawAsync(IEnumerable<string> arguments, CancellationToken cancellationToken = default)
        => RunAsync(arguments, cancellationToken);

    public Task<string> ExecuteRawAsync(string serial, string shellCommand, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial)) throw new ArgumentException("El serial ADB no puede estar vacío.", nameof(serial));
        if (string.IsNullOrWhiteSpace(shellCommand)) throw new ArgumentException("El comando ADB no puede estar vacío.", nameof(shellCommand));
        return RunAsync(new[] { "-s", serial.Trim(), "shell", shellCommand }, cancellationToken);
    }

    public async Task<bool> IsDeviceOnlineAsync(string serial, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial)) return false;
        try
        {
            var state = await GetStateAsync(serial.Trim(), cancellationToken);
            return string.Equals(state.Trim(), "device", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    public async Task<bool> PingAsync(string serial, string host = "1.1.1.1", int timeoutSeconds = 2, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial)) return false;
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("El host no puede estar vacío.", nameof(host));
        timeoutSeconds = Math.Clamp(timeoutSeconds, 1, 10);
        try
        {
            var output = await RunAsync(new[]
            {
                "-s", serial.Trim(), "shell", "ping", "-c", "1", "-W",
                timeoutSeconds.ToString(CultureInfo.InvariantCulture), host.Trim()
            }, cancellationToken);
            return output.Contains("bytes from", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("1 received", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("1 packet received", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("1 packets received", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("0% packet loss", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    public void InvalidateDeviceCache()
    {
        _deviceCache = null;
        _deviceCacheAt = DateTimeOffset.MinValue;
    }

    private async Task<string> SafeShellAsync(string serial, string command, CancellationToken cancellationToken)
    {
        try { return await ShellAsync(serial, command, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch { return string.Empty; }
    }

    private async Task<IReadOnlyList<string>> QueryConnectedSerialsAsync(CancellationToken cancellationToken)
    {
        var output = await RunAsync(new[] { "devices", "-l" }, cancellationToken);
        var serials = new List<string>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && string.Equals(parts[1], "device", StringComparison.OrdinalIgnoreCase))
                serials.Add(parts[0].Trim());
        }
        return serials;
    }

    private async Task<DeviceInfo> ReadDeviceAsync(string serial, CancellationToken cancellationToken)
    {
        var model = (await SafeShellAsync(serial, "getprop ro.product.model", cancellationToken)).Trim();
        var android = (await SafeShellAsync(serial, "getprop ro.build.version.release", cancellationToken)).Trim();
        var build = (await SafeShellAsync(serial, "getprop ro.build.display.id", cancellationToken)).Trim();
        var modes = await GetDisplayModesAsync(serial, cancellationToken);
        var capabilities = await GetDeviceCapabilitiesAsync(serial, modes, cancellationToken);

        return new DeviceInfo
        {
            Serial = serial,
            Model = string.IsNullOrWhiteSpace(model) ? "Dispositivo Android" : model.Replace('_', ' '),
            AndroidVersion = android,
            Build = build,
            Connected = true,
            SupportedDisplayModes = modes,
            BestDisplayMode = modes.OrderByDescending(mode => mode.Pixels).ThenByDescending(mode => mode.RefreshRateHz).FirstOrDefault(),
            Capabilities = capabilities
        };
    }

    private async Task<DeviceCapabilities> GetDeviceCapabilitiesAsync(string serial, IReadOnlyList<DisplayModeInfo> detectedModes, CancellationToken cancellationToken)
    {
        var nativeWidth = 0;
        var nativeHeight = 0;
        var refreshRates = new HashSet<double>();

        var wmSize = await SafeShellAsync(serial, "wm size", cancellationToken);
        var physical = Regex.Match(wmSize, @"Physical size:\s*(\d+)\s*x\s*(\d+)", RegexOptions.IgnoreCase);
        if (!physical.Success) physical = Regex.Match(wmSize, @"(?<!\d)(\d{3,5})\s*x\s*(\d{3,5})(?!\d)", RegexOptions.IgnoreCase);
        if (physical.Success)
        {
            int.TryParse(physical.Groups[1].Value, out nativeWidth);
            int.TryParse(physical.Groups[2].Value, out nativeHeight);
        }

        var display = await SafeShellAsync(serial, "dumpsys display", cancellationToken);
        CollectRefreshRates(display, refreshRates);
        await TryAddRefreshSettingAsync(serial, "peak_refresh_rate", refreshRates, cancellationToken);
        await TryAddRefreshSettingAsync(serial, "min_refresh_rate", refreshRates, cancellationToken);
        foreach (var mode in detectedModes) AddRefreshRate(refreshRates, mode.RefreshRateHz);

        if ((nativeWidth < 320 || nativeHeight < 320) && detectedModes.Count > 0)
        {
            var fallback = detectedModes.Where(mode => mode.Width >= 320 && mode.Height >= 320)
                .OrderByDescending(mode => mode.Pixels).FirstOrDefault();
            if (fallback is not null)
            {
                nativeWidth = fallback.Width;
                nativeHeight = fallback.Height;
            }
        }
        if (refreshRates.Count == 0) refreshRates.Add(60d);

        return new DeviceCapabilities
        {
            NativeWidth = nativeWidth,
            NativeHeight = nativeHeight,
            SupportedRefreshRatesHz = refreshRates.OrderBy(value => value).ToArray()
        };
    }

    private async Task TryAddRefreshSettingAsync(string serial, string settingName, ISet<double> destination, CancellationToken cancellationToken)
    {
        var raw = await SafeShellAsync(serial, $"settings get system {settingName}", cancellationToken);
        if (double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            AddRefreshRate(destination, value);
    }

    private static void CollectRefreshRates(string text, ISet<double> destination)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        foreach (var pattern in new[]
        {
            @"(?:refreshRate|refresh_rate|fps|vsyncRate)\s*[=:]\s*(\d+(?:\.\d+)?)",
            @"(?<!\d)(\d+(?:\.\d+)?)\s*(?:Hz|fps)(?!\w)"
        })
        {
            foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    AddRefreshRate(destination, value);
        }
    }

    private static void AddRefreshRate(ISet<double> destination, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 20 || value > 360) return;
        var normalized = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        var equivalent = destination.FirstOrDefault(existing => Math.Abs(existing - normalized) < 0.6);
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

    private async Task<IReadOnlyList<DisplayModeInfo>> GetDisplayModesAsync(string serial, CancellationToken cancellationToken)
    {
        var modes = new HashSet<DisplayModeInfo>();
        var dumpsys = await SafeShellAsync(serial, "dumpsys display", cancellationToken);
        foreach (var line in dumpsys.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var resolution = Regex.Match(line, @"(?<!\d)(\d{3,5})\s*[xX×]\s*(\d{3,5})(?!\d)");
            if (!resolution.Success || !int.TryParse(resolution.Groups[1].Value, out var width) || !int.TryParse(resolution.Groups[2].Value, out var height)) continue;
            if (width < 320 || height < 320 || width > 10000 || height > 10000) continue;
            var hz = 60d;
            var refresh = Regex.Match(line, @"(?:refreshRate|refresh_rate|fps)\s*[=:]\s*(\d+(?:\.\d+)?)|(?<!\d)(\d+(?:\.\d+)?)\s*(?:Hz|fps)(?!\w)", RegexOptions.IgnoreCase);
            var token = refresh.Success ? (refresh.Groups[1].Success ? refresh.Groups[1].Value : refresh.Groups[2].Value) : string.Empty;
            if (!string.IsNullOrWhiteSpace(token) && double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) hz = parsed;
            if (hz < 20 || hz > 360) hz = 60d;
            modes.Add(new DisplayModeInfo(width, height, hz));
        }

        var size = await SafeShellAsync(serial, "wm size", cancellationToken);
        var sizeMatch = Regex.Match(size, @"Physical size:\s*(\d+)\s*x\s*(\d+)", RegexOptions.IgnoreCase);
        if (!sizeMatch.Success) sizeMatch = Regex.Match(size, @"Override size:\s*(\d+)\s*x\s*(\d+)", RegexOptions.IgnoreCase);
        if (sizeMatch.Success && int.TryParse(sizeMatch.Groups[1].Value, out var physicalWidth) && int.TryParse(sizeMatch.Groups[2].Value, out var physicalHeight))
        {
            var peakRaw = await SafeShellAsync(serial, "settings get system peak_refresh_rate", cancellationToken);
            var peak = double.TryParse(peakRaw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedPeak) && parsedPeak is >= 20 and <= 360 ? parsedPeak : 60d;
            modes.Add(new DisplayModeInfo(physicalWidth, physicalHeight, peak));
        }

        return modes.Where(mode => mode.Width >= 320 && mode.Height >= 320 && mode.RefreshRateHz is >= 20 and <= 360)
            .OrderByDescending(mode => mode.Pixels).ThenByDescending(mode => mode.RefreshRateHz).ToArray();
    }

    private ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = _paths.Adb,
            WorkingDirectory = _paths.ToolsDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    private async Task<string> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        _paths.ValidateAdbTools();
        var argumentList = arguments.ToArray();
        var info = CreateStartInfo(argumentList);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("No fue posible iniciar ADB.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await stdout;
        var error = await stderr;
        var isPingCommand = argumentList.Any(argument => Regex.IsMatch(argument, @"(?:^|\s)ping(?:\s|$)", RegexOptions.IgnoreCase));
        if (process.ExitCode != 0 && !isPingCommand)
        {
            var message = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? $"ADB terminó con código {process.ExitCode}." : message);
        }
        return !string.IsNullOrWhiteSpace(output) ? output : error;
    }
}
