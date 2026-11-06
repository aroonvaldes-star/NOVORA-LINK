using NOVORA.Linux.Files;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using NOVORA.Linux.VisionEngine;

namespace NOVORA.Linux.Control;

internal sealed record LanInvitation(string Host, int Port, string Fingerprint, string Code, long ExpiresUnix);

internal sealed class LinuxControlServer : IAsyncDisposable
{
    private const int HandshakeTimeoutSeconds = 3;
    private const int MaxConcurrentClients = 32;
    private static readonly ControlOption[] BitrateOptions =
        Enumerable.Range(2, 14).Select(value => new ControlOption($"{value}M", $"{value} Mb/s")).ToArray();
    private static readonly ControlOption[] ProfileOptions =
    [
        new("Automatic", "Automático"),
        new("Competitive", "Competitivo"),
        new("Balanced", "Balanceado"),
        new("Quality", "Calidad"),
        new("Smooth", "Fluidez"),
        new("Software", "Software")
    ];
    private static readonly ControlOption[] ResolutionOptions =
        new[] { 480, 720, 960, 1080, 1280, 1440, 1600, 1920, 2160 }
            .Select(value => new ControlOption(value.ToString(), $"{value} px (lado mayor)")).ToArray();
    private static readonly ControlOption[] FrameRateOptions =
        new[] { 15, 24, 30, 45, 60 }
            .Select(value => new ControlOption(value.ToString(), $"{value} FPS")).ToArray();

    private sealed class PeerState
    {
        public int Active;
        public int Failures;
        public long BlockedUntil;
        public long LastSeen;
    }

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<IPAddress, PeerState> _peers = new();
    private readonly ConcurrentDictionary<Task, byte> _handlers = new();
    private readonly SemaphoreSlim _slots = new(MaxConcurrentClients, MaxConcurrentClients);
    private readonly SemaphoreSlim _activeSessionGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly LinuxTrustStore _store;
    private readonly IPAddress _address;
    private readonly string _transport;
    private readonly ILinuxLinkEngine? _linkEngine;
    private readonly LinuxVisionEngine? _visionEngine;
    private readonly LinuxVisionLanEngine? _visionLanEngine;
    private readonly TcpListener _listener;
    private Task _run = Task.CompletedTask;
    private TcpClient? _activeClient;
    private string? _activeDeviceId;
    private CancellationTokenSource? _activeSessionCancellation;
    private bool _started;
    private bool _invitationConsumed;
    private int _disposed;
    private string _pairingCode = "";
    private string _pairingSecret = "";
    private DateTimeOffset _invitationExpiry;
    private LinuxVisionSettings _visionSettings = LinuxVisionSettings.Default;
    public event Action<LinuxVisionSettings>? VisionSettingsChanged;
    public LinuxVisionSettings VisionSettings => _visionSettings;
    public object VisionSettingsGate { get; set; } = new();
    private string _bitrate => _visionSettings.Bitrate;
    private string _profile => _visionSettings.Profile;
    private int _maxSize => _visionSettings.MaxSize;
    private int _fps => _visionSettings.Fps;
    private long _revision = 1;
    private long _videoRequestId;
    private bool _videoRequested;
    public void RequestVision()
    {
        if (!_authorized) throw new InvalidOperationException("Enlaza y autoriza LINK Control primero.");
        if (HasVisionSession) throw new InvalidOperationException("Detén la sesión VE actual antes de solicitar otra.");
        _videoRequested = true; Interlocked.Increment(ref _videoRequestId);
        StatusChanged?.Invoke(this,"Autoriza la captura en LINK Control (mantén la app visible en Android).");
    }
    private volatile bool _authorized;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler? InvitationChanged;
    public bool IsAuthorized => _authorized;
    public bool IsRunning => _started && Volatile.Read(ref _disposed) == 0;
    private bool UseLanVision => _transport == "LAN" && _visionLanEngine is not null;
    public bool IsVisionRunning => UseLanVision
        ? _visionLanEngine?.IsRunning == true
        : _visionEngine?.IsRunning == true;
    public bool HasVisionSession => UseLanVision
        ? _visionLanEngine?.HasSession == true
        : _visionEngine?.HasSession == true;
    public string VisionState => UseLanVision
        ? _visionLanEngine?.State ?? "Unavailable"
        : _visionEngine?.State ?? "Unavailable";
    public string VisionMessage => UseLanVision
        ? _visionLanEngine?.Message ?? "VisionEngine Linux no está disponible por esta ruta."
        : _visionEngine?.Message ?? "VisionEngine Linux no está disponible por esta ruta.";
    public LinuxVisionTelemetry? VisionTelemetry => UseLanVision
        ? _visionLanEngine?.Telemetry
        : _visionEngine?.Telemetry;
    public IReadOnlyList<TrustedDevice> TrustedDevices => _store.Devices;

