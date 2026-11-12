using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

// Linux counterparts of the two Windows services that the shared ExIn sources use.
// ExInEngine/*.cs and Integration/ExIn*.cs are compiled unchanged from `src/NOVORA LINK`
// (see NOVORA.Linux.csproj); only these platform boundaries differ.
namespace NOVORA.Service;

/// <summary>Rutas de NOVORA en Linux con la misma forma que en Windows.</summary>
public sealed class NLServiceNovoraPaths
{
    public NLServiceNovoraPaths(string? baseDirectory = null)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        string tools = Path.Combine(BaseDirectory, "Tools");
        // The shared HID output uses this as the adb working directory; it must exist.
        ToolsDirectory = Directory.Exists(tools) ? tools : BaseDirectory;
        string? dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string dataRoot = !string.IsNullOrWhiteSpace(dataHome) && Path.IsPathRooted(dataHome)
            ? dataHome
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        ExInProfilesDirectory = Path.Combine(dataRoot, "NOVORA-LINK", "ExInProfiles");
    }

    public string BaseDirectory { get; }
    public string ToolsDirectory { get; }
    public string ExInProfilesDirectory { get; }

    /// <summary>adb del sistema (android-tools), igual que AdbBridge.</summary>
    public string Adb => ResolveAdb();

    private static string ResolveAdb()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(directory, "adb");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return "adb";
    }
}

/// <summary>Dispositivo ADB con los campos que consulta la sesión ExIn.</summary>
public sealed record NLServiceADBDevice(string Serial, string State, string Model, bool IsUsb)
{
    public bool Connected => State == "device";
    // host:port and mDNS `adb-…._adb-tls-connect._tcp` serials are Wi-Fi transports.
    public bool IsWifiConnection => !IsUsb;
    public string FriendlyName => string.IsNullOrWhiteSpace(Model) ? "Android" : Model.Replace('_', ' ');
}

/// <summary>Subconjunto Linux de NLServiceADB usado por ExInControlSession y ExInHidOutput.</summary>
public sealed class NLServiceADB
{
    private static readonly TimeSpan DeviceCacheLifetime = TimeSpan.FromSeconds(1);
    // A stuck adb server must never freeze the two-second ExIn refresh (it would leave
    // _exInRefreshing true forever and the phone would never be detected again).
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private readonly NLServiceNovoraPaths _paths;
    private readonly SemaphoreSlim _serverGate = new(1, 1);
    private readonly SemaphoreSlim _deviceGate = new(1, 1);
    private IReadOnlyList<NLServiceADBDevice>? _deviceCache;
    private DateTimeOffset _deviceCacheAt;
    private bool _serverStarted;

