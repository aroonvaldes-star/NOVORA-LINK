using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Metrics;
using NOVORA.LinkEngine.Protocol;
using NOVORA.Services;

namespace NOVORA.LinkEngine.Transport;

public sealed class ManagerTransportLE : IAsyncDisposable
{
    public event EventHandler<SessionTransportLE>? SessionChangedLE;

    private static readonly TimeSpan HandshakeTimeoutLE =
        TimeSpan.FromSeconds(8);

    private readonly ConcurrentDictionary<string, SessionTransportLE> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ListenerTransportLE> _listeners =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _acceptLoopCts =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Task> _acceptLoopTasks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, TcpClient> _clients =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<SessionTransportLE>>
        _handshakeWaiters =
            new(StringComparer.OrdinalIgnoreCase);

    private readonly CollectorMetricsLE _metrics;
    private readonly PortTransportLE _portAllocator;

    private AdbService? _adbService;

    private bool _initialized;
    private bool _disposed;

    public ManagerTransportLE(
        CollectorMetricsLE metrics)
    {
        _metrics =
            metrics ??
            throw new ArgumentNullException(nameof(metrics));

        _portAllocator =
            new PortTransportLE();
    }

    public ManagerTransportLE(
        CollectorMetricsLE metrics,
        PortTransportLE portAllocator)
    {
        _metrics =
            metrics ??
            throw new ArgumentNullException(nameof(metrics));

        _portAllocator =
            portAllocator ??
            throw new ArgumentNullException(nameof(portAllocator));
    }

    public void ConfigureAdbLE(
        AdbService adbService)
    {
        ThrowIfDisposedLE();

        _adbService =
            adbService ??
            throw new ArgumentNullException(nameof(adbService));
    }

    public Task<ResultCoreLE> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        cancellationToken.ThrowIfCancellationRequested();

        _initialized = true;