    public LinuxVisionRuntime? VisionRuntime => UseLanVision ? _visionLanEngine?.Runtime : _visionEngine?.Runtime;
    public bool VisionRecording => VisionRuntime?.Recorder.IsRecording == true;

    public Task StopVisionAsync() => UseLanVision
        ? _visionLanEngine?.StopAsync() ?? Task.CompletedTask
        : _visionEngine?.StopAsync() ?? Task.CompletedTask;

    public void ConfigureVisionSettings(string bitrate, string profile, int maxSize, int fps) =>
        ConfigureVisionSettings(_visionSettings with { Bitrate = bitrate, Profile = LinuxVisionSettings.ProfileId(profile), MaxSize = maxSize, Fps = fps });

    public void ConfigureVisionSettings(LinuxVisionSettings settings)
    {
        lock(VisionSettingsGate)
        {
        settings.Validate();
        lock (_gate)
        {
            if (VisionRecording) throw new InvalidOperationException("Detén la grabación antes de cambiar ajustes.");
            _visionSettings = settings;
            if (_visionEngine is not null) _visionEngine.Settings = settings;
            if (_visionLanEngine is not null) _visionLanEngine.Settings = settings;
            Interlocked.Increment(ref _revision);
        }

        }
    }

    public LanInvitation Invitation
    {
        get
        {
            lock (_gate)
                return new(_address.ToString(), Port, _store.Fingerprint,
                    _pairingCode, _invitationExpiry.ToUnixTimeSeconds());
        }
    }

    public ControlInvitation UsbInvitation
    {
        get
        {
            lock (_gate)
            {
                if (_transport != "USB")
                    throw new InvalidOperationException("La invitación USB no está disponible.");
                return new(1, _address.ToString(), Port, _store.Fingerprint,
                    _pairingSecret, _invitationExpiry.ToUnixTimeSeconds());
            }
        }
    }

    public LinuxControlServer(
        IPAddress address,
        LinuxTrustStore store,
        int port = ControlProtocol.Port,
        string transport = "LAN",
        bool allowLoopbackForTests = false,
        ILinuxLinkEngine? linkEngine = null,
        LinuxVisionEngine? visionEngine = null,
        LinuxVisionLanEngine? visionLanEngine = null)
    {
        if (transport is not ("LAN" or "USB") || port is < 0 or > 65535 ||
            transport == "LAN" && !LinuxLanDiscovery.IsPrivateIpv4(address) &&
                !(allowLoopbackForTests && IPAddress.IsLoopback(address)) ||
            transport == "USB" && (!IPAddress.IsLoopback(address) || port != 27214 && !allowLoopbackForTests) ||
            allowLoopbackForTests && !IPAddress.IsLoopback(address))
            throw new InvalidDataException("Selecciona una dirección IPv4 privada de la LAN.");
        _address = address;
        _transport = transport;
        _store = store;
        _linkEngine = linkEngine;
        _visionEngine = visionEngine;
        _visionLanEngine = visionLanEngine;
        if (_linkEngine is not null)
            _linkEngine.StateChanged += OnLinkStateChanged;
        if (_visionEngine is not null)
            _visionEngine.StatusChanged += OnVisionStateChanged;
        if (_visionLanEngine is not null)
            _visionLanEngine.StatusChanged += OnVisionStateChanged;
        _listener = new TcpListener(address, port);
    }

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start()
    {
        if (_started || Volatile.Read(ref _disposed) != 0)
            throw new InvalidOperationException("El servidor ya está iniciado o cerrado.");
        _listener.Start(64);
        _started = true;
        RenewAndGetInvitation();
        _run = RunAsync();
        StatusChanged?.Invoke(this, $"Control {_transport} escuchando en {_address}:{Port}.");
    }