    public NLServiceADB(NLServiceNovoraPaths paths)
        => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public async Task StartServerAsync(CancellationToken cancellationToken = default)
    {
        if (_serverStarted) return;
        await _serverGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_serverStarted) return;
            await RunAsync(["start-server"], cancellationToken).ConfigureAwait(false);
            _serverStarted = true;
        }
        finally { _serverGate.Release(); }
    }

    public async Task<IReadOnlyList<NLServiceADBDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        if (!forceRefresh && _deviceCache is not null && DateTimeOffset.UtcNow - _deviceCacheAt < DeviceCacheLifetime)
            return _deviceCache;
        await _deviceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _deviceCache is not null && DateTimeOffset.UtcNow - _deviceCacheAt < DeviceCacheLifetime)
                return _deviceCache;
            await StartServerAsync(cancellationToken).ConfigureAwait(false);
            string output = await RunAsync(["devices", "-l"], cancellationToken).ConfigureAwait(false);
            _deviceCache = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Select(ParseDevice)
                .Where(device => device is not null)
                .Cast<NLServiceADBDevice>()
                .ToArray();
            _deviceCacheAt = DateTimeOffset.UtcNow;
            return _deviceCache;
        }
        finally { _deviceGate.Release(); }
    }

    public async Task<string> GetHardwareSerialAsync(string adbSerial, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adbSerial);
        string value = (await RunAsync(["-s", adbSerial, "shell", "getprop", "ro.serialno"], cancellationToken)
            .ConfigureAwait(false)).Trim();
        return value is "" or "unknown" ? string.Empty : value;
    }

    public Task<string> ExecuteRawAsync(IEnumerable<string> arguments, CancellationToken cancellationToken = default)
        => RunAsync(arguments, cancellationToken);

    /// <summary>
    /// Igual que Windows: intenta <c>adb connect host:5555</c> para un teléfono autorizado por LAN.
    /// Devuelve el serial ADB si responde; null si ADB Wi-Fi no está activo.
    /// </summary>
    public async Task<string?> TryConnectLanEndpointAsync(string host, int port = 5555,
        CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(host, out IPAddress? address) ||
            address.AddressFamily != AddressFamily.InterNetwork || port is < 1 or > 65535)
            return null;
        string serial = $"{address}:{port}";
        try
        {
            await StartServerAsync(cancellationToken).ConfigureAwait(false);
            string output = await RunAsync(["connect", serial], cancellationToken).ConfigureAwait(false);
            if (!output.Contains("connected to", StringComparison.OrdinalIgnoreCase) &&
                !output.Contains("already connected", StringComparison.OrdinalIgnoreCase))
                return null;
            IReadOnlyList<NLServiceADBDevice> devices = await GetDevicesAsync(cancellationToken, forceRefresh: true)
                .ConfigureAwait(false);
            return devices.Any(device => device.Connected && device.Serial == serial) ? serial : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public Task<string> ShellAsync(string serial, string command, CancellationToken cancellationToken = default)
        => RunAsync(["-s", serial, "shell", command], cancellationToken);

    /// <summary>Windows ConnectOverWifiAsync: Wi-Fi IP of the USB phone, <c>adb tcpip</c>, then <c>adb connect</c>.</summary>
    public async Task<string> ConnectOverWifiAsync(string usbSerial, int port = 5555,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usbSerial))
            throw new ArgumentException("No hay un dispositivo ADB USB seleccionado.", nameof(usbSerial));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        string addresses = await ShellAsync(usbSerial, "ip -4 -o addr show wlan0 scope global", cancellationToken).ConfigureAwait(false);
        var ip = System.Text.RegularExpressions.Regex.Match(addresses, @"\binet\s+(\d{1,3}(?:\.\d{1,3}){3})/");
        if (!ip.Success)
        {
            string route = await ShellAsync(usbSerial, "ip -4 route get 1.1.1.1", cancellationToken).ConfigureAwait(false);
            ip = System.Text.RegularExpressions.Regex.Match(route, @"\bsrc\s+(\d{1,3}(?:\.\d{1,3}){3})\b");
        }
        if (!ip.Success)
            throw new InvalidOperationException(
                "NOVORA no pudo obtener la IP Wi-Fi del teléfono. Conéctalo a una red Wi-Fi y vuelve a intentarlo.");
        await RunAsync(["-s", usbSerial, "tcpip", port.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            cancellationToken).ConfigureAwait(false);
        string endpoint = $"{ip.Groups[1].Value}:{port}";
        string lastOutput = "";
        for (int attempt = 0; attempt < 7; attempt++)
        {
            await Task.Delay(attempt == 0 ? 1200 : 700, cancellationToken).ConfigureAwait(false);
            try
            {
                lastOutput = await RunAsync(["connect", endpoint], cancellationToken).ConfigureAwait(false);
                if (lastOutput.Contains("connected to", StringComparison.OrdinalIgnoreCase) ||
                    lastOutput.Contains("already connected", StringComparison.OrdinalIgnoreCase))
                {
                    _deviceCache = null;
                    return endpoint;
                }
            }
            catch (InvalidOperationException ex) { lastOutput = ex.Message; }
        }
        throw new InvalidOperationException($"ADB no pudo conectar por Wi-Fi a {endpoint}. {lastOutput.Trim()}");
    }

    /// <summary>Windows ConnectWirelessDebuggingAsync: Android 11+ Wireless debugging endpoint (IP:port).</summary>
    public async Task<string> ConnectWirelessDebuggingAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        string[] parts = (endpoint ?? string.Empty).Trim().Split(':');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out IPAddress? address) ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int port) ||
            port is < 1 or > 65535)
            throw new ArgumentException("Escribe la dirección IP y puerto de Depuración inalámbrica, por ejemplo 10.0.0.28:37123.", nameof(endpoint));
        string serial = $"{address}:{port}";
        await StartServerAsync(cancellationToken).ConfigureAwait(false);
        string output = await RunAsync(["connect", serial], cancellationToken).ConfigureAwait(false);
        if (!output.Contains("connected to", StringComparison.OrdinalIgnoreCase) &&
            !output.Contains("already connected", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"ADB no conectó con {serial}: {output.Trim()}");
        for (int attempt = 0; attempt < 5; attempt++)
        {
            IReadOnlyList<NLServiceADBDevice> devices = await GetDevicesAsync(cancellationToken, forceRefresh: true).ConfigureAwait(false);
            if (devices.Any(device => device.Connected && device.Serial == serial)) return serial;
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("ADB alcanzó el teléfono, pero aún no está autorizado. Vincúlalo en Depuración inalámbrica y vuelve a intentar.");
    }

    public async Task<IReadOnlyList<string>> GetWirelessDebuggingEndpointsAsync(CancellationToken cancellationToken = default)
    {
        await StartServerAsync(cancellationToken).ConfigureAwait(false);
        return ParseWirelessDebuggingEndpoints(await RunAsync(["mdns", "services"], cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Same parser as Windows NLServiceADB.ParseWirelessDebuggingEndpoints.</summary>
    public static IReadOnlyList<string> ParseWirelessDebuggingEndpoints(string output)
        => System.Text.RegularExpressions.Regex.Matches(output,
                // The instance name may contain spaces: Android renames a re-announced service
                // "adb-<serial>-<id> (2)", and that is often the only endpoint still accepting.
                @"^.+?\s+_adb-tls-connect\._tcp\s+((?:\d{1,3}\.){3}\d{1,3}):([0-9]{1,5})\s*$",
                System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(match =>
                IPAddress.TryParse(match.Groups[1].Value, out IPAddress? address) &&
                address.AddressFamily == AddressFamily.InterNetwork &&
                int.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int port) &&
                port is >= 1 and <= 65535
                    ? $"{address}:{port}"
                    : null)
            .Where(endpoint => endpoint is not null)
            .Select(endpoint => endpoint!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static NLServiceADBDevice? ParseDevice(string line)
    {
        string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        string serial = parts[0];
        string model = parts.Skip(2).FirstOrDefault(part => part.StartsWith("model:", StringComparison.Ordinal))?[6..] ?? "";
        bool network = serial.Contains(':') || serial.Contains("._adb-tls-connect.", StringComparison.Ordinal);
        bool usb = !network && parts.Skip(2).Any(part => part.StartsWith("usb:", StringComparison.Ordinal));
        return new NLServiceADBDevice(serial, parts[1], model, usb);
    }

    private async Task<string> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(_paths.Adb)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException("No fue posible iniciar ADB.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                "ADB no está instalado. Instala android-tools (adb) para usar ExInEngine.", ex);
        }
        using (process)
        {
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(CommandTimeout);
            try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                throw new OperationCanceledException(
                    $"ADB no respondió en {CommandTimeout.TotalSeconds:0} s. Se reintentará.", deadline.Token);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                throw;
            }
            string output = await stdout.ConfigureAwait(false);
            string error = await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string message = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                    ? $"ADB terminó con código {process.ExitCode}."
                    : message);
            }
            return string.IsNullOrWhiteSpace(output) ? error : output;
        }
    }
}
