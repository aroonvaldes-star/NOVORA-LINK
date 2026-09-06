using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.LinkEngine.Transport;

public sealed class ListenerTransportLE : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleGate =
        new(1, 1);

    private TcpListener? _listener;

    private bool _disposed;

    public int PortLE { get; private set; }

    public bool IsListeningLE { get; private set; }

    public async Task StartAsync(
        int port,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port));
        }

        await _lifecycleGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (IsListeningLE)
            {
                if (PortLE == port)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"ListenerTransportLE ya escucha en {PortLE}.");
            }

            var listener =
                new TcpListener(
                    IPAddress.Loopback,
                    port);

            try
            {
                listener.Start(8);

                _listener =
                    listener;

                PortLE =
                    port;

                IsListeningLE =
                    true;
            }
            catch
            {
                listener.Stop();

                _listener =
                    null;

                PortLE =
                    0;

                IsListeningLE =
                    false;

                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<TcpClient> AcceptTcpClientAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        TcpListener listener =
            _listener ??
            throw new InvalidOperationException(
                "ListenerTransportLE no está iniciado.");

        if (!IsListeningLE)
        {
            throw new InvalidOperationException(
                "ListenerTransportLE no está escuchando.");
        }

        TcpClient client =
            await listener
                .AcceptTcpClientAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        client.NoDelay =
            true;

        client.Client.SetSocketOption(
            SocketOptionLevel.Socket,
            SocketOptionName.KeepAlive,
            true);

        return client;
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        await _lifecycleGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            TcpListener? listener =
                _listener;

            _listener =
                null;

            PortLE =
                0;

            IsListeningLE =
                false;

            try
            {
                listener?.Stop();
            }
            catch
            {
                // Cleanup best-effort.
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void ThrowIfDisposedLE()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _lifecycleGate
            .WaitAsync()
            .ConfigureAwait(false);

        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed =
                true;

            TcpListener? listener =
                _listener;

            _listener =
                null;

            PortLE =
                0;

            IsListeningLE =
                false;

            try
            {
                listener?.Stop();
            }
            catch
            {
                // Cleanup best-effort.
            }
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }
    }
}