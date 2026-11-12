using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;

namespace NOVORA.Control;

/// <summary>Authenticated TLS bridge from one trusted LAN client to NOVORA relay's loopback DATA port.</summary>
public sealed class NLControlLanDataGateway : IAsyncDisposable
{
    private const int HandshakeTimeoutSeconds = 3;
    private const int MaxConcurrentHandshakes = 32;
    private readonly TcpListener _listener;
    private readonly NLControlTrustStore _store;
    private readonly CancellationTokenSource _stop = new();
    private readonly NLControlLanPeerGuard _peers = new(maxConcurrentPerPeer: 4, maxFailures: 5,
        blockDuration: TimeSpan.FromSeconds(30));
    private readonly SemaphoreSlim _handshakeSlots = new(MaxConcurrentHandshakes, MaxConcurrentHandshakes);
    private readonly ConcurrentDictionary<Task, byte> _handlers = new();
    private readonly object _sessionGate = new();
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly string? _relayToken;
    private readonly int _targetPort;
    private Task _run = Task.CompletedTask;
    private TcpClient? _active;
    private bool _started;
    private volatile bool _connected;
    private long _connectionGeneration;
    public long ConnectionGeneration => Interlocked.Read(ref _connectionGeneration);
    public bool IsConnected => _connected;

    public NLControlLanDataGateway(IPAddress address, NLControlTrustStore store, int targetPort = 27184,
        bool allowLoopback = false, string? relayToken = null)
    {
        NLControlLanInvitation.ValidateAddress(address, allowLoopback);
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _targetPort = targetPort is > 0 and <= 65535 ? targetPort : throw new ArgumentOutOfRangeException(nameof(targetPort));
        if (relayToken is not null && !NLControlVpnProtocol.IsValidSessionToken(relayToken))
            throw new ArgumentException("Token del relay inválido.", nameof(relayToken));
        _relayToken = relayToken?.Trim().ToLowerInvariant();
        _listener = new TcpListener(address, 0);
    }

    public event EventHandler? StateChanged;

    public NLControlLinkOffer Start()
    {
        if (_started) throw new InvalidOperationException("El gateway DATA LAN ya está iniciado.");
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
                if (!_peers.TryEnter(remote, out NLControlLanPeerLease? lease))
                {
                    client.Dispose();
                    continue;
                }
                if (!_handshakeSlots.Wait(0))
                {
                    lease?.Dispose();
                    client.Dispose();
                    continue;
                }
                Task handler = HandleClientAsync(client, remote, lease);
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

    private async Task HandleClientAsync(TcpClient client, IPAddress remote, NLControlLanPeerLease? lease)
    {
        bool authenticated = false;
        bool handshakeSlotReleased = false;
        void ReleaseHandshakeSlot()
        {
            if (handshakeSlotReleased) return;
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
            // Keep a malformed LAN client from faulting the accept loop.
        }
        finally
        {
            if (authenticated) _peers.ReportSuccess(remote);
            else _peers.ReportFailure(remote);
            lease?.Dispose();
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
            client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
            client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);
            client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
        }
        catch (Exception ex) when (ex is SocketException or PlatformNotSupportedException) { }
        using var tls = new SslStream(client.GetStream(), false);
        using var authorization = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        authorization.CancelAfter(TimeSpan.FromSeconds(HandshakeTimeoutSeconds));
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = _store.Certificate,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, authorization.Token);
        NLControlLinkHello hello = await NLControlProtocol.ReadAsync<NLControlLinkHello>(tls, authorization.Token);
        if (hello.Version != NLControlProtocol.Version || hello.Channel != "DATA" || hello.Token is not { Length: 64 } ||
            !hello.Token.All(Uri.IsHexDigit) ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hello.Token), Convert.FromHexString(_token)))
            throw new AuthenticationException("Autorización DATA LAN rechazada.");

        using var relay = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        await relay.ConnectAsync(IPAddress.Loopback, _targetPort, authorization.Token);
        using NetworkStream local = relay.GetStream();
        if (_relayToken is not null)
        {
            await NLControlVpnProtocol.WriteControlAsync(
                local,
                NLControlVpnProtocol.CreateDataAuthentication(_relayToken),
                authorization.Token);
            string relayAck = await NLControlVpnProtocol.ReadControlAsync(
                local,
                authorization.Token);
            if (!NLControlVpnProtocol.IsDataAuthenticationAck(relayAck))
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
            if (changed) StateChanged?.Invoke(this, EventArgs.Empty);
        }
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_started) return;
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
        try { await _run; } catch (OperationCanceledException) { }
        Task[] handlers = _handlers.Keys.ToArray();
        if (handlers.Length > 0)
        {
            try { await Task.WhenAll(handlers).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or ObjectDisposedException) { }
        }
        _stop.Dispose();
    }
}
