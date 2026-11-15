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
    // Device-side ports published through adb reverse. The PC listeners use
    // ephemeral host ports and are mapped to these stable Android endpoints.
    public const int DevicePortVE = 27215;
    public const int ControlPortVE = 27216;
    public const int AudioPortVE = 27217;
    // Bound the reject loop's CPU burst; the offer itself remains alive until
    // the accept deadline or an explicit cancellation.
    private const int MaxRejectedBeforeBackoff = 32;
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RejectedAttemptDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan RejectedBurstDelay = TimeSpan.FromSeconds(1);

    private readonly IPAddress _address;
    private readonly int _requestedPort;
    private readonly object _gate = new();
    private TcpListener? _listener;
    private TcpClient? _client;
    private VETransportAppControlOffer? _offer;
    private bool _accepting;

    public VETransportAppControl()
        : this(IPAddress.Loopback, 0)
    {
    }

    public static VETransportAppControl CreateControlVE() =>
        new(IPAddress.Loopback, 0);

    public static VETransportAppControl CreateAudioVE() =>
        new(IPAddress.Loopback, 0);

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
            _listener.Start(16);
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
        byte[] expected = Convert.FromHexString(offer.Token);

        TcpListener listener;
        lock (_gate)
        {
            if (_accepting) throw new InvalidOperationException("Ya hay una aceptación AppControl en curso.");
            if (_listener is null || _offer != offer)
                throw new InvalidOperationException("La oferta AppControl ya no está activa.");
            listener = _listener;
            _accepting = true;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        int rejectedAttempts = 0;
        try
        {
            while (true)
            {
                TcpClient? client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync(deadline.Token).ConfigureAwait(false);
                    client.NoDelay = true;
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                    attempt.CancelAfter(AttemptTimeout);
                    byte[] received = new byte[32];
                    await client.GetStream().ReadExactlyAsync(received, attempt.Token).ConfigureAwait(false);
                    if (!CryptographicOperations.FixedTimeEquals(received, expected))
                    {
                        client.Dispose();
                        client = null;
                    }
                    else
                    {
                        NetworkStream stream = client.GetStream();
                        lock (_gate)
                        {
                            if (_listener != listener || _offer != offer)
                                throw new InvalidOperationException("La oferta AppControl cambió.");
                            _client = client;
                            _listener.Stop();
                            _listener = null;
                            _offer = null;
                            client = null;
                        }
                        return stream;
                    }
                }
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested)
                {
                    // This attempt stalled or the client disconnected. The offer
                    // remains active for the next, correctly authenticated client.
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                {
                    if (deadline.IsCancellationRequested) throw;
                }
                finally
                {
                    client?.Dispose();
                }

                rejectedAttempts++;
                TimeSpan delay = rejectedAttempts >= MaxRejectedBeforeBackoff
                    ? RejectedBurstDelay
                    : RejectedAttemptDelay;
                if (rejectedAttempts >= MaxRejectedBeforeBackoff) rejectedAttempts = 0;
                await Task.Delay(delay, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            ClearOffer(listener, offer);
            throw;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
            ClearOffer(listener, offer);
            throw;
        }
        finally
        {
            lock (_gate) _accepting = false;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) StopCoreVE();
        return ValueTask.CompletedTask;
    }

    private void ClearOffer(TcpListener listener, VETransportAppControlOffer offer)
    {
        lock (_gate)
        {
            if (_listener != listener || _offer != offer) return;
            try { _listener.Stop(); } catch { }
            _listener = null;
            _offer = null;
        }
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
