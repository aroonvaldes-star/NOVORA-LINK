using NOVORA.Linux.VisionEngine;
using System.Net;
using System.Net.NetworkInformation;

namespace NOVORA.Linux.Control;

internal sealed record LanAddress(string InterfaceName, IPAddress Address)
{
    public override string ToString() => $"{InterfaceName} · {Address}";
}

internal sealed class LinuxControlHost : IAsyncDisposable, IAutoConnectTarget
{
    private readonly LinuxVisionSettingsStore _visionStore;
    private readonly object _visionSettingsGate = new();
    public LinuxVisionSettings VisionSettings { get; private set; }
    public event Action? VisionSettingsChanged;
    public LinuxControlHost(LinuxVisionSettingsStore? visionStore = null)
    {
        _visionStore=visionStore ?? new();
        try { VisionSettings = _visionStore.Load(); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException or UnauthorizedAccessException)
        { VisionSettings = LinuxVisionSettings.Default; }
    }
    private LinuxTrustStore? _store;
    private LinuxControlServer? _lanServer;
    private LinuxControlServer? _usbServer;
    private LinuxLinkEngine? _usbLinkEngine;
    private LinuxLanLinkEngine? _lanLinkEngine;
    private LinuxLanDiscovery? _discovery;
    private AdbBridge.UsbReverseLease? _usbReverse;
    private string? _usbSerial;
    private bool _usbIsWireless;
    private readonly AdbBridge _adb = new();
    private readonly SemaphoreSlim _visionSessionGate = new(1, 1);
    // Serializes route start/stop between the UI and the automatic connector.
    private readonly SemaphoreSlim _routeGate = new(1, 1);
    private IPAddress? _lanAddress;
    private AdbDeviceTracker? _adbTracker;
    private LinuxAutoConnector? _autoConnector;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler? InvitationChanged;
    public bool IsRunning => _lanServer?.IsRunning == true;
    public bool IsUsbRunning => _usbServer?.IsRunning == true;
    public bool IsUsbAuthorized => _usbServer?.IsAuthorized == true;
    public bool IsLanAuthorized => _lanServer?.IsAuthorized == true;
    private string? _selectedTransport;
    private string? ActiveCaptureTransport =>
        _usbServer?.HasVisionSession == true ? "USB" :
        _lanServer?.HasVisionSession == true ? "LAN" :
        _usbServer?.VisionRequested == true ? "USB" :
        _lanServer?.VisionRequested == true ? "LAN" : null;
    public string? SelectedTransport => LinuxConnectionSelection.Resolve(
        _selectedTransport, IsUsbAuthorized, IsLanAuthorized, ActiveCaptureTransport);
    public bool ConnectionBusy => ActiveCaptureTransport is not null ||
        _usbLinkEngine?.IsStarted == true || _lanLinkEngine?.IsStarted == true;
    /// <summary>
    /// Records a route preference. It only decides between two authorized routes; a route that
    /// is not authorized never blocks the other one, and a running capture keeps its route.
    /// </summary>
    public void SelectTransport(string transport)
    {
        if (transport is not ("USB" or "LAN")) throw new ArgumentException("Selecciona USB o LAN.", nameof(transport));
        _selectedTransport = transport;
    }
    /// <summary>Address of the running LAN listener.</summary>
    public IPAddress? LanAddress => IsRunning ? _lanAddress : null;
    public bool LanBusy => _lanServer?.HasVisionSession == true || _lanServer?.VisionRequested == true ||
        _lanLinkEngine?.IsStarted == true;
    internal LinuxAutoConnector? AutoConnect => _autoConnector;
    /// <summary>Connection state for the Home card, independent of the transport.</summary>
    /// <summary>The ADB route uses paired wireless debugging rather than a cable.</summary>
    public bool UsbIsWireless => IsUsbRunning && _usbIsWireless;
    public string ConnectionSummary =>
        IsUsbAuthorized ? (UsbIsWireless ? "Conectado por ADB Wi-Fi" : "Conectado por USB") :
        IsLanAuthorized ? "Conectado por LAN" :
        _autoConnector?.Summary ?? "Desconectado";

