using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using NOVORA.Linux.Control;

namespace NOVORA.Linux.VisionEngine;

internal sealed class LinuxVisionLanChannel : IAsyncDisposable
{
    private readonly IPAddress _address;
    private readonly X509Certificate2 _certificate;
    private readonly LinuxVisionLanChannelKind _kind;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;
    private TcpClient? _client;
    private SslStream? _stream;
    private LinuxVisionLanEndpoint? _endpoint;
    private string? _sessionId;
    private DateTimeOffset _expiresAt;
    private bool _accepting;
    private bool _disposed;

    public LinuxVisionLanChannel(
        IPAddress address,
        X509Certificate2 certificate,
        LinuxVisionLanChannelKind kind,
        bool allowLoopback = false)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        if (allowLoopback ? !IPAddress.IsLoopback(address) : !LinuxLanDiscovery.IsPrivateIpv4(address))
            throw new InvalidDataException("VisionEngine LAN requiere una dirección IPv4 privada.");
        _certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
        _kind = kind;
    }

    public LinuxVisionLanEndpoint Prepare(string sessionId, DateTimeOffset expiresAt)
    {
        if (sessionId is not { Length: >= 8 and <= 128 })
            throw new InvalidDataException("Sesión VE LAN no válida.");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (expiresAt <= now || expiresAt > now.AddSeconds(LinuxVisionLanProtocol.OfferLifetimeSeconds))
            throw new InvalidDataException("Caducidad VE LAN no válida.");

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopCore();
            _listener = new TcpListener(_address, 0);
            _listener.Start(4);
            _sessionId = sessionId;
            _expiresAt = expiresAt;
            _endpoint = new LinuxVisionLanEndpoint(
                ((IPEndPoint)_listener.LocalEndpoint).Port,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                _kind.ToString().ToUpperInvariant());
            return _endpoint;
        }
    }

    public async Task<Stream> AcceptAsync(
        string sessionId,
        LinuxVisionLanEndpoint endpoint,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        TcpListener listener;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_accepting || _listener is null || _endpoint != endpoint ||
                _sessionId != sessionId || _expiresAt != expiresAt ||
                expiresAt <= DateTimeOffset.UtcNow)
                throw new InvalidDataException("La oferta VE LAN ya no está activa.");
            listener = _listener;
            _accepting = true;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        deadline.CancelAfter(expiresAt - DateTimeOffset.UtcNow);
        try
        {
            while (true)
            {
                TcpClient? candidate = null;
                SslStream? tls = null;
                try
                {
                    candidate = await listener.AcceptTcpClientAsync(deadline.Token).ConfigureAwait(false);
                    candidate.NoDelay = true;
                    tls = new SslStream(candidate.GetStream(), leaveInnerStreamOpen: false);
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                    attempt.CancelAfter(TimeSpan.FromSeconds(4));
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _certificate,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                    }, attempt.Token).ConfigureAwait(false);
                    LinuxVisionLanHello hello =
                        await ControlProtocol.ReadAsync<LinuxVisionLanHello>(tls, attempt.Token).ConfigureAwait(false);
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
                        return _stream ?? throw new InvalidOperationException("El canal TLS no quedó establecido.");
                    }
                }
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested)
                {
                    tls?.Dispose();
                    candidate?.Dispose();
                }
                catch (Exception ex) when (ex is AuthenticationException or InvalidDataException or
                                           IOException or SocketException or JsonException)
                {
                    tls?.Dispose();
                    candidate?.Dispose();
                    if (deadline.IsCancellationRequested)
                        throw new OperationCanceledException("La oferta VE LAN venció.", ex, deadline.Token);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            ClearOffer(listener, endpoint);
            throw;
        }
        finally
        {
            lock (_gate) _accepting = false;
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

    private void ClearOffer(TcpListener listener, LinuxVisionLanEndpoint endpoint)
    {
        lock (_gate)
        {
            if (_listener != listener || _endpoint != endpoint) return;
            _listener.Stop();
            _listener = null;
            _endpoint = null;
            _sessionId = null;
        }
    }

    private void StopCore()
    {
        _listener?.Stop();
        _stream?.Dispose();
        _client?.Dispose();
        _listener = null;
        _stream = null;
        _client = null;
        _endpoint = null;
        _sessionId = null;
    }
}
