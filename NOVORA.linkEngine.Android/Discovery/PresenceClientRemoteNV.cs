using System.Net.Sockets;
using NOVORA.LinkEngine.Android.Remote;

namespace NOVORA.LinkEngine.Android.Discovery;

public sealed class PresenceClientRemoteNV :
    IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeoutNV =
        TimeSpan.FromSeconds(5);

    private TcpClient? _clientNV;
    private NetworkStream? _streamNV;
    private bool _disposedNV;
    private string _sessionTokenNV = string.Empty;

    public bool IsConnectedNV =>
        _clientNV is
        {
            Connected: true
        } &&
        _streamNV is not null && _sessionTokenNV == SessionRemoteNV.TokenNV;

    public async Task<bool> ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedNV();

        if (IsConnectedNV)
        {
            return true;
        }

        await DisconnectAsync()
            .ConfigureAwait(false);

        string sessionToken = SessionRemoteNV.TokenNV;
        if (string.IsNullOrEmpty(sessionToken)) return false;

        var clientNV =
            new TcpClient
            {
                NoDelay =
                    true
            };

        try
        {
            clientNV.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.KeepAlive,
                true);
        }
        catch
        {
        }

        try
        {
            using var connectCtsNV =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            connectCtsNV.CancelAfter(
                ConnectTimeoutNV);

            await clientNV
                .ConnectAsync(
                    ClientRemoteNV.DefaultHostNV,
                    ProtocolRemoteNV.DefaultPortNV,
                    connectCtsNV.Token)
                .ConfigureAwait(false);

            NetworkStream streamNV =
                clientNV.GetStream();

            await ProtocolRemoteNV
                .WriteFrameNVAsync(
                    streamNV,
                    ProtocolRemoteNV.CreateHelloNV(sessionToken),
                    connectCtsNV.Token)
                .ConfigureAwait(false);

            string welcomeNV =
                await ProtocolRemoteNV
                    .ReadFrameNVAsync(
                        streamNV,
                        connectCtsNV.Token)
                    .ConfigureAwait(false);

            if (!ProtocolRemoteNV.IsWelcomeNV(
                    welcomeNV))
            {
                throw new InvalidOperationException(
                    "DiscoveryEngine recibió WELCOME inválido.");
            }

            if (sessionToken != SessionRemoteNV.TokenNV)
                throw new InvalidOperationException("La sesión cambió durante la conexión.");
            _sessionTokenNV = sessionToken;
            _clientNV =
                clientNV;

            _streamNV =
                streamNV;

            return true;
        }
        catch
        {
            try
            {
                clientNV.Close();
            }
            catch
            {
            }

            clientNV.Dispose();

            return false;
        }
    }

    public async Task WaitForDisconnectAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedNV();

        NetworkStream? streamNV =
            _streamNV;

        if (streamNV is null)
        {
            return;
        }

        byte[] bufferNV =
            new byte[1];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int readNV =
                    await streamNV
                        .ReadAsync(
                            bufferNV,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (readNV == 0)
                {
                    return;
                }

                /*
                 * Este socket es sólo de presencia. El servidor normal
                 * no envía datos espontáneamente después de WELCOME.
                 * Si alguna versión futura lo hace, seguimos esperando
                 * cierre sin convertir esto en polling.
                 */
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
        }
    }

    public Task DisconnectAsync()
    {
        _sessionTokenNV = string.Empty;
        try
        {
            _streamNV?.Dispose();
        }
        catch
        {
        }

        try
        {
            _clientNV?.Close();
        }
        catch
        {
        }

        try
        {
            _clientNV?.Dispose();
        }
        catch
        {
        }

        _streamNV =
            null;

        _clientNV =
            null;

        return Task.CompletedTask;
    }

    private void ThrowIfDisposedNV()
    {
        ObjectDisposedException.ThrowIf(
            _disposedNV,
            this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposedNV)
        {
            return;
        }

        _disposedNV =
            true;

        await DisconnectAsync()
            .ConfigureAwait(false);
    }
}