        return Task.FromResult(
            ResultCoreLE.Ok(
                "ManagerTransportLE inicializado."));
    }

    public async Task<ResultCoreLE> OpenAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        if (!_initialized)
        {
            return ResultCoreLE.Fail(
                "ManagerTransportLE no está inicializado.");
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            return ResultCoreLE.Fail(
                "El serial del dispositivo es obligatorio.");
        }

        serial =
            serial.Trim();

        SemaphoreSlim sessionLock =
            GetSessionLockLE(
                serial);

        await sessionLock
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            /*
             * Si ya existe una sesión completamente reutilizable,
             * NO creamos otro listener.
             *
             * Esto mantiene START idempotente.
             */
            if (_sessions.TryGetValue(
                    serial,
                    out SessionTransportLE? currentSession))
            {
                bool reusable =
                    currentSession.State is
                        StateTransportLE.Ready or
                        StateTransportLE.Connected or
                        StateTransportLE.Degraded &&
                    currentSession.ReverseVerified &&
                    currentSession.ListenerStarted &&
                    IsListenerActiveLE(
                        serial);

                if (reusable)
                {
                    EnsureAcceptLoopLE(
                        serial);

                    return ResultCoreLE.Ok(
                        $"ManagerTransportLE reutiliza el canal de {serial}. " +
                        $"DevicePort={currentSession.DevicePort}; " +
                        $"HostPort={currentSession.HostPort}; " +
                        $"Estado={currentSession.State}.");
                }

                /*
                 * Una sesión vieja/incompleta debe destruirse antes
                 * de crear la nueva.
                 */
                await CloseInternalLEAsync(
                        serial,
                        currentSession,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            /*
             * CONTROL Android SIEMPRE utiliza tcp:27183.
             *
             * adb reverse permite mapearlo hacia un puerto HOST
             * diferente.
             *
             * Esto es esencial para:
             *
             * - varios dispositivos;
             * - evitar "AddressAlreadyInUse";
             * - no chocar con un listener diagnóstico viejo.
             */
            const int devicePort =
                PortTransportLE.DefaultStartPortLE;

            int hostPort =
                _portAllocator.Reserve(
                    serial);

            DateTimeOffset now =
                DateTimeOffset.UtcNow;

            var session =
                new SessionTransportLE(
                    Serial:
                        serial,

                    DevicePort:
                        devicePort,

                    HostPort:
                        hostPort,

                    State:
                        StateTransportLE.Preparing,

                    ReverseConfigured:
                        false,

                    ReverseVerified:
                        false,

                    ListenerStarted:
                        false,

                    ClientConnected:
                        false,

                    HandshakeVerified:
                        false,

                    ClientId:
                        null,

                    CreatedAtUtc:
                        now,

                    UpdatedAtUtc:
                        now,

                    LastVerifiedAtUtc:
                        null,

                    ConnectedAtUtc:
                        null,

                    LastError:
                        null);

            _sessions[serial] =
                session;


            // ========================================================
            // WINDOWS LISTENER
            //
            // OJO:
            //
            // Escuchamos HOST PORT, NO DevicePort.
            // ========================================================

            ResultCoreLE listenerResult =
                await StartListenerLEAsync(
                        serial,
                        hostPort,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!listenerResult.Success)
            {
                await FailAndCleanupLEAsync(
                        serial,
                        session,
                        listenerResult.Message)
                    .ConfigureAwait(false);

                return listenerResult;
            }

            session =
                session with
                {
                    State =
                        StateTransportLE.Listening,

                    ListenerStarted =
                        true,

                    UpdatedAtUtc =
                        DateTimeOffset.UtcNow,

                    LastError =
                        null
                };

            _sessions[serial] =
                session;


            // ========================================================
            // ADB
            // ========================================================

            if (_adbService is null)
            {
                await FailAndCleanupLEAsync(
                        serial,
                        session,
                        "ManagerTransportLE inició ListenerTransportLE, pero ADB no está enlazado.")
                    .ConfigureAwait(false);

                return ResultCoreLE.Fail(
                    "ManagerTransportLE inició ListenerTransportLE, pero ADB no está enlazado.");
            }

            await _adbService
                .StartServerAsync(
                    cancellationToken)
                .ConfigureAwait(false);


            // ========================================================
            // ADB REVERSE
            //
            // Android:
            //
            //     127.0.0.1:27183
            //
            // Windows:
            //
            //     127.0.0.1:<hostPort>
            //
            // Ejemplo:
            //
            //     tcp:27183 -> tcp:27185
            // ========================================================

            ResultCoreLE reverseResult =
                await ConfigureReverseLEAsync(
                        serial,
                        devicePort,
                        hostPort,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!reverseResult.Success)
            {
                await FailAndCleanupLEAsync(
                        serial,
                        session,
                        reverseResult.Message)
                    .ConfigureAwait(false);

                return reverseResult;
            }

            session =
                session with
                {
                    State =
                        StateTransportLE.ReverseConfigured,

                    ReverseConfigured =
                        true,

                    ListenerStarted =
                        true,

                    UpdatedAtUtc =
                        DateTimeOffset.UtcNow,

                    LastError =
                        null
                };

            _sessions[serial] =
                session;


            // ========================================================
            // VERIFY REVERSE
            // ========================================================

            ResultCoreLE verification =
                await VerifyAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!verification.Success)
            {
                SessionTransportLE failedSession =
                    GetSessionLE(
                        serial) ??
                    session;

                await FailAndCleanupLEAsync(
                        serial,
                        failedSession,
                        verification.Message)
                    .ConfigureAwait(false);

                return verification;
            }

            SessionTransportLE? verifiedSession =
                GetSessionLE(
                    serial);

            if (verifiedSession is null)
            {
                await StopListenerLEAsync(
                        serial,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                _portAllocator.Release(
                    hostPort);

                return ResultCoreLE.Fail(
                    "ManagerTransportLE perdió la sesión después de verificar adb reverse.");
            }


            // ========================================================
            // LISTENER STILL ALIVE
            // ========================================================

            if (!IsListenerActiveLE(
                    serial))
            {
                await FailAndCleanupLEAsync(
                        serial,
                        verifiedSession,
                        $"ListenerTransportLE dejó de escuchar en el HostPort {hostPort}.")
                    .ConfigureAwait(false);

                return ResultCoreLE.Fail(
                    $"ListenerTransportLE dejó de escuchar en el HostPort {hostPort}.");
            }


            // ========================================================
            // READY
            // ========================================================

            verifiedSession =
                verifiedSession with
                {
                    State =
                        StateTransportLE.Ready,

                    ReverseConfigured =
                        true,

                    ReverseVerified =
                        true,

                    ListenerStarted =
                        true,

                    ClientConnected =
                        false,

                    HandshakeVerified =
                        false,

                    SessionHealthy =
                        false,

                    UpdatedAtUtc =
                        DateTimeOffset.UtcNow,

                    LastVerifiedAtUtc =
                        DateTimeOffset.UtcNow,

                    LastError =
                        null
                };

            _sessions[serial] =
                verifiedSession;

            ResetHandshakeWaiterLE(
                serial);

            EnsureAcceptLoopLE(
                serial);

            return ResultCoreLE.Ok(
                $"ManagerTransportLE READY. " +
                $"Android tcp:{devicePort} -> Windows tcp:{hostPort}; " +
                $"ListenerTransportLE LISTENING en 127.0.0.1:{hostPort}; " +
                $"adb reverse verificado; esperando cliente Android.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SessionTransportLE? cleanupSession =
                GetSessionLE(
                    serial);

            if (cleanupSession is not null)
            {
                await FailAndCleanupLEAsync(
                        serial,
                        cleanupSession,
                        ex.Message)
                    .ConfigureAwait(false);
            }

            return ResultCoreLE.Fail(
                $"ManagerTransportLE no pudo abrirse: {ex.Message}");
        }
        finally
        {
            sessionLock.Release();
        }
    }
    public async Task<ResultCoreLE> VerifyAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        if (string.IsNullOrWhiteSpace(serial))
        {
            return ResultCoreLE.Fail(
                "El serial del dispositivo es obligatorio.");
        }

        serial = serial.Trim();

        if (_adbService is null)
        {
            return ResultCoreLE.Fail(
                "ManagerTransportLE no tiene un AdbService configurado.");
        }

        if (!_sessions.TryGetValue(
                serial,
                out SessionTransportLE? session))
        {
            return ResultCoreLE.Fail(
                $"No existe una sesión ManagerTransportLE para {serial}.");
        }

        try
        {
            string output =
                await _adbService
                    .ExecuteRawAsync(
                        new[]
                        {
                            "-s",
                            serial,
                            "reverse",
                            "--list"
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

            bool verified =
                ContainsReverseMappingLE(
                    output,
                    session.DevicePort,
                    session.HostPort);

            DateTimeOffset now =
                DateTimeOffset.UtcNow;

            if (!verified)
            {
                SessionTransportLE failed =
                    session with
                    {
                        State = StateTransportLE.Failed,
                        ReverseVerified = false,
                        UpdatedAtUtc = now,
                        LastVerifiedAtUtc = now,
                        LastError =
                            $"No apareció adb reverse tcp:{session.DevicePort} -> tcp:{session.HostPort}."
                    };

                _sessions[serial] =
                    failed;

                return ResultCoreLE.Fail(
                    failed.LastError!);
            }

            bool listenerActive =
                IsListenerActiveLE(serial);

            StateTransportLE targetState;

            if (!listenerActive)
            {
                targetState =
                    StateTransportLE.Degraded;
            }
            else if (session.ClientConnected &&
                     session.HandshakeVerified)
            {
                targetState =
                    StateTransportLE.Connected;
            }
            else
            {
                targetState =
                    StateTransportLE.Ready;
            }

            SessionTransportLE verifiedSession =
                session with
                {
                    State = targetState,
                    ReverseConfigured = true,
                    ReverseVerified = true,
                    ListenerStarted = listenerActive,
                    UpdatedAtUtc = now,
                    LastVerifiedAtUtc = now,
                    LastError =
                        listenerActive
                            ? null
                            : "adb reverse existe, pero ListenerTransportLE no está escuchando."
                };

            _sessions[serial] =
                verifiedSession;

            if (!listenerActive)
            {
                return ResultCoreLE.Fail(
                    verifiedSession.LastError!);
            }

            return ResultCoreLE.Ok(
                $"adb reverse tcp:{session.DevicePort} -> tcp:{session.HostPort} verificado.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SessionTransportLE failed =
                session with
                {
                    State = StateTransportLE.Failed,
                    ReverseVerified = false,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    LastVerifiedAtUtc = DateTimeOffset.UtcNow,
                    LastError =
                        $"No se pudo verificar adb reverse: {ex.Message}"
                };

            _sessions[serial] =
                failed;

            return ResultCoreLE.Fail(
                failed.LastError!);
        }
    }

    public async Task<ResultCoreLE> WaitForHandshakeAsync(
        string serial,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        if (string.IsNullOrWhiteSpace(serial))
        {
            return ResultCoreLE.Fail(
                "El serial del dispositivo es obligatorio.");
        }

        if (timeout <= TimeSpan.Zero)
        {
            return ResultCoreLE.Fail(
                "El timeout debe ser mayor que cero.");
        }

        serial = serial.Trim();

        SessionTransportLE? current =
            GetSessionLE(serial);

        if (current is null)
        {
            return ResultCoreLE.Fail(
                $"No existe una sesión ManagerTransportLE para {serial}.");
        }

        if (current.ClientConnected &&
            current.HandshakeVerified &&
            current.State ==
                StateTransportLE.Connected)
        {
            return ResultCoreLE.Ok(
                $"Cliente Android '{current.ClientId}' ya está CONNECTED.");
        }

        TaskCompletionSource<SessionTransportLE> waiter =
            _handshakeWaiters.GetOrAdd(
                serial,
                static _ =>
                    CreateHandshakeWaiterLE());

        try
        {
            SessionTransportLE connected =
                await waiter.Task
                    .WaitAsync(
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);

            return ResultCoreLE.Ok(
                $"Handshake verificado con '{connected.ClientId}'.");
        }
        catch (TimeoutException)
        {
            return ResultCoreLE.Fail(
                $"No llegó HELLO Android en {timeout.TotalSeconds:0.#} segundos.");
        }
    }

    /*
     * =========================================================
     * LE-004F-A FAULT INJECTION
     * =========================================================
     *
     * Destruye solamente TcpClient.
     *
     * NO:
     *   - detiene listener
     *   - elimina adb reverse
     *   - libera puerto
     *   - mata adb
     *
     * Android debe detectar el corte y reconectar.
     * =========================================================
     */

    public Task<ResultCoreLE> InjectClientDisconnectLEAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(serial))
        {
            return Task.FromResult(
                ResultCoreLE.Fail(
                    "El serial es obligatorio."));
        }

        serial = serial.Trim();

        if (!_sessions.TryGetValue(
                serial,
                out SessionTransportLE? session))
        {
            return Task.FromResult(
                ResultCoreLE.Fail(
                    $"No existe sesión ManagerTransportLE para {serial}."));
        }

        if (!_clients.ContainsKey(serial))
        {
            return Task.FromResult(
                ResultCoreLE.Fail(
                    "No existe un TcpClient Android activo para cortar."));
        }

        SessionTransportLE degraded =
            session with
            {
                State = StateTransportLE.Degraded,
                ClientConnected = false,
                HandshakeVerified = false,
                SessionHealthy = false,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                LastError =
                    "LE-004F-A fault injection: conexión TCP Android cerrada intencionalmente."
            };

        _sessions[serial] =
            degraded;

        ResetHandshakeWaiterLE(serial);

        CloseClientLE(serial);

        EnsureAcceptLoopLE(serial);

        return Task.FromResult(
            ResultCoreLE.Ok(
                "LE-004F-A fault injection ejecutado. " +
                "TcpClient cerrado; adb reverse y ListenerTransportLE permanecen activos."));
    }

    public async Task<ResultCoreLE> CloseAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        if (string.IsNullOrWhiteSpace(serial))
        {
            return ResultCoreLE.Fail(
                "El serial es obligatorio.");
        }

        serial = serial.Trim();

        SemaphoreSlim sessionLock =
            GetSessionLockLE(serial);

        await sessionLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (!_sessions.TryGetValue(
                    serial,
                    out SessionTransportLE? session))
            {
                await StopAcceptLoopLEAsync(serial)
                    .ConfigureAwait(false);

                await StopListenerLEAsync(
                        serial,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                _portAllocator.ReleaseBySerial(serial);

                return ResultCoreLE.Ok(
                    "ManagerTransportLE ya estaba cerrado.");
            }

            await CloseInternalLEAsync(
                    serial,
                    session,
                    cancellationToken)
                .ConfigureAwait(false);

            return ResultCoreLE.Ok(
                "ManagerTransportLE cerrado.");
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public bool IsOpen(
        string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return false;
        }

        if (!_sessions.TryGetValue(
                serial.Trim(),
                out SessionTransportLE? session))
        {
            return false;
        }

        return session.State is
            StateTransportLE.Preparing or
            StateTransportLE.ReverseConfigured or
            StateTransportLE.Listening or
            StateTransportLE.Ready or
            StateTransportLE.Connected or
            StateTransportLE.Degraded;
    }

    public bool IsListenerActiveLE(
        string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return false;
        }

        return
            _listeners.TryGetValue(
                serial.Trim(),
                out ListenerTransportLE? listener) &&
            listener.IsListeningLE;
    }

    public SessionTransportLE? GetSessionLE(
        string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        _sessions.TryGetValue(
            serial.Trim(),
            out SessionTransportLE? session);

        return session;
    }

    public IReadOnlyCollection<SessionTransportLE> GetSessionsLE()
    {
        return _sessions
            .Values
            .OrderBy(
                session => session.Serial,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void EnsureAcceptLoopLE(
        string serial)
    {
        if (_disposed)
        {
            return;
        }

        if (!_listeners.TryGetValue(
                serial,
                out ListenerTransportLE? listener) ||
            !listener.IsListeningLE)
        {
            return;
        }

        if (_acceptLoopTasks.TryGetValue(
                serial,
                out Task? existingTask) &&
            !existingTask.IsCompleted)
        {
            return;
        }

        if (_acceptLoopCts.TryRemove(
                serial,
                out CancellationTokenSource? oldCts))
        {
            try
            {
                oldCts.Cancel();
            }
            catch
            {
            }

            oldCts.Dispose();
        }

        var cts =
            new CancellationTokenSource();

        _acceptLoopCts[serial] =
            cts;

        Task task =
            Task.Run(
                () =>
                    AcceptLoopLEAsync(
                        serial,
                        listener,
                        cts.Token),
                CancellationToken.None);

        _acceptLoopTasks[serial] =
            task;
    }

    private async Task AcceptLoopLEAsync(
        string serial,
        ListenerTransportLE listener,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested &&
               !_disposed)
        {
            TcpClient? client = null;

            try
            {
                client =
                    await listener
                        .AcceptTcpClientAsync(
                            cancellationToken)
                        .ConfigureAwait(false);

                if (client is null)
                {
                    continue;
                }

                await HandleClientLEAsync(
                        serial,
                        client,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                UpdateSessionAfterClientFailureLE(
                    serial,
                    ex.Message);

                try
                {
                    await Task.Delay(
                            250,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                if (client is not null)
                {
                    RemoveClientLE(
                        serial,
                        client);

                    try
                    {
                        client.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    private async Task HandleClientLEAsync(
        string serial,
        TcpClient client,
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

        ReplaceClientLE(
            serial,
            client);

        NetworkStream stream =
            client.GetStream();

        string hello;

        using (
            var handshakeCts =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken))
        {
            handshakeCts.CancelAfter(
                HandshakeTimeoutLE);

            hello =
                await HandshakeTransportLE
                    .ReadFrameLEAsync(
                        stream,
                        handshakeCts.Token)
                    .ConfigureAwait(false);

            if (!TryParseHelloLE(
                    hello,
                    out string clientId,
                    out string error))
            {
                throw new InvalidOperationException(
                    error);
            }

            await HandshakeTransportLE
                .WriteFrameLEAsync(
                    stream,
                    $"{HandshakeTransportLE.ProtocolNameLE}|" +
                    $"{HandshakeTransportLE.ProtocolVersionLE}|ACK",
                    handshakeCts.Token)
                .ConfigureAwait(false);

            DateTimeOffset now =
                DateTimeOffset.UtcNow;

            if (!_sessions.TryGetValue(
                    serial,
                    out SessionTransportLE? session))
            {
                throw new InvalidOperationException(
                    $"La sesión de {serial} desapareció durante HELLO/ACK.");
            }

            SessionTransportLE connected =
                session with
                {
                    State = StateTransportLE.Connected,
                    ClientConnected = true,
                    HandshakeVerified = true,
                    ClientId = clientId,
                    ConnectedAtUtc = now,
                    UpdatedAtUtc = now,
                    SessionHealthy = false,
                    LastError = null
                };

            _sessions[serial] =
                connected;

            PublishSessionChangedLE(connected);

            TaskCompletionSource<SessionTransportLE> waiter =
                _handshakeWaiters.GetOrAdd(
                    serial,
                    static _ =>
                        CreateHandshakeWaiterLE());

            waiter.TrySetResult(
                connected);
        }

        await RunPersistentSessionLEAsync(
                serial,
                client,
                stream,
                cancellationToken)
            .ConfigureAwait(false);

        MarkClientDisconnectedLE(serial);
    }

    private static bool TryParseHelloLE(
        string? payload,
        out string clientId,
        out string error)
    {
        clientId = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(payload))
        {
            error = "Handshake vacío.";
            return false;
        }

        string[] parts =
            payload.Trim().Split(
                '|',
                StringSplitOptions.None);

        if (parts.Length != 4)
        {
            error =
                $"HELLO inválido: se esperaban 4 campos y llegaron {parts.Length}.";
            return false;
        }

        if (!string.Equals(
                parts[0],
                HandshakeTransportLE.ProtocolNameLE,
                StringComparison.Ordinal))
        {
            error =
                $"Protocolo inválido: '{parts[0]}'.";
            return false;
        }

        if (!int.TryParse(
                parts[1],
                out int version) ||
            version !=
                HandshakeTransportLE.ProtocolVersionLE)
        {
            error =
                $"Versión incompatible: '{parts[1]}'.";
            return false;
        }

        if (!string.Equals(
                parts[2],
                "HELLO",
                StringComparison.Ordinal))
        {
            error =
                $"Mensaje inválido: '{parts[2]}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(parts[3]))
        {
            error =
                "HELLO no contiene ClientId.";
            return false;
        }

        clientId =
            parts[3].Trim();

        return true;
    }

    private async Task RunPersistentSessionLEAsync(
        string serial,
        TcpClient client,
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string payload;

            using (
                var heartbeatCts =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            cancellationToken))
            {
                heartbeatCts.CancelAfter(
                    HeartbeatProtocolLE.HealthTimeoutLE);

                try
                {
                    payload =
                        await HandshakeTransportLE
                            .ReadFrameLEAsync(
                                stream,
                                heartbeatCts.Token)
                            .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    MarkHeartbeatTimeoutLE(serial);

                    throw new TimeoutException(
                        "No se recibió HEARTBEAT Android dentro del timeout.");
                }
            }

            if (!HeartbeatProtocolLE.TryParseHeartbeatLE(
                    payload,
                    out long sequence,
                    out long clientUnixMilliseconds,
                    out string error))
            {
                throw new InvalidOperationException(
                    error);
            }

            DateTimeOffset heartbeatAt =
                DateTimeOffset.UtcNow;

            if (!_sessions.TryGetValue(
                    serial,
                    out SessionTransportLE? session))
            {
                throw new InvalidOperationException(
                    $"La sesión {serial} desapareció durante HEARTBEAT.");
            }

            string ack =
                HeartbeatProtocolLE.CreateAckPayloadLE(
                    sequence,
                    clientUnixMilliseconds);

            await HandshakeTransportLE
                .WriteFrameLEAsync(
                    stream,
                    ack,
                    cancellationToken)
                .ConfigureAwait(false);

            DateTimeOffset ackAt =
                DateTimeOffset.UtcNow;

            /*
             * IMPORTANTE LE-004F:
             *
             * No calculamos RTT Windows usando clientUnixMilliseconds.
             * Son relojes distintos.
             *
             * Android conserva el RTT real porque mide sentAt -> ackAt
             * usando su propio reloj.
             */

            SessionTransportLE healthy =
                session with
                {
                    State = StateTransportLE.Connected,
                    ClientConnected = true,
                    HandshakeVerified = true,
                    SessionHealthy = true,
                    HeartbeatSequence = sequence,
                    HeartbeatCount =
                        session.HeartbeatCount + 1,
                    LastHeartbeatAtUtc = heartbeatAt,
                    LastHeartbeatAckAtUtc = ackAt,
                    LastRoundTripMilliseconds = null,
                    UpdatedAtUtc = ackAt,
                    LastError = null
                };

            _sessions[serial] =
                healthy;

            PublishSessionChangedLE(healthy);
        }
    }

    private void MarkHeartbeatTimeoutLE(
        string serial)
    {
        if (!_sessions.TryGetValue(
                serial,
                out SessionTransportLE? session))
        {
            return;
        }

        SessionTransportLE degraded =
            session with
            {
                State = StateTransportLE.Degraded,
                SessionHealthy = false,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                LastError =
                    "LE-004F heartbeat timeout."
            };

        _sessions[serial] = degraded;
        PublishSessionChangedLE(degraded);
    }

    private void MarkClientDisconnectedLE(
        string serial)
    {
        if (!_sessions.TryGetValue(
                serial,
                out SessionTransportLE? session))
        {
            return;
        }

        if (session.State is
            StateTransportLE.Closing or
            StateTransportLE.Failed)
        {
            return;
        }

        bool listenerActive =
            IsListenerActiveLE(serial);

        SessionTransportLE disconnected =
            session with
            {
                State =
                    listenerActive
                        ? StateTransportLE.Ready
                        : StateTransportLE.Degraded,
                ClientConnected = false,
                HandshakeVerified = false,
                SessionHealthy = false,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                LastError =
                    listenerActive
                        ? "Esperando reconexión Android."
                        : "Cliente desconectado y listener no disponible."
            };

        _sessions[serial] = disconnected;
        PublishSessionChangedLE(disconnected);

        ResetHandshakeWaiterLE(serial);
    }

    private void UpdateSessionAfterClientFailureLE(
        string serial,
        string message)
    {
        if (!_sessions.TryGetValue(
                serial,
                out SessionTransportLE? session))
        {
            return;
        }

        if (session.State is
            StateTransportLE.Closing or
            StateTransportLE.Failed)
        {
            return;
        }

        bool listenerActive =
            IsListenerActiveLE(serial);

        SessionTransportLE failed =
            session with
            {
                State =
                    listenerActive
                        ? StateTransportLE.Ready
                        : StateTransportLE.Degraded,
                ClientConnected = false,
                HandshakeVerified = false,
                SessionHealthy = false,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                LastError =
                    $"Cliente Android desconectado: {message}"
            };

        _sessions[serial] = failed;
        PublishSessionChangedLE(failed);

        ResetHandshakeWaiterLE(serial);
    }

    private void PublishSessionChangedLE(
        SessionTransportLE session)
    {
        try
        {
            SessionChangedLE?.Invoke(
                this,
                session);
        }
        catch
        {
            /*
             * Observers nunca deben romper el transporte.
             */
        }
    }

    private void ReplaceClientLE(
        string serial,
        TcpClient client)
    {
        if (_clients.TryGetValue(
                serial,
                out TcpClient? previous) &&
            !ReferenceEquals(previous, client))
        {
            try
            {
                previous.Dispose();
            }
            catch
            {
            }
        }

        _clients[serial] = client;
    }

    private void RemoveClientLE(
        string serial,
        TcpClient client)
    {
        if (_clients.TryGetValue(
                serial,
                out TcpClient? current) &&
            ReferenceEquals(current, client))
        {
            _clients.TryRemove(
                serial,
                out _);
        }
    }

    private void CloseClientLE(
        string serial)
    {
        if (!_clients.TryRemove(
                serial,
                out TcpClient? client))
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
        }

        try
        {
            client.Dispose();
        }
        catch
        {
        }
    }

    private static TaskCompletionSource<SessionTransportLE>
        CreateHandshakeWaiterLE()
    {
        return new TaskCompletionSource<SessionTransportLE>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void ResetHandshakeWaiterLE(
        string serial)
    {
        _handshakeWaiters[serial] =
            CreateHandshakeWaiterLE();
    }

    private async Task StopAcceptLoopLEAsync(
        string serial)
    {
        CancellationTokenSource? cts = null;

        if (_acceptLoopCts.TryRemove(
                serial,
                out cts))
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
            }
        }

        CloseClientLE(serial);

        if (_acceptLoopTasks.TryRemove(
                serial,
                out Task? task))
        {
            try
            {
                await task
                    .WaitAsync(
                        TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        cts?.Dispose();

        if (_handshakeWaiters.TryRemove(
                serial,
                out TaskCompletionSource<SessionTransportLE>? waiter))
        {
            waiter.TrySetCanceled();
        }
    }

    private async Task<ResultCoreLE> StartListenerLEAsync(
        string serial,
        int port,
        CancellationToken cancellationToken)
    {
        if (_listeners.TryGetValue(
                serial,
                out ListenerTransportLE? existing))
        {
            if (existing.IsListeningLE &&
                existing.PortLE == port)
            {
                return ResultCoreLE.Ok(
                    $"ListenerTransportLE ya escucha en 127.0.0.1:{port}.");
            }

            await StopListenerLEAsync(
                    serial,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        var listener =
            new ListenerTransportLE();

        try
        {
            await listener
                .StartAsync(
                    port,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!_listeners.TryAdd(
                    serial,
                    listener))
            {
                await listener.DisposeAsync()
                    .ConfigureAwait(false);

                return ResultCoreLE.Fail(
                    $"No se pudo registrar ListenerTransportLE para {serial}.");
            }

            return ResultCoreLE.Ok(
                $"ListenerTransportLE LISTENING en 127.0.0.1:{port}.");
        }
        catch (OperationCanceledException)
        {
            await listener.DisposeAsync()
                .ConfigureAwait(false);

            throw;
        }
        catch (Exception ex)
        {
            await listener.DisposeAsync()
                .ConfigureAwait(false);

            return ResultCoreLE.Fail(
                $"ListenerTransportLE no pudo escuchar: {ex.Message}");
        }
    }

    private async Task StopListenerLEAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        if (!_listeners.TryRemove(
                serial,
                out ListenerTransportLE? listener))
        {
            return;
        }

        try
        {
            await listener
                .StopAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await listener.DisposeAsync()
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task<ResultCoreLE> ConfigureReverseLEAsync(
        string serial,
        int devicePort,
        int hostPort,
        CancellationToken cancellationToken)
    {
        if (_adbService is null)
        {
            return ResultCoreLE.Fail(
                "ADB no está configurado.");
        }

        try
        {
            await _adbService
                .ExecuteRawAsync(
                    new[]
                    {
                        "-s",
                        serial,
                        "reverse",
                        $"tcp:{devicePort}",
                        $"tcp:{hostPort}"
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            return ResultCoreLE.Ok(
                $"adb reverse tcp:{devicePort} -> tcp:{hostPort} creado.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ResultCoreLE.Fail(
                $"No se pudo crear adb reverse: {ex.Message}");
        }
    }

    private static bool ContainsReverseMappingLE(
        string? output,
        int expectedDevicePort,
        int expectedHostPort)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        string expectedDevice =
            $"tcp:{expectedDevicePort}";

        string expectedHost =
            $"tcp:{expectedHostPort}";

        string[] lines =
            output.Split(
                new[]
                {
                    "\r\n",
                    "\n",
                    "\r"
                },
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        foreach (string line in lines)
        {
            string[] columns =
                line.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

            if (columns.Length >= 3 &&
                string.Equals(
                    columns[^2],
                    expectedDevice,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    columns[^1],
                    expectedHost,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (columns.Length == 2 &&
                string.Equals(
                    columns[0],
                    expectedDevice,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    columns[1],
                    expectedHost,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task CloseInternalLEAsync(
        string serial,
        SessionTransportLE session,
        CancellationToken cancellationToken)
    {
        _sessions[serial] =
            session with
            {
                State = StateTransportLE.Closing,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

        await StopAcceptLoopLEAsync(serial)
            .ConfigureAwait(false);

        if (session.ReverseConfigured &&
            _adbService is not null)
        {
            await CleanupReverseLEAsync(
                    serial,
                    session.DevicePort,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await StopListenerLEAsync(
                serial,
                CancellationToken.None)
            .ConfigureAwait(false);

        _sessions.TryRemove(
            serial,
            out _);

        _portAllocator.Release(
            session.HostPort);
    }

    private async Task CleanupReverseLEAsync(
        string serial,
        int devicePort,
        CancellationToken cancellationToken)
    {
        if (_adbService is null)
        {
            return;
        }

        try
        {
            await _adbService
                .ExecuteRawAsync(
                    new[]
                    {
                        "-s",
                        serial,
                        "reverse",
                        "--remove",
                        $"tcp:{devicePort}"
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task FailAndCleanupLEAsync(
        string serial,
        SessionTransportLE session,
        string message)
    {
        _sessions[serial] =
            session with
            {
                State = StateTransportLE.Failed,
                ClientConnected = false,
                HandshakeVerified = false,
                SessionHealthy = false,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                LastError = message
            };

        await StopAcceptLoopLEAsync(serial)
            .ConfigureAwait(false);

        if (session.ReverseConfigured &&
            _adbService is not null)
        {
            await CleanupReverseLEAsync(
                    serial,
                    session.DevicePort,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        await StopListenerLEAsync(
                serial,
                CancellationToken.None)
            .ConfigureAwait(false);

        _portAllocator.Release(
            session.HostPort);
    }

    private SemaphoreSlim GetSessionLockLE(
        string serial)
    {
        return _sessionLocks.GetOrAdd(
            serial,
            static _ =>
                new SemaphoreSlim(1, 1));
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

        _disposed = true;

        string[] acceptSerials =
            _acceptLoopCts.Keys
                .Concat(_acceptLoopTasks.Keys)
                .Concat(_clients.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        foreach (string serial in acceptSerials)
        {
            await StopAcceptLoopLEAsync(serial)
                .ConfigureAwait(false);
        }

        SessionTransportLE[] sessions =
            _sessions.Values.ToArray();

        foreach (SessionTransportLE session in sessions)
        {
            if (session.ReverseConfigured &&
                _adbService is not null)
            {
                await CleanupReverseLEAsync(
                        session.Serial,
                        session.DevicePort,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await StopListenerLEAsync(
                    session.Serial,
                    CancellationToken.None)
                .ConfigureAwait(false);

            _portAllocator.Release(
                session.HostPort);
        }

        _sessions.Clear();

        foreach (SemaphoreSlim sessionLock in
                 _sessionLocks.Values)
        {
            sessionLock.Dispose();
        }

        _sessionLocks.Clear();
        _acceptLoopCts.Clear();
        _acceptLoopTasks.Clear();
        _clients.Clear();
        _handshakeWaiters.Clear();

        _portAllocator.Clear();
    }
}
