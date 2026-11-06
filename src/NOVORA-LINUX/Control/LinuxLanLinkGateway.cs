using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;

namespace NOVORA.Linux.Control;

internal sealed record LanLinkOffer(string Host, int Port, string Fingerprint, string Token,
    string Transport = "LAN");

/// <summary>
/// Authenticated TLS bridge from one trusted LAN client to the LinkEngine
/// relay's loopback DATA port. Mirrors the Windows NLControlLanDataGateway
/// contract: pinned certificate, single-use token, DATA authentication and a
/// single active tunnel that Android may renew.
/// </summary>
internal sealed class LinuxLanLinkGateway : IAsyncDisposable
{
    private const int HandshakeTimeoutSeconds = 3;
    private const int MaxConcurrentHandshakes = 32;
    private const int MaxConnectionsPerPeer = 4;
    private const int MaxPeerFailures = 5;

    private sealed class PeerState
    {
        public int Active;
        public int Failures;
        public long BlockedUntil;
    }

    private readonly TcpListener _listener;
    private readonly LinuxTrustStore _store;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _handshakeSlots = new(MaxConcurrentHandshakes, MaxConcurrentHandshakes);
    private readonly ConcurrentDictionary<Task, byte> _handlers = new();
    private readonly ConcurrentDictionary<IPAddress, PeerState> _peers = new();
    private readonly object _sessionGate = new();
    private readonly string? _relayToken;
    private readonly int _targetPort;
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private Task _run = Task.CompletedTask;
    private TcpClient? _active;
    private bool _started;
    private volatile bool _connected;
    private long _connectionGeneration;

    public LinuxLanLinkGateway(IPAddress address, LinuxTrustStore store, int targetPort = 27184,
        string? relayToken = null, bool allowLoopback = false)
    {
        if (!allowLoopback && !LinuxLanDiscovery.IsPrivateIpv4(address))
            throw new InvalidDataException("El gateway LAN requiere una dirección IPv4 privada.");
        if (allowLoopback && !IPAddress.IsLoopback(address))
            throw new InvalidDataException("La dirección de prueba del gateway debe ser loopback.");
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _targetPort = targetPort is > 0 and <= 65535
            ? targetPort
            : throw new ArgumentOutOfRangeException(nameof(targetPort));
        if (relayToken is not null && !LinuxLinkProtocol.IsToken(relayToken))
            throw new ArgumentException("Token del relay inválido.", nameof(relayToken));
        _relayToken = relayToken?.Trim().ToLowerInvariant();
        _listener = new TcpListener(address, 0);
    }

    public event EventHandler? StateChanged;
    public bool IsConnected => _connected;
    public long ConnectionGeneration => Interlocked.Read(ref _connectionGeneration);

