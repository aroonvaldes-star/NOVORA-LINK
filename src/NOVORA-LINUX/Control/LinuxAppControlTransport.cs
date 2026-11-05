using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace NOVORA.Linux.Control;

internal sealed record AppControlStreamOffer(int Port, string Token);

internal sealed class LinuxAppControlTransport : IAsyncDisposable
{
    private readonly object _gate = new();
    private TcpListener? _listener;
    private TcpClient? _client;
    private AppControlStreamOffer? _offer;
    private bool _accepting;
    private int _disposed;

    public AppControlStreamOffer Prepare()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_gate)
        {
            StopCore();
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(8);
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _offer = new AppControlStreamOffer(
                port, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            return _offer;
        }
    }

    public async Task<Stream> AcceptAsync(
        AppControlStreamOffer offer,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        TcpListener listener;
        lock (_gate)
        {
            if (_accepting)
                throw new InvalidOperationException("Ya hay una aceptación AppControl en curso.");
            if (_listener is null || _offer != offer)
                throw new InvalidOperationException("La oferta AppControl ya no está activa.");
            listener = _listener;
            _accepting = true;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        byte[] expected = Convert.FromHexString(offer.Token);
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
                    attempt.CancelAfter(TimeSpan.FromSeconds(2));
                    byte[] received = new byte[32];
                    await client.GetStream().ReadExactlyAsync(received, attempt.Token).ConfigureAwait(false);
                    if (!CryptographicOperations.FixedTimeEquals(received, expected))
                        continue;

                    NetworkStream stream = client.GetStream();
                    lock (_gate)
                    {
                        if (_listener != listener || _offer != offer)
                            throw new InvalidOperationException("La oferta AppControl cambió.");
                        _client = client;
                        _listener = null;
                        _offer = null;
                        listener.Stop();
                        client = null;
                    }
                    return stream;
                }
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested)
                {
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                {
                    if (deadline.IsCancellationRequested)
                        throw;
                }
                finally
                {
                    client?.Dispose();
                }
            }
        }
        catch
        {
            ClearOffer(listener, offer);
            throw;
        }
        finally
        {
            lock (_gate)
                _accepting = false;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            lock (_gate)
                StopCore();
        }
        return ValueTask.CompletedTask;
    }

    private void ClearOffer(TcpListener listener, AppControlStreamOffer offer)
    {
        lock (_gate)
        {
            if (_listener != listener || _offer != offer)
                return;
            listener.Stop();
            _listener = null;
            _offer = null;
        }
    }

    private void StopCore()
    {
        _listener?.Stop();
        _client?.Dispose();
        _listener = null;
        _client = null;
        _offer = null;
    }
}