    /// <summary>
    /// Starts the automatic USB/LAN connection: LAN follows the active private network and
    /// ADB hotplug bootstraps LINK Control over USB. Engines still start only on request.
    /// </summary>
    public void StartAutomaticConnection()
    {
        if (_autoConnector is not null) return;
        _adbTracker = new AdbDeviceTracker();
        _autoConnector = new LinuxAutoConnector(this, () => _adbTracker?.Devices, GetLanCandidates);
        _adbTracker.Changed += _autoConnector.Signal;
        _autoConnector.Changed += OnAutoConnectChanged;
        _autoConnector.Notice += OnAutoConnectNotice;
        _adbTracker.Start();
        _autoConnector.Start();
    }

    public event EventHandler? ConnectionChanged;
    public event EventHandler<(string Message, bool Warning)>? ConnectionNotice;
    private void OnAutoConnectChanged() => ConnectionChanged?.Invoke(this, EventArgs.Empty);
    private void OnAutoConnectNotice(string message, bool warning) => ConnectionNotice?.Invoke(this, (message, warning));

    public static IReadOnlyList<LanCandidate> GetLanCandidates() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(network =>
            {
                IPInterfaceProperties properties = network.GetIPProperties();
                bool gateway = properties.GatewayAddresses.Any(entry =>
                    entry.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !entry.Address.Equals(IPAddress.Any));
                return properties.UnicastAddresses
                    .Where(entry => entry.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        LinuxLanDiscovery.IsPrivateIpv4(entry.Address))
                    .Select(entry => new LanCandidate(network.Name, entry.Address, gateway, network.NetworkInterfaceType));
            })
            .DistinctBy(entry => entry.Address)
            .ToArray();
    public bool IsAuthorized => _lanServer?.IsAuthorized == true || _usbServer?.IsAuthorized == true;
    public LanInvitation? Invitation => _lanServer?.Invitation;
    public string? UsbSerial => _usbSerial;
    private NOVORA.Linux.ExIn.ILinuxExInControl? _exIn;
    /// <summary>ExInEngine bridge shared by the USB and LAN control servers.</summary>
    internal NOVORA.Linux.ExIn.ILinuxExInControl? ExIn
    {
        get => _exIn;
        set
        {
            _exIn = value;
            if (_lanServer is not null) _lanServer.ExIn = value;
            if (_usbServer is not null) _usbServer.ExIn = value;
        }
    }
    /// <summary>LINK Control peer authorized over LAN, for ExIn's ADB Wi-Fi fallback.</summary>
    public IPAddress? LanPeerAddress => _lanServer?.AuthorizedPeerAddress;
    /// <summary>Current-session LinkEngine SpeedTest and traffic (shared with both control servers).</summary>
    internal LinuxLinkMeasurements LinkMeasurements { get; } = new();
    /// <summary>The LinkEngine whose state the window shows and measures.</summary>
    internal ILinuxLinkEngine? CurrentLinkEngine => ActiveLinkEngine;
    public string LinkState => ActiveLinkEngine?.State ?? "Unavailable";
    public string LinkMessage => ActiveLinkEngine?.Message ??
        "Enlaza un teléfono por USB o LAN para habilitar LinkEngine.";
    private ILinuxLinkEngine? ActiveLinkEngine =>
        _usbLinkEngine?.IsConnected == true ? _usbLinkEngine :
        _lanLinkEngine?.IsConnected == true ? _lanLinkEngine :
        _usbLinkEngine?.IsStarted == true ? _usbLinkEngine :
        _lanLinkEngine?.IsStarted == true ? _lanLinkEngine :
        SelectedTransport == "USB" ? _usbLinkEngine :
        SelectedTransport == "LAN" ? _lanLinkEngine : null;
    private LinuxControlServer? VisionServer =>
        SelectedTransport == "USB" ? _usbServer :
        SelectedTransport == "LAN" ? _lanServer : null;
    public bool VisionRequested => VisionServer?.VisionRequested == true;
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

