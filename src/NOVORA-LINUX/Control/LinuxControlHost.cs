using System.Net;
using System.Net.NetworkInformation;

namespace NOVORA.Linux.Control;

internal sealed record LanAddress(string InterfaceName, IPAddress Address)
{
    public override string ToString() => $"{InterfaceName} · {Address}";
}

internal sealed class LinuxControlHost : IAsyncDisposable
{
    private LinuxTrustStore? _store;
    private LinuxControlServer? _lanServer;
    private LinuxControlServer? _usbServer;
    private LinuxLinkEngine? _usbLinkEngine;
    private LinuxLanDiscovery? _discovery;
    private AdbBridge.UsbReverseLease? _usbReverse;
    private string? _usbSerial;
    private readonly AdbBridge _adb = new();
    private readonly SemaphoreSlim _visionSessionGate = new(1, 1);

    public event EventHandler<string>? StatusChanged;
    public event EventHandler? InvitationChanged;
    public bool IsRunning => _lanServer?.IsRunning == true;
    public bool IsUsbRunning => _usbServer?.IsRunning == true;
    public bool IsAuthorized => _lanServer?.IsAuthorized == true || _usbServer?.IsAuthorized == true;
    public LanInvitation? Invitation => _lanServer?.Invitation;
    public string? UsbSerial => _usbSerial;
    public string LinkState => _usbLinkEngine?.State ?? "Unavailable";
    public string LinkMessage => _usbLinkEngine?.Message ??
        "Enlaza un teléfono por USB para habilitar LinkEngine.";
    private LinuxControlServer? VisionServer =>
        _lanServer?.HasVisionSession == true ? _lanServer :
        _usbServer?.HasVisionSession == true ? _usbServer :
        _lanServer ?? _usbServer;
    public bool VisionRunning => VisionServer?.IsVisionRunning == true;
    public bool HasVisionSession => VisionServer?.HasVisionSession == true;
    public string VisionState => VisionServer?.VisionState ?? "Unavailable";
    public string VisionMessage => VisionServer?.VisionMessage ??
        "VisionEngine Linux requiere una sesión autorizada por USB o LAN.";
    public NOVORA.Linux.VisionEngine.LinuxVisionTelemetry? VisionTelemetry => VisionServer?.VisionTelemetry;
    public IReadOnlyList<TrustedDevice> TrustedDevices => _store?.Devices ?? [];

