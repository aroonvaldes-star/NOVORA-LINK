using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.LinkEngine.Android.LinkEngine;

public sealed class ClientTransportLE :
    IAsyncDisposable
{
    public const string DefaultHostLE =
        "127.0.0.1";

    public const int DefaultPortLE =
        27183;

    private static readonly TimeSpan ConnectTimeoutLE =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan HandshakeTimeoutLE =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ReconnectDelayLE =
        TimeSpan.FromSeconds(1);

    private readonly string _host;
    private readonly int _port;
    private readonly string _clientId;

    private readonly SemaphoreSlim _lifecycleGate =
        new(1, 1);

    private CancellationTokenSource? _lifetimeCts;
    private Task? _workerTask;
    private TcpClient? _client;

    private bool _disposed;

    private StatusTransportLE _status;

    public ClientTransportLE(
        string clientId,
        string host = DefaultHostLE,
        int port = DefaultPortLE)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            clientId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            host);

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port));
        }

        _clientId =
            clientId.Trim();

        _host =
            host.Trim();

        _port =
            port;

        _status =
            StatusTransportLE.CreateInitialLE(
                _host,
                _port,
                _clientId);
    }

    public event EventHandler<StatusTransportLE>?
        StatusChangedLE;

    public StatusTransportLE StatusLE =>
        _status;

    public bool IsRunningLE =>
        _workerTask is
        {
            IsCompleted: false
        };

    public async Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        await _lifecycleGate
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (_workerTask is
                {
                    IsCompleted: false
                })
            {
                return;
            }

            _lifetimeCts?.Dispose();

            _lifetimeCts =
                new CancellationTokenSource();

            UpdateStatusLE(
                StateTransportLE.Starting,
                "Iniciando ClientTransportLE.",
                socketConnected:
                    false,
                handshakeVerified:
                    false);

            _workerTask =
                RunAsync(
                    _lifetimeCts.Token);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            UpdateStatusLE(
                StateTransportLE.Stopping,
                "Deteniendo ClientTransportLE.",
                socketConnected:
                    false,
                handshakeVerified:
                    false);

            CancellationTokenSource? cts =
                _lifetimeCts;

            Task? worker =
                _workerTask;

            cts?.Cancel();

            CloseClientLE();

            if (worker is not null)
            {
                try
                {
                    await worker
                        .WaitAsync(
                            TimeSpan.FromSeconds(3),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Cierre solicitado.
                }
                catch (TimeoutException)
                {
                    // El socket ya fue cerrado.
                }
                catch
                {
                    // Cleanup best-effort.
                }
            }

            _workerTask =
                null;

            _lifetimeCts?.Dispose();

            _lifetimeCts =
                null;

            UpdateStatusLE(
                StateTransportLE.Stopped,
                "ClientTransportLE detenido.",
                socketConnected:
                    false,
                handshakeVerified:
                    false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task RunAsync(
        CancellationToken cancellationToken)
    {
        bool firstAttempt =
            true;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                UpdateStatusLE(
                    firstAttempt
                        ? StateTransportLE.Connecting
                        : StateTransportLE.Reconnecting,
                    $"Conectando con {_host}:{_port}...",
                    socketConnected:
                        false,
                    handshakeVerified:
                        false);

                firstAttempt =
                    false;

                await ConnectAndRunSessionLEAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                UpdateStatusLE(
                    StateTransportLE.Reconnecting,
                    $"Canal no disponible: {ex.Message}",
                    socketConnected:
                        false,
                    handshakeVerified:
                        false);
            }
            finally
            {
                CloseClientLE();
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(
                        ReconnectDelayLE,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ConnectAndRunSessionLEAsync(
        CancellationToken cancellationToken)
    {
        var client =
            new TcpClient
            {
                NoDelay =
                    true
            };

        client.Client.SetSocketOption(
            SocketOptionLevel.Socket,
            SocketOptionName.KeepAlive,
            true);

        _client =
            client;

        using (
            var connectCts =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken))
        {
            connectCts.CancelAfter(
                ConnectTimeoutLE);

            await client
                .ConnectAsync(
                    _host,
                    _port,
                    connectCts.Token)
                .ConfigureAwait(false);
        }

        if (!client.Connected)
        {
            throw new SocketException(
                (int)SocketError.NotConnected);
        }

        UpdateStatusLE(
            StateTransportLE.Handshaking,
            "TCP conectado. Enviando HELLO...",
            socketConnected:
                true,
            handshakeVerified:
                false);

        NetworkStream stream =
            client.GetStream();

        string hello =
            HandshakeProtocolLE.CreateHelloPayloadLE(
                _clientId);

        using (
            var handshakeCts =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken))
        {
            handshakeCts.CancelAfter(
                HandshakeTimeoutLE);

            await HandshakeProtocolLE
                .WriteFrameLEAsync(
                    stream,
                    hello,
                    handshakeCts.Token)
                .ConfigureAwait(false);

            string ack =
                await HandshakeProtocolLE
                    .ReadFrameLEAsync(
                        stream,
                        handshakeCts.Token)
                    .ConfigureAwait(false);

            if (!HandshakeProtocolLE.IsValidAckLE(
                    ack))
            {
                throw new InvalidOperationException(
                    $"ACK inválido recibido: '{ack}'.");
            }
        }

        UpdateStatusLE(
            StateTransportLE.Connected,
            "HELLO/ACK VERIFIED. Iniciando sesión persistente...",
            socketConnected:
                true,
            handshakeVerified:
                true);

        await RunPersistentSessionLEAsync(
                client,
                stream,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RunPersistentSessionLEAsync(
        TcpClient client,
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        long sequence =
            0;

        long heartbeatCount =
            0;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!client.Connected)
            {
                throw new SocketException(
                    (int)SocketError.NotConnected);
            }

            /*
             * Windows espera el primer HEARTBEAT
             * antes de 5 segundos.
             *
             * Enviamos cada 2 segundos.
             */

            await Task.Delay(
                    HeartbeatProtocolLE.IntervalLE,
                    cancellationToken)
                .ConfigureAwait(false);

            sequence++;

            DateTimeOffset sentAt =
                DateTimeOffset.UtcNow;

            long sentAtUnixMilliseconds =
                sentAt.ToUnixTimeMilliseconds();

            string heartbeat =
                HeartbeatProtocolLE.CreateHeartbeatPayloadLE(
                    sequence,
                    sentAtUnixMilliseconds);

            await HandshakeProtocolLE
                .WriteFrameLEAsync(
                    stream,
                    heartbeat,
                    cancellationToken)
                .ConfigureAwait(false);

            string ack;

            using (
                var ackCts =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            cancellationToken))
            {
                ackCts.CancelAfter(
                    HeartbeatProtocolLE.AckTimeoutLE);

                try
                {
                    ack =
                        await HandshakeProtocolLE
                            .ReadFrameLEAsync(
                                stream,
                                ackCts.Token)
                            .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"HEARTBEAT_ACK #{sequence} no llegó en " +
                        $"{HeartbeatProtocolLE.AckTimeoutLE.TotalSeconds:0.#} segundos.");
                }
            }

            if (!HeartbeatProtocolLE.TryParseAckLE(
                    ack,
                    sequence,
                    sentAtUnixMilliseconds,
                    out _,
                    out string error))
            {
                throw new InvalidOperationException(
                    error);
            }

            DateTimeOffset ackAt =
                DateTimeOffset.UtcNow;

            double rttMilliseconds =
                Math.Max(
                    0,
                    (
                        ackAt -
                        sentAt
                    ).TotalMilliseconds);

            heartbeatCount++;

            UpdateHeartbeatStatusLE(
                StateTransportLE.Connected,
                $"SESSION HEALTHY · HEARTBEAT #{sequence}",
                socketConnected:
                    true,
                handshakeVerified:
                    true,
                sessionHealthy:
                    true,
                heartbeatSequence:
                    sequence,
                heartbeatCount:
                    heartbeatCount,
                lastHeartbeatAtUtc:
                    sentAt,
                lastHeartbeatAckAtUtc:
                    ackAt,
                roundTripMilliseconds:
                    rttMilliseconds);
        }
    }

    private void UpdateHeartbeatStatusLE(
        StateTransportLE state,
        string message,
        bool socketConnected,
        bool handshakeVerified,
        bool sessionHealthy,
        long heartbeatSequence,
        long heartbeatCount,
        DateTimeOffset? lastHeartbeatAtUtc,
        DateTimeOffset? lastHeartbeatAckAtUtc,
        double? roundTripMilliseconds)
    {
        var status =
            new StatusTransportLE(
                State:
                    state,

                Message:
                    message,

                Host:
                    _host,

                Port:
                    _port,

                ClientId:
                    _clientId,

                SocketConnected:
                    socketConnected,

                HandshakeVerified:
                    handshakeVerified,

                UpdatedAtUtc:
                    DateTimeOffset.UtcNow,

                SessionHealthy:
                    sessionHealthy,

                HeartbeatSequence:
                    heartbeatSequence,

                HeartbeatCount:
                    heartbeatCount,

                LastHeartbeatAtUtc:
                    lastHeartbeatAtUtc,

                LastHeartbeatAckAtUtc:
                    lastHeartbeatAckAtUtc,

                RoundTripMilliseconds:
                    roundTripMilliseconds);

        PublishStatusLE(
            status);
    }

    private void UpdateStatusLE(
        StateTransportLE state,
        string message,
        bool socketConnected,
        bool handshakeVerified)
    {
        var status =
            new StatusTransportLE(
                State:
                    state,

                Message:
                    message,

                Host:
                    _host,

                Port:
                    _port,

                ClientId:
                    _clientId,

                SocketConnected:
                    socketConnected,

                HandshakeVerified:
                    handshakeVerified,

                UpdatedAtUtc:
                    DateTimeOffset.UtcNow,

                SessionHealthy:
                    false,

                HeartbeatSequence:
                    0,

                HeartbeatCount:
                    0,

                LastHeartbeatAtUtc:
                    null,

                LastHeartbeatAckAtUtc:
                    null,

                RoundTripMilliseconds:
                    null);

        PublishStatusLE(
            status);
    }

    private void PublishStatusLE(
        StatusTransportLE status)
    {
        _status =
            status;

        try
        {
            StatusChangedLE?.Invoke(
                this,
                status);
        }
        catch
        {
            // La UI nunca debe romper ClientTransportLE.
        }
    }

    private void CloseClientLE()
    {
        TcpClient? client =
            Interlocked.Exchange(
                ref _client,
                null);

        if (client is null)
        {
            return;
        }

        try
        {
            client.Client.Shutdown(
                SocketShutdown.Both);
        }
        catch
        {
            // Puede estar desconectado.
        }

        try
        {
            client.Dispose();
        }
        catch
        {
            // Cleanup best-effort.
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

        try
        {
            await StopAsync()
                .ConfigureAwait(false);
        }
        finally
        {
            _disposed =
                true;

            _lifecycleGate.Dispose();
        }
    }
}