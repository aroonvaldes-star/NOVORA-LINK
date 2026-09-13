using System.Net.Sockets;

namespace NOVORA.LinkEngine.Android.Remote;

public sealed class ClientRemoteNV :
    IAsyncDisposable
{
    public const string DefaultHostNV =
        "127.0.0.1";

    private static readonly TimeSpan ConnectTimeoutNV =
        TimeSpan.FromSeconds(3);

    private static readonly TimeSpan CommandTimeoutNV =
        TimeSpan.FromSeconds(45);

    private readonly SemaphoreSlim _gateNV =
        new(1, 1);

    private TcpClient? _clientNV;
    private NetworkStream? _streamNV;
    private bool _disposedNV;
    private string _sessionTokenNV = string.Empty;

    public event EventHandler<bool>?
        ConnectionChangedNV;

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

        await _gateNV
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return await EnsureConnectedUnderGateNVAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gateNV.Release();
        }
    }

    public async Task<ResultRemoteNV> SendCommandAsync(
        CommandRemoteNV command,
        string? payload = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedNV();

        await _gateNV
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            bool connected =
                await EnsureConnectedUnderGateNVAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!connected ||
                _streamNV is null)
            {
                return ResultRemoteNV.FailNV(
                    "NOVORA PC no está disponible. Abre o minimiza NOVORA en Windows.");
            }

            string requestId =
                Guid.NewGuid()
                    .ToString("N");

            string request =
                ProtocolRemoteNV.CreateCommandNV(
                    requestId,
                    command,
                    payload);

            using var commandCts =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            commandCts.CancelAfter(
                CommandTimeoutNV);

            try
            {
                await ProtocolRemoteNV
                    .WriteFrameNVAsync(
                        _streamNV,
                        request,
                        commandCts.Token)
                    .ConfigureAwait(false);

                string response =
                    await ProtocolRemoteNV
                        .ReadFrameNVAsync(
                            _streamNV,
                            commandCts.Token)
                        .ConfigureAwait(false);

                if (!ProtocolRemoteNV.TryParseResultNV(
                        response,
                        requestId,
                        out ResultRemoteNV result,
                        out string error))
                {
                    CloseConnectionNV();

                    return ResultRemoteNV.FailNV(
                        error);
                }

                return result;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                CloseConnectionNV();

                return ResultRemoteNV.FailNV(
                    "NOVORA PC no respondió dentro del tiempo permitido.");
            }
            catch (Exception ex)
            {
                CloseConnectionNV();

                return ResultRemoteNV.FailNV(
                    $"Canal remoto perdido: {ex.Message}");
            }
        }
        finally
        {
            _gateNV.Release();
        }
    }

    public async Task DisconnectAsync(
        CancellationToken cancellationToken = default)
    {
        await _gateNV
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            CloseConnectionNV();
        }
        finally
        {
            _gateNV.Release();
        }
    }

    private async Task<bool> EnsureConnectedUnderGateNVAsync(
        CancellationToken cancellationToken)
    {
        if (IsConnectedNV)
        {
            return true;
        }

        CloseConnectionNV(
            notify: false);

        string sessionToken = SessionRemoteNV.TokenNV;
        if (string.IsNullOrEmpty(sessionToken)) return false;

        var client =
            new TcpClient
            {
                NoDelay = true
            };

        try
        {
            client.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.KeepAlive,
                true);
        }
        catch
        {
        }

        try
        {
            using var connectCts =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            connectCts.CancelAfter(
                ConnectTimeoutNV);

            await client
                .ConnectAsync(
                    DefaultHostNV,
                    ProtocolRemoteNV.DefaultPortNV,
                    connectCts.Token)
                .ConfigureAwait(false);

            NetworkStream stream =
                client.GetStream();

            await ProtocolRemoteNV
                .WriteFrameNVAsync(
                    stream,
                    ProtocolRemoteNV.CreateHelloNV(sessionToken),
                    connectCts.Token)
                .ConfigureAwait(false);

            string welcome =
                await ProtocolRemoteNV
                    .ReadFrameNVAsync(
                        stream,
                        connectCts.Token)
                    .ConfigureAwait(false);

            if (!ProtocolRemoteNV.IsWelcomeNV(
                    welcome))
            {
                throw new InvalidOperationException(
                    "NOVORA PC respondió con WELCOME inválido.");
            }

            if (sessionToken != SessionRemoteNV.TokenNV)
                throw new InvalidOperationException("La sesión cambió durante la conexión.");
            _sessionTokenNV = sessionToken;
            _clientNV =
                client;

            _streamNV =
                stream;

            ConnectionChangedNV?.Invoke(
                this,
                true);

            return true;
        }
        catch
        {
            try
            {
                client.Close();
            }
            catch
            {
            }

            client.Dispose();

            ConnectionChangedNV?.Invoke(
                this,
                false);

            return false;
        }
    }

    private void CloseConnectionNV(
        bool notify = true)
    {
        bool wasConnected =
            IsConnectedNV;

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

        _streamNV = null;
        _clientNV = null;
        _sessionTokenNV = string.Empty;

        if (notify &&
            wasConnected)
        {
            ConnectionChangedNV?.Invoke(
                this,
                false);
        }
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

        _disposedNV = true;

        await DisconnectAsync()
            .ConfigureAwait(false);

        _gateNV.Dispose();
    }
}
