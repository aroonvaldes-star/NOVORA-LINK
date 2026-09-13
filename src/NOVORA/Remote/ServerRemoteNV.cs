using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace NOVORA.Remote;

public sealed class ServerRemoteNV :
    IAsyncDisposable
{
    private static readonly TimeSpan HandshakeTimeoutNV =
        TimeSpan.FromSeconds(5);

    private readonly Func<
        CommandRemoteNV,
        string,
        CancellationToken,
        Task<ResultRemoteNV>> _commandHandlerNV;

    private readonly ConcurrentDictionary<int, TcpClient>
        _clientsNV =
            new();

    private readonly SemaphoreSlim _lifecycleGateNV =
        new(1, 1);

    private CancellationTokenSource? _lifetimeCtsNV;
    private TcpListener? _listenerNV;
    private Task? _acceptTaskNV;
    private int _nextClientIdNV;
    private bool _disposedNV;
    private readonly int _portNV;
    private readonly ConcurrentDictionary<int, byte> _authenticatedClientsNV = new();
    internal string SessionTokenNV { get; private set; } = string.Empty;
    internal int BoundPortNV => ((IPEndPoint?)_listenerNV?.LocalEndpoint)?.Port ?? 0;

    public ServerRemoteNV(
        Func<
            CommandRemoteNV,
            string,
            CancellationToken,
            Task<ResultRemoteNV>> commandHandlerNV)
        : this(commandHandlerNV, ProtocolRemoteNV.DefaultDevicePortNV)
    {
    }

    internal ServerRemoteNV(
        Func<CommandRemoteNV, string, CancellationToken, Task<ResultRemoteNV>> commandHandlerNV,
        int portNV)
    {
        _portNV = portNV;
        _commandHandlerNV =
            commandHandlerNV ??
            throw new ArgumentNullException(
                nameof(commandHandlerNV));
    }

    public bool IsRunningNV =>
        _acceptTaskNV is
        {
            IsCompleted: false
        };

    public bool HasClientNV =>
        !_authenticatedClientsNV.IsEmpty;

    public async Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedNV();

        await _lifecycleGateNV
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (IsRunningNV)
            {
                return;
            }

            _lifetimeCtsNV?.Dispose();
            _lifetimeCtsNV =
                new CancellationTokenSource();

            _listenerNV =
                new TcpListener(
                    IPAddress.Loopback,
                    _portNV);

            _listenerNV.Start(
                backlog: 5);

            SessionTokenNV = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

            _acceptTaskNV =
                AcceptLoopNVAsync(
                    SessionTokenNV,
                    _lifetimeCtsNV.Token);
        }
        finally
        {
            _lifecycleGateNV.Release();
        }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGateNV
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            CancellationTokenSource? cts =
                _lifetimeCtsNV;

            Task? acceptTask =
                _acceptTaskNV;

            cts?.Cancel();
            SessionTokenNV = string.Empty;
            _authenticatedClientsNV.Clear();

            try
            {
                _listenerNV?.Stop();
            }
            catch
            {
            }

            foreach (TcpClient client in
                     _clientsNV.Values)
            {
                try
                {
                    client.Close();
                }
                catch
                {
                }
            }

            _clientsNV.Clear();

            if (acceptTask is not null)
            {
                try
                {
                    await acceptTask
                        .WaitAsync(
                            TimeSpan.FromSeconds(3),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                }
            }

            _listenerNV = null;
            _acceptTaskNV = null;

            _lifetimeCtsNV?.Dispose();
            _lifetimeCtsNV = null;
        }
        finally
        {
            _lifecycleGateNV.Release();
        }
    }

    private async Task AcceptLoopNVAsync(
        string sessionToken,
        CancellationToken cancellationToken)
    {
        TcpListener listener =
            _listenerNV ??
            throw new InvalidOperationException(
                "ServerRemoteNV no tiene listener.");

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client =
                    await listener
                        .AcceptTcpClientAsync(
                            cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            int clientId =
                Interlocked.Increment(
                    ref _nextClientIdNV);

            _clientsNV[clientId] =
                client;

            _ = HandleClientNVAsync(
                clientId,
                client,
                sessionToken,
                cancellationToken);
        }
    }

    private async Task HandleClientNVAsync(
        int clientId,
        TcpClient client,
        string sessionToken,
        CancellationToken cancellationToken)
    {
        client.NoDelay = true;

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
            NetworkStream stream =
                client.GetStream();

            using (
                var handshakeCts =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            cancellationToken))
            {
                handshakeCts.CancelAfter(
                    HandshakeTimeoutNV);

                string hello =
                    await ProtocolRemoteNV
                        .ReadFrameNVAsync(
                            stream,
                            handshakeCts.Token)
                        .ConfigureAwait(false);

                if (!ProtocolRemoteNV.IsHelloNV(
                        hello, sessionToken))
                {
                    throw new InvalidOperationException(
                        "HELLO remoto inválido.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                _authenticatedClientsNV[clientId] = 0;

                await ProtocolRemoteNV
                    .WriteFrameNVAsync(
                        stream,
                        ProtocolRemoteNV.CreateWelcomeNV(),
                        handshakeCts.Token)
                    .ConfigureAwait(false);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                string payload =
                    await ProtocolRemoteNV
                        .ReadFrameNVAsync(
                            stream,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (!ProtocolRemoteNV.TryParseCommandNV(
                        payload,
                        out string requestId,
                        out CommandRemoteNV command,
                        out string commandPayload,
                        out string error))
                {
                    string invalidId =
                        Guid.NewGuid()
                            .ToString("N");

                    await ProtocolRemoteNV
                        .WriteFrameNVAsync(
                            stream,
                            ProtocolRemoteNV.CreateResultNV(
                                invalidId,
                                ResultRemoteNV.FailNV(
                                    error)),
                            cancellationToken)
                        .ConfigureAwait(false);

                    continue;
                }

                ResultRemoteNV result;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    result =
                        await _commandHandlerNV(
                                command,
                                commandPayload,
                                cancellationToken)
                            .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result =
                        ResultRemoteNV.FailNV(
                            ex.Message);
                }

                await ProtocolRemoteNV
                    .WriteFrameNVAsync(
                        stream,
                        ProtocolRemoteNV.CreateResultNV(
                            requestId,
                            result),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
        }
        finally
        {
            _authenticatedClientsNV.TryRemove(clientId, out _);
            _clientsNV.TryRemove(
                clientId,
                out _);

            try
            {
                client.Close();
            }
            catch
            {
            }

            client.Dispose();
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

        await StopAsync()
            .ConfigureAwait(false);

        _lifecycleGateNV.Dispose();
    }
}