    public static IReadOnlyList<LanAddress> GetLanAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses
                .Where(entry => entry.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    LinuxLanDiscovery.IsPrivateIpv4(entry.Address))
                .Select(entry => new LanAddress(network.Name, entry.Address)))
            .DistinctBy(entry => entry.Address)
            .OrderBy(entry => entry.InterfaceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Address.ToString(), StringComparer.Ordinal)
            .ToArray();

    public Task<IReadOnlyList<AdbDevice>> GetAdbDevicesAsync(CancellationToken cancellationToken) =>
        _adb.GetDevicesAsync(cancellationToken);

    public void Start(IPAddress address)
    {
        if (IsRunning)
            throw new InvalidOperationException("Detén el control LAN antes de cambiar de interfaz.");

        EnsureTrustStore();
        try
        {
            var visionLanEngine = new NOVORA.Linux.VisionEngine.LinuxVisionLanEngine(
                address, _store!, _visionSessionGate);
            _lanServer = new LinuxControlServer(address, _store!, visionLanEngine: visionLanEngine);
            _discovery = new LinuxLanDiscovery(address, Environment.MachineName, _store!.Fingerprint);
            _lanServer.StatusChanged += OnStatusChanged;
            _lanServer.InvitationChanged += OnInvitationChanged;
            _lanServer.Start();
            _discovery.Start();
        }
        catch
        {
            if (_lanServer is not null)
            {
                _lanServer.StatusChanged -= OnStatusChanged;
                _lanServer.InvitationChanged -= OnInvitationChanged;
                _lanServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            if (_discovery is not null)
                _discovery.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _lanServer = null;
            _discovery = null;
            throw;
        }
    }

    public LanInvitation RenewInvitation() =>
        (_lanServer ?? throw new InvalidOperationException("El control LAN no está iniciado.")).RenewAndGetInvitation();

    public async Task RevokeAsync(string deviceId)
    {
        (_store ?? throw new InvalidOperationException("El almacén de confianza no está iniciado."))
            .Revoke(deviceId);
        List<Exception>? errors = null;
        foreach (LinuxControlServer? server in new[] { _lanServer, _usbServer })
        {
            if (server is null)
                continue;
            try
            {
                await server.DisconnectRevokedDeviceAsync(deviceId).ConfigureAwait(false);
            }
            catch (Exception ex) { (errors ??= []).Add(ex); }
        }

        if (errors is not null)
            throw new AggregateException("El dispositivo fue revocado, pero no se liberaron todos sus recursos.", errors);
    }

    public Task StopVisionAsync() =>
        (VisionServer ?? throw new InvalidOperationException("El control LAN o USB no está iniciado."))
        .StopVisionAsync();

    public async Task StartUsbAsync(AdbDevice device, CancellationToken cancellationToken)
    {
        if (IsUsbRunning)
            throw new InvalidOperationException("Detén el control USB antes de cambiar de teléfono.");
        EnsureTrustStore();
        var linkEngine = new LinuxLinkEngine(_adb, device.Serial);
        var visionEngine = new NOVORA.Linux.VisionEngine.LinuxVisionEngine(
            _adb, device.Serial, _visionSessionGate);
        LinuxControlServer server = new(IPAddress.Loopback, _store!, port: 27214,
            transport: "USB", linkEngine: linkEngine, visionEngine: visionEngine);
        server.StatusChanged += OnStatusChanged;
        try
        {
            server.Start();
            AdbBridge.UsbReverseLease reverse =
                await _adb.PrepareUsbControlAsync(device.Serial, server.UsbInvitation, cancellationToken);
            _usbServer = server;
            _usbReverse = reverse;
            _usbLinkEngine = linkEngine;
            _usbSerial = device.Serial;
            server = null!;
        }
        finally
        {
            if (server is not null)
            {
                try
                {
                    await linkEngine.DisposeAsync();
                }
                finally
                {
                    server.StatusChanged -= OnStatusChanged;
                    await server.DisposeAsync();
                }
            }
        }
    }

    public async Task StopUsbAsync()
    {
        LinuxControlServer? server = _usbServer;
        LinuxLinkEngine? linkEngine = _usbLinkEngine;
        AdbBridge.UsbReverseLease? reverse = _usbReverse;
        _usbServer = null;
        _usbLinkEngine = null;
        _usbReverse = null;
        _usbSerial = null;
        try
        {
            try
            {
                if (linkEngine is not null)
                    await linkEngine.DisposeAsync();
            }
            finally
            {
                if (server is not null)
                {
                    server.StatusChanged -= OnStatusChanged;
                    await server.DisposeAsync();
                }
            }
        }
        finally
        {
            if (reverse is not null)
                await reverse.DisposeAsync();
        }
    }

    private void EnsureTrustStore() => _store ??= new LinuxTrustStore(GetConfigurationDirectory());

    private void OnStatusChanged(object? sender, string message) => StatusChanged?.Invoke(this, message);
    private void OnInvitationChanged(object? sender, EventArgs e) => InvitationChanged?.Invoke(this, EventArgs.Empty);

    private static string GetConfigurationDirectory()
    {
        string? configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string root = !string.IsNullOrWhiteSpace(configHome) && Path.IsPathFullyQualified(configHome)
            ? configHome
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(root, "novora-link-linux");
    }

    public async ValueTask DisposeAsync()
    {
        LinuxLanDiscovery? discovery = _discovery;
        LinuxControlServer? lanServer = _lanServer;
        LinuxControlServer? usbServer = _usbServer;
        LinuxLinkEngine? usbLinkEngine = _usbLinkEngine;
        AdbBridge.UsbReverseLease? reverse = _usbReverse;
        LinuxTrustStore? store = _store;
        _discovery = null;
        _lanServer = null;
        _usbServer = null;
        _usbLinkEngine = null;
        _usbReverse = null;
        _usbSerial = null;
        _store = null;
        try
        {
            if (discovery is not null)
                await discovery.DisposeAsync();
        }
        finally
        {
            try
            {
                try
                {
                    if (usbLinkEngine is not null)
                        await usbLinkEngine.DisposeAsync();
                }
                finally
                {
                    if (usbServer is not null)
                    {
                        usbServer.StatusChanged -= OnStatusChanged;
                        await usbServer.DisposeAsync();
                    }
                }
            }
            finally
            {
                try
                {
                    if (reverse is not null)
                        await reverse.DisposeAsync();
                }
                finally
                {
                    try
                    {
                        if (lanServer is not null)
                        {
                            lanServer.StatusChanged -= OnStatusChanged;
                            lanServer.InvitationChanged -= OnInvitationChanged;
                            await lanServer.DisposeAsync();
                        }
                    }
                    finally
                    {
                        store?.Dispose();
                    }
                }
            }
        }
    }
}
