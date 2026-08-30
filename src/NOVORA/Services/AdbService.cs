using NOVORA.Models;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NOVORA.Services;

public sealed class AdbService
{
    private readonly NovoraPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<DeviceInfo> _deviceCache = Array.Empty<DeviceInfo>();
    private DateTimeOffset _deviceCacheAt = DateTimeOffset.MinValue;
    private bool _serverStarted;

    public AdbService(NovoraPaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default, bool force = false)
    {
        if (!force && DateTimeOffset.UtcNow - _deviceCacheAt < TimeSpan.FromSeconds(2)) return _deviceCache;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!force && DateTimeOffset.UtcNow - _deviceCacheAt < TimeSpan.FromSeconds(2)) return _deviceCache;
            await StartServerAsync(cancellationToken);
            var output = await ExecuteRawAsync(new[] { "devices", "-l" }, cancellationToken, ensureServer: false);
            var devices = new List<DeviceInfo>();
            foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (raw.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !parts[1].Equals("device", StringComparison.OrdinalIgnoreCase)) continue;
                var serial = parts[0].Trim();
                var modelToken = parts.FirstOrDefault(x => x.StartsWith("model:", StringComparison.OrdinalIgnoreCase));
                var model = modelToken is null ? serial : modelToken[6..].Replace('_', ' ');
                var details = await ReadDeviceDetailsAsync(serial, cancellationToken);
                devices.Add(new DeviceInfo
                {
                    Serial = serial,
                    Model = string.IsNullOrWhiteSpace(details.Model) ? model : details.Model,
                    AndroidVersion = details.AndroidVersion,
                    Build = details.Build,
                    Connected = true,
                    BestDisplayMode = details.BestMode,
                    SupportedDisplayModes = details.Modes
                });
            }
            _deviceCache = devices;
            _deviceCacheAt = DateTimeOffset.UtcNow;
            return _deviceCache;
        }
        finally { _gate.Release(); }
    }

    public async Task StartServerAsync(CancellationToken cancellationToken = default)
    {
        if (_serverStarted) return;
        _paths.ValidateRequiredTools();
        await ExecuteRawAsync(new[] { "start-server" }, cancellationToken, ensureServer: false);
        _serverStarted = true;
    }

    public async Task<IReadOnlyList<string>> GetConnectedSerialsAsync(CancellationToken cancellationToken = default)
        => (await GetDevicesAsync(cancellationToken, true)).Where(x => x.Connected).Select(x => x.Serial).ToArray();

    public async Task<string> ConnectOverWifiAsync(string serial, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial)) throw new ArgumentException("Serial ADB no válido.", nameof(serial));
        var ipOutput = await ShellAsync(serial, "ip -4 route get 1.1.1.1", cancellationToken);
        var match = Regex.Match(ipOutput, @"\bsrc\s+(\d{1,3}(?:\.\d{1,3}){3})");
        if (!match.Success) throw new InvalidOperationException("No se pudo obtener la IP Wi-Fi del dispositivo.");
        var ip = match.Groups[1].Value;
        await ExecuteRawAsync(new[] { "-s", serial, "tcpip", "5555" }, cancellationToken);
        await Task.Delay(1200, cancellationToken);
        var endpoint = $"{ip}:5555";
        var result = await ExecuteRawAsync(new[] { "connect", endpoint }, cancellationToken);
        if (!result.Contains("connected", StringComparison.OrdinalIgnoreCase) && !result.Contains("already", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result) ? "ADB Wi-Fi no pudo conectar." : result.Trim());
        InvalidateDeviceCache();
        return endpoint;
    }

    public async Task StopServerIfNoOtherDevicesAsync(string? selectedSerial = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var devices = await GetDevicesAsync(cancellationToken, true);
            var others = devices.Count(d => d.Connected && !string.Equals(d.Serial, selectedSerial, StringComparison.OrdinalIgnoreCase));
            if (others == 0)
            {
                await ExecuteRawAsync(new[] { "kill-server" }, cancellationToken, ensureServer: false);
                _serverStarted = false;
                InvalidateDeviceCache();
            }
        }
        catch { }
    }

    public Task<string> GetStateAsync(string serial, CancellationToken cancellationToken = default)
        => ExecuteRawAsync(new[] { "-s", serial, "get-state" }, cancellationToken);

    public Task<string> InstallAsync(string serial, string apkPath, CancellationToken cancellationToken = default)
        => ExecuteRawAsync(new[] { "-s", serial, "install", "-r", apkPath }, cancellationToken);

    public Task<string> PullAsync(string serial, string remotePath, string localPath, CancellationToken cancellationToken = default)
        => ExecuteRawAsync(new[] { "-s", serial, "pull", remotePath, localPath }, cancellationToken);

    public Task<string> PushAsync(string serial, string localPath, string remotePath, CancellationToken cancellationToken = default)
        => ExecuteRawAsync(new[] { "-s", serial, "push", localPath, remotePath }, cancellationToken);

    public async Task<byte[]> CaptureScreenAsync(string serial, CancellationToken cancellationToken = default)
    {
        var info = CreateStartInfo(new[] { "-s", serial, "exec-out", "screencap", "-p" });
        info.RedirectStandardOutput = true;
        using var process = Process.Start(info) ?? throw new InvalidOperationException("No se pudo iniciar ADB.");
        await using var memory = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(memory, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException($"ADB screencap terminó con código {process.ExitCode}.");
        return memory.ToArray();
    }

    public Task<string> ShellAsync(string serial, string command, CancellationToken cancellationToken = default)
        => ExecuteRawAsync(new[] { "-s", serial, "shell", command }, cancellationToken);

    public Task<string> ExecuteRawAsync(IEnumerable<string> arguments, CancellationToken cancellationToken = default)
        => ExecuteRawAsync(arguments, cancellationToken, ensureServer: true);

    public void InvalidateDeviceCache()
    {
        _deviceCache = Array.Empty<DeviceInfo>();
        _deviceCacheAt = DateTimeOffset.MinValue;
    }

    private async Task<string> ExecuteRawAsync(IEnumerable<string> arguments, CancellationToken cancellationToken, bool ensureServer)
    {
        _paths.ValidateRequiredTools();
        if (ensureServer && !_serverStarted) await StartServerAsync(cancellationToken);
        var info = CreateStartInfo(arguments);
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        using var process = Process.Start(info) ?? throw new InvalidOperationException("No se pudo iniciar adb.exe.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await stdout).Trim();
        var error = (await stderr).Trim();
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"ADB terminó con código {process.ExitCode}." : error);
        return string.IsNullOrWhiteSpace(output) ? error : output;
    }

    private ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = _paths.Adb,
            WorkingDirectory = _paths.ToolsDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    private async Task<(string Model, string AndroidVersion, string Build, DisplayModeInfo? BestMode, IReadOnlyList<DisplayModeInfo> Modes)> ReadDeviceDetailsAsync(string serial, CancellationToken ct)
    {
        var props = await ShellAsync(serial, "printf '%s|%s|%s' \"$(getprop ro.product.model)\" \"$(getprop ro.build.version.release)\" \"$(getprop ro.build.display.id)\"", ct);
        var fields = props.Split('|');
        var model = fields.ElementAtOrDefault(0)?.Trim() ?? string.Empty;
        var android = fields.ElementAtOrDefault(1)?.Trim() ?? string.Empty;
        var build = fields.ElementAtOrDefault(2)?.Trim() ?? string.Empty;
        IReadOnlyList<DisplayModeInfo> modes = Array.Empty<DisplayModeInfo>();
        try
        {
            var display = await ShellAsync(serial, "dumpsys display", ct);
            modes = ParseDisplayModes(display);
        }
        catch { }
        return (model, android, build, modes.OrderByDescending(x => x.Pixels).ThenByDescending(x => x.RefreshRateHz).FirstOrDefault(), modes);
    }

    private static IReadOnlyList<DisplayModeInfo> ParseDisplayModes(string text)
    {
        var modes = new List<DisplayModeInfo>();
        foreach (Match m in Regex.Matches(text, @"(?<w>\d{3,5})\s*x\s*(?<h>\d{3,5})(?:[^\n\r]{0,80}?)(?<hz>\d{2,3}(?:\.\d+)?)\s*(?:Hz|fps)?", RegexOptions.IgnoreCase))
        {
            if (!int.TryParse(m.Groups["w"].Value, out var w) || !int.TryParse(m.Groups["h"].Value, out var h)) continue;
            if (!double.TryParse(m.Groups["hz"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hz)) continue;
            if (w < 200 || h < 200 || hz < 10 || hz > 1000) continue;
            modes.Add(new DisplayModeInfo(w, h, hz));
        }
        return modes.Distinct().OrderByDescending(x => x.Pixels).ThenByDescending(x => x.RefreshRateHz).ToArray();
    }
}
