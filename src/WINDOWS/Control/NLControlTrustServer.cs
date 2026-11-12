using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
namespace NOVORA.Control;
/// <summary>Persistent pinned TLS listener. New trust is granted only by a fresh, single-use pairing session.</summary>
public sealed class NLControlTrustServer : IAsyncDisposable
{
    private const int HandshakeTimeoutSeconds = 3;
    private const int MaxConcurrentHandshakes = 64;
    private readonly TcpListener _listener;
    private readonly IPAddress _address;
    private readonly Func<NLControlRequest, Task<NLControlReply>> _handler;
    private readonly NLControlTrustStore _store;
    private readonly CancellationTokenSource _stop = new();
    private readonly NLControlLanPeerGuard? _peers;
    private readonly SemaphoreSlim _handshakeSlots = new(MaxConcurrentHandshakes, MaxConcurrentHandshakes);
    private readonly ConcurrentDictionary<Task, byte> _handlers = new();
    private readonly object _sessionGate = new();
    private System.Threading.Timer? _expiry;
    private readonly string _secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly string _pairingCode = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
    private readonly bool _allowPairing;
    private readonly Func<bool>? _allowRemember;
    private readonly string Transport;
    private Channel<NLControlReply>? _outgoing;
    private TcpClient? _client;
    private Task _run = Task.CompletedTask;
    private bool _started;
    private int _consumed, _disposed;
    private long _sessionGeneration;
    private volatile bool _authorized;
    public NLControlLanInvitation Invitation { get; private set; } = null!;
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public bool IsAuthorized => _authorized;
    /// <summary>IP del teléfono autorizado en este canal LAN; null si no hay sesión autorizada.</summary>
    public string? PeerHost { get; private set; }
    public bool IsClosed { get; private set; }
    public bool IsInvitationOpen => _started && !IsClosed && _allowPairing && Volatile.Read(ref _consumed) == 0 && DateTimeOffset.UtcNow.ToUnixTimeSeconds() < Invitation.ExpiresUnix;
    public string PairingCode => _allowPairing ? _pairingCode : string.Empty;
    public event EventHandler<string>? StatusChanged;
    public NLControlTrustServer(IPAddress address, Func<NLControlRequest, Task<NLControlReply>> handler, NLControlTrustStore store, int port = 27215, bool allowPairing = true, Func<bool>? allowRemember = null, string transport = "LAN")
    {
        NLControlLanInvitation.ValidateAddress(address, true);
        _address = address; _handler = handler; _store = store; _allowPairing = allowPairing;
        _allowRemember = allowRemember;
        if (transport is not ("USB" or "LAN") || transport == "USB" && (!IPAddress.IsLoopback(address) || port != NLControlProtocol.Port)) throw new ArgumentException("Transporte o destino USB inválido.");
        Transport = transport;
        // USB reverse makes every Android app appear as 127.0.0.1, so per-IP
        // failure blocking would let one app deny the legitimate client.
        _peers = transport == "LAN"
            ? new NLControlLanPeerGuard(maxConcurrentPerPeer: 4, maxFailures: 5,
                blockDuration: TimeSpan.FromSeconds(30))
            : null;
        _listener = new TcpListener(address, port);
    }
    public void Start()
    {
        if (_started || Volatile.Read(ref _disposed) != 0) throw new InvalidOperationException("Servidor ya iniciado o cerrado.");
        _listener.Start(64); _started = true;
        Invitation = new(1, _address.ToString(), Port, _store.Fingerprint, _secret, DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds());
        if (_allowPairing) _expiry = new System.Threading.Timer(_ => { if (!_authorized && Interlocked.Exchange(ref _consumed, 1) == 0) StatusChanged?.Invoke(this, "Invitación vencida; confianza previa disponible."); }, null, TimeSpan.FromMinutes(2), Timeout.InfiniteTimeSpan);
        _run = RunAsync();
    }
    public void CancelInvitation() { Interlocked.Exchange(ref _consumed, 1); _expiry?.Change(Timeout.Infinite, Timeout.Infinite); }
    public void Publish(NLControlSnapshot snapshot)
    {
        lock (_sessionGate)
        {
            if (_authorized && _outgoing is { } channel &&
                !channel.Writer.TryWrite(new(1, 0, true, "Estado actualizado.", snapshot)))
                _client?.Dispose();
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
                NLControlLanPeerLease? lease = null;
                if (_peers is not null && !_peers.TryEnter(remote, out lease))
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
        catch (InvalidOperationException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) when (_stop.IsCancellationRequested &&
            ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        finally
        {
            IsClosed = true;
            Channel<NLControlReply>? outgoing;
            TcpClient? active;
            lock (_sessionGate)
            {
                _authorized = false;
                outgoing = _outgoing;
                active = _client;
                _outgoing = null;
                _client = null;
                PeerHost = null;
            }
            outgoing?.Writer.TryComplete();
            active?.Dispose();
            _listener.Stop();
            StatusChanged?.Invoke(this, $"Control {Transport} detenido.");
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
            OperationCanceledException or SocketException or ObjectDisposedException or
            System.Text.Json.JsonException or ChannelClosedException) { }
        catch (Exception)
        {
            // A malformed client must never fault the listener or escape as an
            // unobserved task exception.
        }
        finally
        {
            if (_peers is not null)
            {
                if (authenticated) _peers.ReportSuccess(remote);
                else _peers.ReportFailure(remote);
            }
            lease?.Dispose();
            ReleaseHandshakeSlot();
            client.Dispose();
            if (authenticated && !IsAuthorized && !_stop.IsCancellationRequested)
                StatusChanged?.Invoke(this, $"{Transport} disponible para dispositivos de confianza.");
        }
    }
    private async Task<bool> ServeAsync(TcpClient client, Action releaseHandshakeSlot)
    {
        ConfigureLiveness(client);
        using var tls = new SslStream(client.GetStream(), false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(HandshakeTimeoutSeconds));
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        { ServerCertificate = _store.Certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, timeout.Token);
        var hello = await NLControlProtocol.ReadAsync<NLControlRequest>(tls, timeout.Token);
        // The six-digit LAN code is never accepted over USB reverse: every app
        // on the phone appears as 127.0.0.1, so it cannot be rate-limited per app.
        bool pairedByLanCode = Transport == "LAN" && hello.Action == "pair.lan" && IsInvitationOpen &&
            hello.Code is { Length: 6 } && hello.Code.All(char.IsAsciiDigit) &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(hello.Code), Encoding.ASCII.GetBytes(_pairingCode));
        bool pairedByPinnedInvitation = hello.Action == "pair" && IsInvitationOpen &&
            hello.Code is { Length: 64 } && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(hello.Code)),
                SHA256.HashData(Encoding.UTF8.GetBytes(_secret)));
        bool paired = pairedByLanCode || pairedByPinnedInvitation;
        bool resumed = hello.Action == "resume" && _store.Authenticate(hello.Value, hello.Code);
        if (hello.Version != 1 || hello.Id <= 0 || string.IsNullOrEmpty(hello.Action) || !(paired || resumed))
        { await NLControlProtocol.WriteAsync(tls, new NLControlReply(1, hello.Id, false, "Autorización rechazada. Prepara un enlace nuevo en PC."), timeout.Token); return false; }
        if (paired) CancelInvitation();
        string? deviceId = resumed ? hello.Value : null;
        bool enrollmentUsed = false;
        var channel = Channel.CreateBounded<NLControlReply>(32);
        TcpClient? previousClient;
        Channel<NLControlReply>? previousChannel;
        long sessionId;
        lock (_sessionGate)
        {
            previousChannel = _outgoing;
            previousClient = _client;
            _outgoing = channel;
            _client = client;
            _authorized = true;
            sessionId = ++_sessionGeneration;
        }
        releaseHandshakeSlot();
        previousChannel?.Writer.TryComplete();
        previousClient?.Dispose();
        PeerHost = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString();
        StatusChanged?.Invoke(this, resumed ? $"Android de confianza conectado por {Transport}." : $"Android autorizado por {Transport}.");
        Task writer = WriteAsync(client, tls, channel);
        bool registered = true;
        try
        {
            await channel.Writer.WriteAsync(await _handler(hello with { Action = "pair", Code = null, Value = null }), _stop.Token);
            long lastId = hello.Id;
            while (!_stop.IsCancellationRequested)
            {
                var request = await NLControlProtocol.ReadAsync<NLControlRequest>(tls, _stop.Token);
                if (request.Version != 1 || request.Id <= lastId || request.Code is not null || string.IsNullOrEmpty(request.Action) || request.Action.Length > 64) throw new InvalidDataException("Secuencia inválida.");
                lastId = request.Id;
                if (deviceId is not null && !_store.Contains(deviceId)) throw new AuthenticationException("Confianza revocada.");
                NLControlReply reply;
                if (request.Action == "trust.enroll")
                {
                    if (!paired || enrollmentUsed || _allowRemember?.Invoke() == false) reply = new(1, request.Id, false, "Recordar no está habilitado o requiere un enlace nuevo.");
                    else
                    {
                        try
                        {
                            var grant = _store.Enroll(request.Value ?? "");
                            deviceId = grant.Id; enrollmentUsed = true;
                            reply = new(1, request.Id, true, "PC guardada como confiable.", TrustedPc: new(Environment.MachineName, _address.ToString(), Port, _store.Fingerprint, grant.Id, grant.Token, Transport));
                        }
                        catch (InvalidDataException ex) { reply = new(1, request.Id, false, ex.Message); }
                    }
                }
                else if (request.Action is "pair" or "pair.lan" or "resume" || request.Action.StartsWith("trust.", StringComparison.Ordinal)) reply = new(1, request.Id, false, "Acción de confianza no permitida.");
                else reply = await _handler(request);
                await channel.Writer.WriteAsync(reply, _stop.Token);
            }
        }
        catch (Exception ex) when (ex is IOException or AuthenticationException or InvalidDataException or OperationCanceledException or SocketException or ObjectDisposedException or System.Text.Json.JsonException or ChannelClosedException) { }
        catch (Exception ex) when (registered && ex is not OutOfMemoryException) { }
        finally
        {
            bool current;
            lock (_sessionGate)
            {
                current = _sessionGeneration == sessionId;
                if (current)
                {
                    _authorized = false;
                    _client = null;
                    _outgoing = null;
                    PeerHost = null;
                }
            }
            channel.Writer.TryComplete();
            client.Dispose();
            try { await writer; } catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException or ChannelClosedException) { }
        }
        return registered;
    }
    private static void ConfigureLiveness(TcpClient client)
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
    }

    private async Task WriteAsync(TcpClient client, Stream stream, Channel<NLControlReply> channel)
    {
        try
        {
            await foreach (var reply in channel.Reader.ReadAllAsync(_stop.Token))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await NLControlProtocol.WriteAsync(stream, reply, timeout.Token);
            }
        }
        finally { channel.Writer.TryComplete(); client.Dispose(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_expiry is not null) await _expiry.DisposeAsync();
        _stop.Cancel(); _listener.Stop();
        Channel<NLControlReply>? outgoing;
        TcpClient? active;
        lock (_sessionGate)
        {
            _authorized = false;
            outgoing = _outgoing;
            active = _client;
            _outgoing = null;
            _client = null;
            PeerHost = null;
        }
        outgoing?.Writer.TryComplete();
        active?.Dispose();
        await _run;
        Task[] handlers = _handlers.Keys.ToArray();
        if (handlers.Length > 0)
        {
            try { await Task.WhenAll(handlers).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or ObjectDisposedException) { }
        }
        _stop.Dispose();
    }
}