    public LanLinkOffer Start()
    {
        if (_started)
            throw new InvalidOperationException("El gateway DATA LAN ya está iniciado.");
        _listener.Start(32);
        _started = true;
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        _run = RunAsync();
        return new(endpoint.Address.ToString(), endpoint.Port, _store.Fingerprint, _token);
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
                if (!_handshakeSlots.Wait(0))
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
                }, _handlers, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (Exception ex) when (_stop.IsCancellationRequested &&
            ex is OperationCanceledException or SocketException or ObjectDisposedException or InvalidOperationException) { }
        finally
        {
            TcpClient? active;
            lock (_sessionGate)
            {
                active = _active;
                _active = null;
            }
            _connected = false;
            active?.Dispose();
        }
    }

    private bool TryEnterPeer(IPAddress address, out PeerState? state)
    {
        IPAddress key = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        state = _peers.GetOrAdd(key, _ => new PeerState());
        lock (state)
        {
            long now = Environment.TickCount64;
            if (state.BlockedUntil > now || state.Active >= MaxConnectionsPerPeer)
                return false;
            state.Active++;
            return true;
        }
    }

    private static void ExitPeer(PeerState state, bool authenticated = false)
    {
        lock (state)
        {
            if (state.Active > 0)
                state.Active--;
            if (authenticated)
            {
                state.Failures = 0;
                state.BlockedUntil = 0;
            }
            else
            {
                state.Failures++;
                if (state.Failures >= MaxPeerFailures)
                {
                    state.Failures = 0;
                    state.BlockedUntil = Environment.TickCount64 +
                        (long)TimeSpan.FromSeconds(30).TotalMilliseconds;
                }
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, IPAddress remote, PeerState peer)
    {
        bool authenticated = false;
        bool handshakeSlotReleased = false;
        void ReleaseHandshakeSlot()
        {
            if (handshakeSlotReleased)
                return;
            handshakeSlotReleased = true;
            _handshakeSlots.Release();
        }

        try
        {
            authenticated = await ServeAsync(client, ReleaseHandshakeSlot);
        }
        catch (Exception ex) when (ex is IOException or AuthenticationException or InvalidDataException or
            OperationCanceledException or SocketException or ObjectDisposedException or JsonException) { }
        catch (Exception)
        {
            // A malformed LAN client must not fault the accept loop.
        }
        finally
        {
            ExitPeer(peer, authenticated);
            ReleaseHandshakeSlot();
            client.Dispose();
        }
    }

    private async Task<bool> ServeAsync(TcpClient client, Action releaseHandshakeSlot)
    {
        client.NoDelay = true;
        try
        {
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        }
        catch (SocketException) { }

        using var tls = new SslStream(client.GetStream(), false);
        using var authorization = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        authorization.CancelAfter(TimeSpan.FromSeconds(HandshakeTimeoutSeconds));
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = _store.Certificate,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, authorization.Token);

        LinkHello hello = await ControlProtocol.ReadAsync<LinkHello>(tls, authorization.Token);
        if (hello.Version != ControlProtocol.Version || hello.Channel != "DATA" ||
            !LinuxLinkProtocol.IsToken(hello.Token) ||
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(hello.Token), Convert.FromHexString(_token)))
            throw new AuthenticationException("Autorización DATA LAN rechazada.");

        using var relay = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        await relay.ConnectAsync(IPAddress.Loopback, _targetPort, authorization.Token);
        using NetworkStream local = relay.GetStream();
        if (_relayToken is not null)
        {
            await LinuxLinkProtocol.WriteFrameAsync(
                local, LinuxLinkProtocol.CreateDataAuthentication(_relayToken), authorization.Token);
            string relayAck = await LinuxLinkProtocol.ReadFrameAsync(local, authorization.Token);
            if (!LinuxLinkProtocol.IsDataAuthenticationAck(relayAck))
                throw new AuthenticationException("El relay local rechazó la autenticación DATA LAN.");
        }

        releaseHandshakeSlot();
        TcpClient? previous;
        lock (_sessionGate)
        {
            previous = _active;
            _active = client;
            _connected = true;
            Interlocked.Increment(ref _connectionGeneration);
        }
        previous?.Dispose();
        StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            Task toRelay = tls.CopyToAsync(local, _stop.Token);
            Task toAndroid = local.CopyToAsync(tls, _stop.Token);
            await await Task.WhenAny(toRelay, toAndroid);
        }
        finally
        {
            bool changed = false;
            lock (_sessionGate)
            {
                if (ReferenceEquals(_active, client))
                {
                    _active = null;
                    _connected = false;
                    changed = true;
                }
            }
            if (changed)
                StateChanged?.Invoke(this, EventArgs.Empty);
        }
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_started)
            return;
        _started = false;
        _stop.Cancel();
        _listener.Stop();
        TcpClient? active;
        lock (_sessionGate)
        {
            active = _active;
            _active = null;
        }
        _connected = false;
        active?.Dispose();
        try { await _run; }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        Task[] handlers = _handlers.Keys.ToArray();
        if (handlers.Length > 0)
        {
            try { await Task.WhenAll(handlers).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or ObjectDisposedException) { }
        }
        _stop.Dispose();
    }
}

internal sealed record LinkHello(int Version, string Token, string Channel = "DATA");
