using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.IO;

namespace NOVORA.VisionEngine.Transport;

public sealed record VETransportAppControlOffer(int Port, string Token);

/// <summary>
/// Acepta una única fuente AppControl autenticada por un token efímero.
/// </summary>
public sealed class VETransportAppControl : IAsyncDisposable
{
    public const int DevicePortVE = 27215;
    public const int ControlPortVE = 27216;
    public const int AudioPortVE = 27217;

    private readonly IPAddress _address;
    private readonly int _requestedPort;
    private readonly object _gate = new();
    private TcpListener? _listener;
    private TcpClient? _client;
    private VETransportAppControlOffer? _offer;

    public VETransportAppControl()
        : this(IPAddress.Loopback, DevicePortVE)
    {
    }

    public static VETransportAppControl CreateControlVE() =>
        new(IPAddress.Loopback, ControlPortVE);

    public static VETransportAppControl CreateAudioVE() =>
        new(IPAddress.Loopback, AudioPortVE);

    internal VETransportAppControl(IPAddress address, int port)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _requestedPort = port;
    }

    public bool IsConnectedVE
    {
        get { lock (_gate) return _client?.Connected == true; }
    }

    public VETransportAppControlOffer PrepareVE()
    {
        lock (_gate)
        {
            StopCoreVE();
            _listener = new TcpListener(_address, _requestedPort);
            _listener.Start(1);
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _offer = new VETransportAppControlOffer(
                port,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            return _offer;
        }
    }

    public async Task<Stream> AcceptAsync(
        VETransportAppControlOffer offer,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        TcpListener listener;
        lock (_gate)
        {
            if (_listener is null || _offer != offer)
                throw new InvalidOperationException("La oferta AppControl ya no está activa.");
            listener = _listener;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        TcpClient client = await listener.AcceptTcpClientAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            client.NoDelay = true;
            byte[] received = new byte[32];
            await client.GetStream().ReadExactlyAsync(received, deadline.Token).ConfigureAwait(false);
            byte[] expected = Convert.FromHexString(offer.Token);
            if (!CryptographicOperations.FixedTimeEquals(received, expected))
                throw new UnauthorizedAccessException("Token de video AppControl inválido.");

            lock (_gate)
            {
                if (_listener != listener || _offer != offer)
                    throw new OperationCanceledException("La oferta AppControl cambió.");
                _client = client;
                _listener.Stop();
                _listener = null;
                _offer = null;
            }
            return client.GetStream();
        }
        catch
        {
            client.Dispose();
            lock (_gate)
            {
                if (_listener == listener)
                {
                    _listener.Stop();
                    _listener = null;
                    _offer = null;
                }
            }
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) StopCoreVE();
        return ValueTask.CompletedTask;
    }

    private void StopCoreVE()
    {
        try { _listener?.Stop(); } catch { }
        try { _client?.Dispose(); } catch { }
        _listener = null;
        _client = null;
        _offer = null;
    }
}