    /// <summary>Manual LAN start from the UI; refuses instead of blocking while a route is changing.</summary>
    public void Start(IPAddress address)
    {
        if (!_routeGate.Wait(0))
            throw new InvalidOperationException("Se está preparando otra conexión; inténtalo de nuevo en unos segundos.");
        try { StartLanCore(address); }
        finally { _routeGate.Release(); }
    }

    public async Task StartLanAsync(IPAddress address, CancellationToken cancellationToken)
    {
        await _routeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { StartLanCore(address); }
        finally { _routeGate.Release(); }
    }

    private void StartLanCore(IPAddress address)
    {
        if (IsRunning && address.Equals(_lanAddress))
            return; // Already listening there (UI and automatic connector may race).
        if (IsRunning)
            throw new InvalidOperationException("Detén el control LAN antes de cambiar de interfaz.");

        EnsureTrustStore();
        LinuxLanLinkEngine? linkEngine = null;
        try
        {
            linkEngine = new LinuxLanLinkEngine(address, _store!);
            var visionLanEngine = new NOVORA.Linux.VisionEngine.LinuxVisionLanEngine(
                address, _store!, _visionSessionGate);
            _lanServer = new LinuxControlServer(address, _store!, linkEngine: linkEngine,
                visionLanEngine: visionLanEngine);
            _lanServer.VisionSettingsGate = _visionSettingsGate;
            _lanServer.ExIn = _exIn;
            _lanServer.LinkMeasurements = LinkMeasurements;
            _lanServer.ConfigureVisionSettings(VisionSettings);
            _lanServer.VisionSettingsChanged += ApplyRemoteVisionSettings;
            _lanLinkEngine = linkEngine;
            _discovery = new LinuxLanDiscovery(address, Environment.MachineName, _store!.Fingerprint);
            _lanServer.StatusChanged += OnStatusChanged;
            _lanServer.InvitationChanged += OnInvitationChanged;
            _lanServer.Start();
            _discovery.Start();
            _lanAddress = address;
            linkEngine = null;
        }
        catch
        {
            if (_lanServer is not null)
            {
                _lanServer.StatusChanged -= OnStatusChanged;
                _lanServer.InvitationChanged -= OnInvitationChanged;
                _lanServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            if (linkEngine is not null)
                linkEngine.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (_discovery is not null)
                _discovery.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _lanServer = null;
            _lanLinkEngine = null;
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

    public void StartVisionRecording() { lock(_visionSettingsGate) (VisionRuntime ?? throw new InvalidOperationException("Inicia VE primero.")).StartRecording(); }
    public void RequestVision()
    {
        var server = VisionServer;
        if (server?.IsAuthorized != true)
            throw new InvalidOperationException(SelectedTransport is null
                ? "Esperando a LINK Control. Conecta el cable USB o abre LINK Control en la misma Wi-Fi."
                : $"Enlaza y autoriza LINK Control por {SelectedTransport} primero.");
        server.RequestVision();
    }
    public LinuxVisionRuntime? VisionRuntime => VisionServer?.VisionRuntime;
    public bool VisionRecording => VisionServer?.VisionRecording == true;

    public Task StopVisionAsync() =>
        (VisionServer ?? throw new InvalidOperationException("El control LAN o USB no está iniciado."))
        .StopVisionAsync();

    public void ConfigureVisionSettings(string bitrate, string profile, int maxSize, int fps) =>
        ConfigureVisionSettings(VisionSettings with { Bitrate = bitrate, Profile = LinuxVisionSettings.ProfileId(profile), MaxSize = maxSize, Fps = fps });

    public void ConfigureVisionSettings(LinuxVisionSettings settings)
    {
        lock(_visionSettingsGate)
        {
        settings.Validate();
        if (VisionRecording) throw new InvalidOperationException("Detén la grabación antes de cambiar ajustes.");
        _visionStore.Save(settings);
        _lanServer?.ConfigureVisionSettings(settings);
        _usbServer?.ConfigureVisionSettings(settings);
        VisionSettings = settings;
        VisionSettingsChanged?.Invoke();
        }
    }

    private void ApplyRemoteVisionSettings(LinuxVisionSettings settings)
    {
        ConfigureVisionSettings(settings);
    }

    public async Task StartUsbAsync(AdbDevice device, CancellationToken cancellationToken)
    {
        await _routeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StartUsbCoreAsync(device, cancellationToken).ConfigureAwait(false); }
        finally { _routeGate.Release(); }
    }

    private async Task StartUsbCoreAsync(AdbDevice device, CancellationToken cancellationToken)
    {
        if (IsUsbRunning && _usbSerial == device.Serial)
            return; // Already prepared for this phone (UI and automatic connector may race).
        if (IsUsbRunning)
            throw new InvalidOperationException("Detén el control USB antes de cambiar de teléfono.");
        EnsureTrustStore();
        var linkEngine = new LinuxLinkEngine(_adb, device.Serial);
        var visionEngine = new NOVORA.Linux.VisionEngine.LinuxVisionEngine(
            _adb, device.Serial, _visionSessionGate);
        LinuxControlServer server = new(IPAddress.Loopback, _store!, port: 27214,
            transport: "USB", linkEngine: linkEngine, visionEngine: visionEngine);
        server.VisionSettingsGate = _visionSettingsGate;
        server.ExIn = _exIn;
        server.LinkMeasurements = LinkMeasurements;
        server.ConfigureVisionSettings(VisionSettings);
        server.VisionSettingsChanged += ApplyRemoteVisionSettings;
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
            _usbIsWireless = !device.IsUsb;
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
        await _routeGate.WaitAsync().ConfigureAwait(false);
        try { await StopUsbCoreAsync().ConfigureAwait(false); }
        finally { _routeGate.Release(); }
    }

    private async Task StopUsbCoreAsync()
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

    public async Task StopLanAsync()
    {
        await _routeGate.WaitAsync().ConfigureAwait(false);
        try { await StopLanCoreAsync().ConfigureAwait(false); }
        finally { _routeGate.Release(); }
    }

    private async Task StopLanCoreAsync()
    {
        LinuxLanDiscovery? discovery = _discovery;
        LinuxControlServer? server = _lanServer;
        LinuxLanLinkEngine? linkEngine = _lanLinkEngine;
        _discovery = null;
        _lanServer = null;
        _lanLinkEngine = null;
        _lanAddress = null;
        try
        {
            if (discovery is not null) await discovery.DisposeAsync();
        }
        finally
        {
            try
            {
                if (linkEngine is not null) await linkEngine.DisposeAsync();
            }
            finally
            {
                if (server is not null)
                {
                    server.StatusChanged -= OnStatusChanged;
                    server.InvitationChanged -= OnInvitationChanged;
                    await server.DisposeAsync();
                }
            }
        }
    }

    private void EnsureTrustStore() => _store ??= new LinuxTrustStore(GetConfigurationDirectory());

    private void OnStatusChanged(object? sender, string message)
    {
        // Session start/end changes the automatic route decisions.
        _autoConnector?.Signal();
        StatusChanged?.Invoke(this, message);
    }
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
        // Stop automatic routing first so it cannot reopen a route during shutdown.
        LinuxAutoConnector? autoConnector = _autoConnector;
        AdbDeviceTracker? tracker = _adbTracker;
        _autoConnector = null;
        _adbTracker = null;
        if (autoConnector is not null)
        {
            autoConnector.Changed -= OnAutoConnectChanged;
            autoConnector.Notice -= OnAutoConnectNotice;
            await autoConnector.DisposeAsync();
        }
        if (tracker is not null)
            await tracker.DisposeAsync();
        LinkMeasurements.Dispose();
        LinuxLanDiscovery? discovery = _discovery;
        LinuxControlServer? lanServer = _lanServer;
        LinuxControlServer? usbServer = _usbServer;
        LinuxLinkEngine? usbLinkEngine = _usbLinkEngine;
        LinuxLanLinkEngine? lanLinkEngine = _lanLinkEngine;
        AdbBridge.UsbReverseLease? reverse = _usbReverse;
        LinuxTrustStore? store = _store;
        _discovery = null;
        _lanServer = null;
        _usbServer = null;
        _usbLinkEngine = null;
        _lanLinkEngine = null;
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
                        if (lanLinkEngine is not null)
                            await lanLinkEngine.DisposeAsync();
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
}
