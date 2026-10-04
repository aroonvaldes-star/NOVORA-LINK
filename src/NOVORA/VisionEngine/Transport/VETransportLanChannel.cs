using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using NOVORA.Control;

namespace NOVORA.VisionEngine.Transport;

/// <summary>Accepts one authenticated TLS stream for a native VE LAN channel.</summary>
public sealed class VETransportLanChannel : IAsyncDisposable
{
    private readonly IPAddress _address;
    private readonly NLControlTrustStore _trustStore;
    private readonly NLControlVeLanChannel _channel;
    private readonly bool _allowLoopback;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;
    private TcpClient? _client;
    private SslStream? _stream;
    private string? _sessionId;
    private DateTimeOffset _expiresAt;
    private NLControlVeLanEndpoint? _endpoint;
    private bool _disposed;

    internal VETransportLanChannel(
        IPAddress address,
        NLControlTrustStore trustStore,
        NLControlVeLanChannel channel,
        bool allowLoopback = false)
    {
        ArgumentNullException.ThrowIfNull(address);
        NLControlLanInvitation.ValidateAddress(address, allowLoopback);
        _address = address;
        _trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
        _channel = channel;
        _allowLoopback = allowLoopback;
    }

    public NLControlVeLanEndpoint PrepareVE(string sessionId, DateTimeOffset expiresAt)
    {
        if (sessionId is not { Length: >= 8 and <= 128 })
            throw new InvalidDataException("Sesión VE LAN no válida.");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (expiresAt <= now || expiresAt > now.AddSeconds(30))
            throw new InvalidDataException("Caducidad VE LAN no válida.");

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopCore();
            _listener = new TcpListener(_address, 0);
            _listener.Start(4);
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _sessionId = sessionId;
            _expiresAt = expiresAt;
            _endpoint = new NLControlVeLanEndpoint(port,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                _channel.ToString().ToUpperInvariant());
            return _endpoint;
        }
    }

    public async Task<Stream> AcceptAsync(
        string sessionId,
        NLControlVeLanEndpoint endpoint,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        TcpListener listener;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_listener is null || _endpoint != endpoint || _sessionId != sessionId ||
                _expiresAt != expiresAt || expiresAt <= DateTimeOffset.UtcNow)
                throw new InvalidDataException("La oferta VE LAN ya no está activa.");
            listener = _listener;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        deadline.CancelAfter(expiresAt - DateTimeOffset.UtcNow);
        while (true)
        {
            TcpClient? candidate = null;
            SslStream? tls = null;
            try
            {
                candidate = await listener.AcceptTcpClientAsync(deadline.Token).ConfigureAwait(false);
                candidate.NoDelay = true;
                tls = new SslStream(candidate.GetStream(), leaveInnerStreamOpen: false);
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _trustStore.Certificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, deadline.Token).ConfigureAwait(false);
                NLControlVeLanHello hello = await NLControlProtocol
                    .ReadAsync<NLControlVeLanHello>(tls, deadline.Token).ConfigureAwait(false);
                hello.Validate(sessionId, endpoint);

                lock (_gate)
                {
                    if (_listener != listener || _endpoint != endpoint || _disposed)
                        throw new OperationCanceledException("La oferta VE LAN cambió.");
                    _client = candidate;
                    _stream = tls;
                    _listener.Stop();
                    _listener = null;
                    _endpoint = null;
                    _sessionId = null;
                    candidate = null;
                    tls = null;
                    return _stream;
                }
            }
            catch (Exception ex) when (ex is AuthenticationException or InvalidDataException or IOException)
            {
                tls?.Dispose();
                candidate?.Dispose();
            }
            catch (Exception ex) when (_stop.IsCancellationRequested &&
                ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                tls?.Dispose();
                candidate?.Dispose();
                throw new OperationCanceledException("Canal VE LAN detenido.", ex);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            _stop.Cancel();
            StopCore();
        }
        _stop.Dispose();
        return ValueTask.CompletedTask;
    }

    private void StopCore()
    {
        try { _listener?.Stop(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        _listener = null;
        _stream = null;
        _client = null;
        _endpoint = null;
        _sessionId = null;
    }
}