    public LanInvitation RenewAndGetInvitation()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(LinuxControlServer));
        LanInvitation invitation;
        lock (_gate)
        {
            _pairingCode = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
            _pairingSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _invitationExpiry = DateTimeOffset.UtcNow.AddMinutes(2);
            _invitationConsumed = false;
            invitation = new(_address.ToString(), Port, _store.Fingerprint,
                _pairingCode, _invitationExpiry.ToUnixTimeSeconds());
        }
        InvitationChanged?.Invoke(this, EventArgs.Empty);
        return invitation;
    }

    public async Task DisconnectRevokedDeviceAsync(string deviceId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _activeSessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            TcpClient? active;
            CancellationTokenSource? sessionCancellation;
            lock (_gate)
            {
                if (!string.Equals(_activeDeviceId, deviceId, StringComparison.Ordinal))
                    return;
                active = _activeClient;
                sessionCancellation = _activeSessionCancellation;
                _activeClient = null;
                _activeDeviceId = null;
                _activeSessionCancellation = null;
                _authorized = false;
            }

            CancelSession(sessionCancellation);
            active?.Dispose();
            List<Exception>? errors = null;
            try
            {
                if (_visionEngine is not null)
                    await _visionEngine.StopAsync().ConfigureAwait(false);
                if (_visionLanEngine is not null)
                    await _visionLanEngine.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            try
            {
                if (_linkEngine is not null)
                    await _linkEngine.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { (errors ??= []).Add(ex); }

            if (errors is not null)
                throw new AggregateException("No se liberaron todos los motores tras revocar el dispositivo.", errors);
            StatusChanged?.Invoke(this, $"Acceso revocado; sesión y motores detenidos por {_transport}.");
        }
        finally
        {
            _activeSessionGate.Release();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                IPAddress? remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
                if (remote is null)
                {
                    client.Dispose();
                    continue;
                }
                if (!TryEnterPeer(remote, out PeerState? peer) || peer is null)
                {
                    client.Dispose();
                    continue;
                }
                if (!_slots.Wait(0))
                {
                    ExitPeer(peer);
                    client.Dispose();
                    continue;
                }

                Task handler = HandleClientAsync(client, remote, peer);
                _handlers[handler] = 0;
                _ = handler.ContinueWith(static (task, state) =>
                {
                    var handlers = (ConcurrentDictionary<Task, byte>)state!;
                    handlers.TryRemove(task, out _);
                    if (task.IsFaulted && task.Exception is { } error)
                        Debug.WriteLine(error);
                }, _handlers, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
        finally
        {
            TcpClient? active;
            lock (_gate)
            {
                active = _activeClient;
                _activeClient = null;
                _authorized = false;
            }
            active?.Dispose();
            StatusChanged?.Invoke(this, $"El servidor de control {_transport} se detuvo.");
        }
    }

    private bool TryEnterPeer(IPAddress address, out PeerState? state)
    {
        IPAddress key = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (_peers.Count >= 1024 && !_peers.ContainsKey(key))
        {
            foreach (KeyValuePair<IPAddress, PeerState> pair in _peers)
            {
                lock (pair.Value)
                {
                    if (pair.Value.Active == 0 &&
                        Environment.TickCount64 - pair.Value.LastSeen >= TimeSpan.FromMinutes(5).TotalMilliseconds)
                        _peers.TryRemove(pair.Key, out _);
                }
            }
            if (_peers.Count >= 1024)
            {
                state = null;
                return false;
            }
        }

        state = _peers.GetOrAdd(key, _ => new PeerState());
        lock (state)
        {
            long now = Environment.TickCount64;
            if (state.BlockedUntil > now || state.Active >= 4)
                return false;
            state.Active++;
            state.LastSeen = now;
            return true;
        }
    }

    private static void ExitPeer(PeerState state)
    {
        lock (state)
        {
            if (state.Active > 0)
                state.Active--;
            state.LastSeen = Environment.TickCount64;
        }
    }

    private async Task HandleClientAsync(TcpClient client, IPAddress remote, PeerState peer)
    {
        bool authenticated = false;
        CancellationTokenSource? sessionCancellation = null;
        try
        {
            client.NoDelay = true;
            using var tls = new SslStream(client.GetStream(), false);
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            handshake.CancelAfter(TimeSpan.FromSeconds(HandshakeTimeoutSeconds));
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _store.Certificate,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, handshake.Token);

            ControlRequest hello = await ControlProtocol.ReadAsync<ControlRequest>(tls, handshake.Token);
            if (hello.Version != ControlProtocol.Version || hello.Id <= 0 ||
                string.IsNullOrEmpty(hello.Action) || hello.Action.Length > 64)
            {
                await ControlProtocol.WriteAsync(tls,
                    new ControlReply(ControlProtocol.Version, hello.Id, false, "Solicitud de control no válida."),
                    handshake.Token);
                return;
            }

            bool paired = TryConsumeInvitation(hello);
            bool resumed = hello.Action == "resume" && _store.Authenticate(hello.Value, hello.Code);
            if (!paired && !resumed)
            {
                await ControlProtocol.WriteAsync(tls,
                    new ControlReply(ControlProtocol.Version, hello.Id, false,
                        "Autorización rechazada. Genera un enlace nuevo en NOVORA-LINK para Linux."),
                    handshake.Token);
                return;
            }

            string? deviceId = resumed ? hello.Value : null;
            TcpClient? previous;
            CancellationTokenSource? previousSession;
            sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            await _activeSessionGate.WaitAsync(handshake.Token).ConfigureAwait(false);
            try
            {
                lock (_gate)
                {
                    _authorized = true;
                    previous = _activeClient;
                    previousSession = _activeSessionCancellation;
                    _activeClient = client;
                    _activeDeviceId = deviceId;
                    _activeSessionCancellation = sessionCancellation;
                }
            }
            finally { _activeSessionGate.Release(); }
            CancelSession(previousSession);
            previous?.Dispose();
            authenticated = true;
            StatusChanged?.Invoke(this, resumed
                ? $"Dispositivo de confianza conectado por {_transport}: {remote}."
                : $"Nuevo dispositivo enlazado por {_transport}: {remote}.");

            await ControlProtocol.WriteAsync(tls,
                new ControlReply(ControlProtocol.Version, hello.Id, true,
                    paired ? $"Conectado a NOVORA-LINK por {_transport}." : "Dispositivo de confianza conectado.",
                    CreateSnapshot()),
                sessionCancellation.Token);

            long lastId = hello.Id;
            bool enrollmentUsed = false;
            while (!sessionCancellation.IsCancellationRequested)
            {
                ControlRequest request = await ControlProtocol.ReadAsync<ControlRequest>(
                    tls, sessionCancellation.Token);
                if (request.Version != ControlProtocol.Version || request.Id <= lastId ||
                    request.Code is not null || string.IsNullOrEmpty(request.Action) || request.Action.Length > 64)
                    throw new InvalidDataException("Secuencia de control no válida.");
                lastId = request.Id;
                if (deviceId is not null && !_store.Contains(deviceId))
                    throw new AuthenticationException("La confianza del dispositivo fue revocada.");

                ControlReply reply = await HandleRequestAsync(
                    request, paired, enrollmentUsed, sessionCancellation.Token);
                if (request.Action == "trust.enroll" && reply.Success && reply.TrustedPc is not null)
                {
                    deviceId = reply.TrustedPc.DeviceId;
                    enrollmentUsed = true;
                    lock (_gate)
                    {
                        if (_activeClient == client)
                            _activeDeviceId = deviceId;
                    }
                }
                await ControlProtocol.WriteAsync(tls, reply, sessionCancellation.Token);
            }

        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (OperationCanceledException) when (sessionCancellation?.IsCancellationRequested == true) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or AuthenticationException or
                                   SocketException or ObjectDisposedException or System.Text.Json.JsonException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Debug.WriteLine(ex);
            StatusChanged?.Invoke(this, $"Se cerró la sesión por un error interno del control {_transport}.");
        }
        finally
        {
            bool endedCurrentSession = false;
            lock (_gate)
            {
                if (_activeClient == client)
                {
                    _activeClient = null;
                    _activeDeviceId = null;
                    _activeSessionCancellation = null;
                    _authorized = false;
                    _videoRequested = false;
                    endedCurrentSession = true;
                }
            }
            if (endedCurrentSession)
            {
                CancelSession(sessionCancellation);
                if (!_stop.IsCancellationRequested)
                {
                    try { await StopVisionAsync().ConfigureAwait(false); }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or
                                               ObjectDisposedException or AggregateException)
                    {
                        StatusChanged?.Invoke(this,
                            $"No se liberó VisionEngine después de cerrar el control {_transport}: {ex.Message}");
                    }
                    if (_linkEngine is not null)
                    {
                        try { await _linkEngine.StopAsync().ConfigureAwait(false); }
                        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or
                                                   ObjectDisposedException or AggregateException)
                        {
                            StatusChanged?.Invoke(this,
                                $"No se liberó LinkEngine después de cerrar el control {_transport}: {ex.Message}");
                        }
                    }
                }
            }
            sessionCancellation?.Dispose();
            lock (peer)
            {
                peer.Active = Math.Max(0, peer.Active - 1);
                peer.LastSeen = Environment.TickCount64;
                if (!authenticated)
                {
                    peer.Failures++;
                    if (peer.Failures >= 5)
                    {
                        peer.Failures = 0;
                        peer.BlockedUntil = Environment.TickCount64 + (long)TimeSpan.FromSeconds(30).TotalMilliseconds;
                    }
                }
                else
                {
                    peer.Failures = 0;
                    peer.BlockedUntil = 0;
                }
            }
            _slots.Release();
            client.Dispose();
            if (authenticated && !IsAuthorized && !_stop.IsCancellationRequested)
                StatusChanged?.Invoke(this, $"Dispositivo desconectado; NOVORA-LINK sigue disponible por {_transport}.");
        }
    }

    private bool TryConsumeInvitation(ControlRequest hello)
    {
        lock (_gate)
        {
            bool invitationOpen = !_invitationConsumed && DateTimeOffset.UtcNow < _invitationExpiry;
            bool authorized = _transport switch
            {
                "LAN" => hello.Action == "pair.lan" &&
                    hello.Code is { Length: 6 } && hello.Code.All(char.IsAsciiDigit) &&
                    CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(hello.Code), Encoding.ASCII.GetBytes(_pairingCode)),
                "USB" => hello.Action == "pair" &&
                    hello.Code is { Length: 64 } && hello.Code.All(Uri.IsHexDigit) &&
                    CryptographicOperations.FixedTimeEquals(
                        SHA256.HashData(Encoding.UTF8.GetBytes(hello.Code)),
                        SHA256.HashData(Encoding.UTF8.GetBytes(_pairingSecret))),
                _ => false
            };
            if (!invitationOpen || !authorized)
                return false;
            _invitationConsumed = true;
            return true;
        }
    }

    private async Task<ControlReply> HandleRequestAsync(
        ControlRequest request, bool paired, bool enrollmentUsed, CancellationToken sessionToken)
    {
        if (request.Action == "get")
            return new ControlReply(ControlProtocol.Version, request.Id, true,
                "Estado de NOVORA-LINK para Linux.", CreateSnapshot());

        if (request.Action is "network.capabilities" or "network.policy")
        {
            if (!_authorized) return new ControlReply(1,request.Id,false,"Requiere sesión autorizada.");
            try
            {
                if (request.Action=="network.capabilities")
                {
                    LinuxFilesNetwork.ValidateCapabilities(request.Value ?? "");
                    return new ControlReply(1,request.Id,true,"Capacidades FILES confirmadas.",CreateSnapshot());
                }
                if (VisionRuntime?.Files.IsConnected!=true) throw new IOException("FILES V2 no está conectado.");
                var allocation=LinuxFilesNetwork.Allocate(request.Value ?? "",VisionRuntime.Generation,Interlocked.Read(ref _revision),_transport);
                return new ControlReply(1,request.Id,true,"Presupuesto FILES de la sesión actual.",CreateSnapshot(),Value:JsonSerializer.Serialize(allocation));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException)
            { return new ControlReply(1,request.Id,false,ex.Message,CreateSnapshot()); }
        }
        if (request.Action=="applyVideoSettings")
        {
        lock(VisionSettingsGate)
        {
            if (!_authorized || request.Revision!=Interlocked.Read(ref _revision) || VisionRecording)
                return new ControlReply(1,request.Id,false,"Los ajustes cambiaron o hay una grabación activa; consulta el estado.",CreateSnapshot());
            try
            {
                var changes=JsonSerializer.Deserialize<ControlVideoChanges>(request.Value ?? "") ?? throw new InvalidDataException("Ajustes vacíos.");
                if (changes.Audio is not ("Pc" or "Disabled") || changes.PresentationMode is not ("Window" or "Fullscreen") ||
                    !uint.TryParse(changes.Monitor,out uint monitor) || (monitor!=0 && monitor!=_visionSettings.MonitorId) ||
                    !int.TryParse(changes.Resolution,out int size) || !int.TryParse(changes.Fps,out int fps))
                    throw new InvalidDataException("Salida o ajustes de captura inválidos.");
                var proposed=_visionSettings with { Profile=LinuxVisionSettings.ProfileId(changes.Profile),Bitrate=changes.Bitrate,MaxSize=size,Fps=fps,
                    AudioEnabled=changes.Audio!="Disabled",MonitorId=monitor,MonitorName=monitor==0 ? "" : _visionSettings.MonitorName,Fullscreen=changes.PresentationMode=="Fullscreen" };
                if (proposed.Profile!=_visionSettings.Profile)
                {
                    var preset=LinuxVisionSettings.ForProfile(proposed.Profile);
                    proposed=proposed with { Acceleration=preset.Acceleration,LowLatency=preset.LowLatency,VSync=preset.VSync };
                }
                proposed.Validate();
                if (VisionSettingsChanged is not null) VisionSettingsChanged.Invoke(proposed); else ConfigureVisionSettings(proposed);
                return new ControlReply(1,request.Id,true,"Ajustes guardados para la siguiente captura.",CreateSnapshot());
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or UnauthorizedAccessException)
            { return new ControlReply(1,request.Id,false,ex.Message,CreateSnapshot()); }

        }
    }

        if (request.Action is "files.acquire" or "files.cancelLease" or "startRecording" or "stopRecording")
        {
            if (!_authorized || VisionRuntime is not { } runtime)
                return new ControlReply(1,request.Id,false,"Inicia una sesión VE autorizada primero.",CreateSnapshot());
            try
            {
                string? value=null;
                switch (request.Action)
                {
                    case "files.acquire": value=runtime.Files.Acquire(request.Value ?? ""); break;
                    case "files.cancelLease": value=runtime.Files.CancelLease(request.Value ?? ""); break;
                    case "startRecording": lock(VisionSettingsGate) runtime.StartRecording(); break;
                    case "stopRecording": await runtime.StopRecordingAsync(); break;
                }
                return new ControlReply(1,request.Id,true,"Acción VE completada.",CreateSnapshot(),Value:value);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or System.Text.Json.JsonException)
            { return new ControlReply(1,request.Id,false,ex.Message,CreateSnapshot()); }
        }

        if (request.Action is "bitrate" or "profile" or "resolution" or "fps")
        {
        lock(VisionSettingsGate)
        {
            if (!_authorized)
                return new ControlReply(ControlProtocol.Version, request.Id, false,
                    "VisionEngine requiere una sesión autorizada.", CreateSnapshot());
            if (request.Revision != Interlocked.Read(ref _revision))
                return new ControlReply(ControlProtocol.Version, request.Id, false,
                    "Los ajustes cambiaron en PC. Revisa el estado actualizado y vuelve a aplicar.", CreateSnapshot());

            string[] allowed = request.Action switch
            {
                "bitrate" => BitrateOptions.Select(option => option.Value).ToArray(),
                "profile" => ProfileOptions.Select(option => option.Value).ToArray(),
                "resolution" => ResolutionOptions.Select(option => option.Value).ToArray(),
                "fps" => FrameRateOptions.Select(option => option.Value).ToArray(),
                _ => []
            };
            if (request.Value is null || !allowed.Contains(request.Value, StringComparer.Ordinal))
                return new ControlReply(ControlProtocol.Version, request.Id, false,
                    "Valor de VisionEngine no admitido por esta PC.", CreateSnapshot());

            if (VisionRecording)
                return new ControlReply(1, request.Id, false, "Detén la grabación antes de cambiar ajustes.", CreateSnapshot());
            LinuxVisionSettings proposed = request.Action switch
            {
                "bitrate" => _visionSettings with { Bitrate = request.Value },
                "profile" => LinuxVisionSettings.ForProfile(request.Value) with {
                    AudioEnabled = _visionSettings.AudioEnabled, AudioDeviceId = _visionSettings.AudioDeviceId,
                    MonitorId = _visionSettings.MonitorId, MonitorName = _visionSettings.MonitorName, AudioDeviceName = _visionSettings.AudioDeviceName, Fullscreen = _visionSettings.Fullscreen,
                    RemoteInput = _visionSettings.RemoteInput, RecordingDirectory = _visionSettings.RecordingDirectory,
                    ReceiveDirectory = _visionSettings.ReceiveDirectory },
                "resolution" => _visionSettings with { MaxSize = int.Parse(request.Value) },
                "fps" => _visionSettings with { Fps = int.Parse(request.Value) },
                _ => _visionSettings
            };
            try
            {
                if (VisionSettingsChanged is not null) VisionSettingsChanged.Invoke(proposed);
                else ConfigureVisionSettings(proposed);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
            { return new ControlReply(1, request.Id, false, ex.Message, CreateSnapshot()); }
            return new ControlReply(ControlProtocol.Version, request.Id, true,
                "Ajuste de VisionEngine actualizado.", CreateSnapshot());

        }
    }

        if (request.Action == "trust.enroll")
        {
            if (!paired || enrollmentUsed)
                return new ControlReply(ControlProtocol.Version, request.Id, false,
                    "Recordar requiere un enlace LAN nuevo.");
            try
            {
                (string id, string token) = _store.Enroll(request.Value ?? "");
                var trustedPc = new TrustedPc(Environment.MachineName, _address.ToString(),
                    Port, _store.Fingerprint, id, token, _transport);
                return new ControlReply(ControlProtocol.Version, request.Id, true,
                    "NOVORA-LINK para Linux guardado como PC de confianza.", TrustedPc: trustedPc);
            }
            catch (InvalidDataException ex)
            {
                return new ControlReply(ControlProtocol.Version, request.Id, false, ex.Message);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return new ControlReply(ControlProtocol.Version, request.Id, false,
                    "No se pudo guardar la confianza del teléfono en el perfil Linux.");
            }
        }

        if (request.Action is "pair" or "pair.lan" or "resume" ||
            request.Action.StartsWith("trust.", StringComparison.Ordinal))
            return new ControlReply(ControlProtocol.Version, request.Id, false,
                "Acción de confianza no permitida.");

        if (request.Action is "startAppVideo" or "startVideo" or "restartVideo" or "startVideoLan")
        {
            bool lanRequest = request.Action == "startVideoLan";
            if (!_authorized ||
                lanRequest && (!UseLanVision || _visionLanEngine is null) ||
                !lanRequest && (_transport != "USB" || _visionEngine is null))
                return new ControlReply(ControlProtocol.Version, request.Id, false,
                    lanRequest
                        ? "VisionEngine LAN requiere una sesión LAN autorizada."
                        : "VisionEngine Linux requiere una sesión USB autorizada.", CreateSnapshot());
            try
            {
                if (request.Action == "restartVideo" && HasVisionSession)
                    await StopVisionAsync();
                string offer = lanRequest
                    ? await _visionLanEngine!.StartAsync(
                        audioEnabled: _visionSettings.AudioEnabled && request.Value != "video-only",
                        cancellationToken: sessionToken,
                        bitrate: ParseBitrate(_bitrate),
                        maxSize: _maxSize,
                        fps: _fps)
                    : await _visionEngine!.StartAsync(
                        sessionToken,
                        ParseBitrate(_bitrate),
                        _maxSize,
                        _fps);
                _videoRequested = false;
                return new ControlReply(ControlProtocol.Version, request.Id, true,
                    lanRequest
                        ? "VisionEngine LAN está esperando la autorización de captura."
                        : "VisionEngine Linux está esperando el flujo de pantalla autorizado.",
                    CreateSnapshot(), Value: offer);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or
                                       System.ComponentModel.Win32Exception or SocketException or
                                       OperationCanceledException or UnauthorizedAccessException)
            {
                return new ControlReply(ControlProtocol.Version, request.Id, false,
                    $"No se pudo iniciar VisionEngine: {ex.Message}", CreateSnapshot());
            }
        }

        if (request.Action == "stopVideo")
        {
            if (!_authorized || !HasVisionSession)
                return new ControlReply(ControlProtocol.Version, request.Id, false,
                    "No hay una sesión VisionEngine para detener.", CreateSnapshot());
            try
            {
                await StopVisionAsync();
                return new ControlReply(ControlProtocol.Version, request.Id, true,
                    "VisionEngine detenido.", CreateSnapshot());
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or
                                       System.ComponentModel.Win32Exception or SocketException)
            {
                return new ControlReply(ControlProtocol.Version, request.Id, false,
                    $"No se pudo detener VisionEngine: {ex.Message}", CreateSnapshot());
            }
        }

        if (request.Action == "startLink")
        {
            if (!_authorized || _linkEngine is null ||
                request.Value is not null && request.Value != _transport)
                return new ControlReply(ControlProtocol.Version, request.Id, false,
                    $"LinkEngine Linux requiere una sesión {_transport} autorizada y disponible.", CreateSnapshot());
            try
            {
                string offer = await _linkEngine.StartAsync(sessionToken);
                return new ControlReply(ControlProtocol.Version, request.Id, true,
                    _transport == "LAN"
                        ? "LinkEngine LAN preparado. Android abrirá el canal DATA TLS autenticado."
                        : "Inicio de LinkEngine aceptado. Esperando la conexión CONTROL y el túnel DATA de Android.",
                    CreateSnapshot(), Value: offer);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or
                                       System.ComponentModel.Win32Exception or SocketException or
                                       OperationCanceledException)
            {
                return new ControlReply(ControlProtocol.Version, request.Id, false,
                    $"No se pudo iniciar LinkEngine por {_transport}: {ex.Message}", CreateSnapshot());
            }
        }

        if (request.Action == "stopLink")
        {
            if (!_authorized || _linkEngine is null || !_linkEngine.IsStarted)
                return new ControlReply(ControlProtocol.Version, request.Id, false,
                    $"No hay una sesión LinkEngine {_transport} para detener.", CreateSnapshot());
            try
            {
                await _linkEngine.StopAsync();
                return new ControlReply(ControlProtocol.Version, request.Id, true,
                    "LinkEngine detenido.", CreateSnapshot());
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or
                                       System.ComponentModel.Win32Exception or SocketException)
            {
                return new ControlReply(ControlProtocol.Version, request.Id, false,
                    $"No se pudo detener LinkEngine: {ex.Message}", CreateSnapshot());
            }
        }

        string message = request.Action switch
        {
            "link.speedtest" =>
                "La prueba de velocidad de LinkEngine aún no está integrada en la aplicación Linux.",
            "exin.start" or "exin.mode" or "exin.synchronize" =>
                "ExInEngine aún no está integrado en la aplicación Linux.",
            "file.begin" or "file.chunk" or "file.end" or "file.cancel" or
                "files.acquire" or "files.cancelLease" =>
                "La transferencia de archivos aún no está integrada en la aplicación Linux.",
            _ => "La acción aún no está disponible en NOVORA-LINK para Linux."
        };
        return new ControlReply(ControlProtocol.Version, request.Id, false,
            message, CreateSnapshot());
    }

    private ControlSnapshot CreateSnapshot()
    {
        lock(VisionSettingsGate)
        {
        bool videoRunning = IsVisionRunning;
        bool videoCanStop = _authorized && HasVisionSession;
        bool videoCanStart = _authorized && (UseLanVision
            ? _visionLanEngine is not null && !_visionLanEngine.HasSession
            : _transport == "USB" && _visionEngine is not null && !_visionEngine.HasSession);
        return new ControlSnapshot(
        Revision: Interlocked.Read(ref _revision),
        PcName: Environment.MachineName,
        PcVersion: "Linux preview · LINK Control protocol v1",
        Bitrate: _bitrate,
        Profile: _profile,
        AudioOutput: _visionSettings.AudioEnabled ? "Pc" : "Disabled",
        ActiveAudioOutput: VisionRuntime?.AudioPlaying == true ? "Pc" : "Disabled",
        VideoRunning: videoRunning,
        Bitrates: BitrateOptions,
        Profiles: ProfileOptions,
        AudioOutputs: [new("Pc", "Salida de Linux"), new("Disabled", "Desactivado")],
        Engines: new ControlEngines(
            VideoCanStart: videoCanStart,
            VideoCanStop: videoCanStop,
            LinkCanStart: _authorized && _linkEngine is not null && !_linkEngine.IsStarted,
            LinkCanStop: _authorized && _linkEngine?.IsStarted == true,
            LinkRunning: _linkEngine?.IsConnected == true,
            LinkState: _linkEngine?.State ?? "Unavailable",
            LinkMessage: _linkEngine?.Message ??
                $"LinkEngine Linux no está disponible por esta ruta {_transport}.",
            DeviceName: "",
            VideoState: VisionState,
            VideoMessage: VisionMessage,
            VisionTelemetry: VisionTelemetry,
            VideoAuthorizationRequested: _authorized && _videoRequested,
            VideoAuthorizationRequestId: Interlocked.Read(ref _videoRequestId)),
        VideoSettings: new ControlVideoSettings(
            _maxSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _fps.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ResolutionOptions,
            FrameRateOptions, Monitor: _visionSettings.MonitorId.ToString(),
            Monitors: _visionSettings.MonitorId==0 ? [new("0","Predeterminado del sistema")] : [new("0","Predeterminado del sistema"),new(_visionSettings.MonitorId.ToString(),"Monitor seleccionado en Linux")],
            CanApplyTogether: true, PresentationMode: _visionSettings.Fullscreen ? "Fullscreen" : "Window",
            PresentationModes: [new("Window","Ventana"),new("Fullscreen","Pantalla completa")]),
        Media: new ControlMedia(false, IsVisionRunning, VisionRecording, VisionRuntime?.Recorder.Status ?? "Sin sesión VE.", AudioIncluded: VisionRuntime?.AudioPlaying == true),
        FileSharing: VisionRuntime?.Files.IsConnected == true,
        Capabilities: new { Version=1, FilesWireVersions=new[]{2} });

        }
    }

    private static int ParseBitrate(string bitrate) =>
        checked(int.Parse(bitrate[..^1], System.Globalization.CultureInfo.InvariantCulture) * 1_000_000);

    private void OnLinkStateChanged(object? sender, EventArgs e) =>
        StatusChanged?.Invoke(this, _linkEngine?.Message ?? "Estado de LinkEngine actualizado.");

    private void OnVisionStateChanged(object? sender, EventArgs e) =>
        StatusChanged?.Invoke(this, VisionMessage);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _stop.Cancel();
        _listener.Stop();
        if (_linkEngine is not null)
            _linkEngine.StateChanged -= OnLinkStateChanged;
        if (_visionEngine is not null)
        {
            _visionEngine.StatusChanged -= OnVisionStateChanged;
            await _visionEngine.DisposeAsync();
        }
        if (_visionLanEngine is not null)
        {
            _visionLanEngine.StatusChanged -= OnVisionStateChanged;
            await _visionLanEngine.DisposeAsync();
        }
        TcpClient? active;
        CancellationTokenSource? sessionCancellation;
        lock (_gate)
        {
            active = _activeClient;
            sessionCancellation = _activeSessionCancellation;
            _activeClient = null;
            _activeDeviceId = null;
            _activeSessionCancellation = null;
            _authorized = false;
        }
        CancelSession(sessionCancellation);
        active?.Dispose();
        await _run;
        Task[] handlers = _handlers.Keys.ToArray();
        if (handlers.Length > 0)
        {
            try { await Task.WhenAll(handlers).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { }
        }
        _slots.Dispose();
        _stop.Dispose();
    }

    private static void CancelSession(CancellationTokenSource? session)
    {
        if (session is null)
            return;
        try { session.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}
