using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;

namespace NOVORA.Control;

/// <summary>Authenticated TLS bridge from one trusted LAN client to RelayCore's loopback DATA port.</summary>
public sealed class NLControlLanDataGateway : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly NLControlTrustStore _store;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly int _targetPort;
    private Task _run = Task.CompletedTask;
    private TcpClient? _active;
    private bool _started;

    public NLControlLanDataGateway(IPAddress address, NLControlTrustStore store, int targetPort = 27184,
        bool allowLoopback = false)
    {
        NLControlLanInvitation.ValidateAddress(address, allowLoopback);
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _targetPort = targetPort is > 0 and <= 65535 ? targetPort : throw new ArgumentOutOfRangeException(nameof(targetPort));
        _listener = new TcpListener(address, 0);
    }

    public bool IsConnected { get; private set; }
    public event EventHandler? StateChanged;

    public NLControlLinkOffer Start()
    {
        if (_started) throw new InvalidOperationException("El gateway DATA LAN ya está iniciado.");
        _listener.Start(1);
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
                using TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                client.NoDelay = true;
                _active = client;
                try { await ServeAsync(client); }
                catch (Exception ex) when (ex is IOException or AuthenticationException or InvalidDataException or
                    OperationCanceledException or SocketException or ObjectDisposedException) { }
                finally
                {
                    _active = null;
                    if (IsConnected) { IsConnected = false; StateChanged?.Invoke(this, EventArgs.Empty); }
                }
            }
        }
        catch (Exception ex) when (_stop.IsCancellationRequested && ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var tls = new SslStream(client.GetStream(), false);
        using var authorization = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        authorization.CancelAfter(TimeSpan.FromSeconds(8));
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = _store.Certificate,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, authorization.Token);
        NLControlLinkHello hello = await NLControlProtocol.ReadAsync<NLControlLinkHello>(tls, authorization.Token);
        if (hello.Version != NLControlProtocol.Version || hello.Channel != "DATA" || hello.Token is not { Length: 64 } ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hello.Token), Convert.FromHexString(_token)))
            throw new AuthenticationException("Autorización DATA LAN rechazada.");

        using var relay = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        await relay.ConnectAsync(IPAddress.Loopback, _targetPort, authorization.Token);
        IsConnected = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        using NetworkStream local = relay.GetStream();
        Task toRelay = tls.CopyToAsync(local, _stop.Token);
        Task toAndroid = local.CopyToAsync(tls, _stop.Token);
        await await Task.WhenAny(toRelay, toAndroid);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_started) return;
        _stop.Cancel();
        _listener.Stop();
        _active?.Dispose();
        try { await _run; } catch (OperationCanceledException) { }
        _stop.Dispose();
    }
}
