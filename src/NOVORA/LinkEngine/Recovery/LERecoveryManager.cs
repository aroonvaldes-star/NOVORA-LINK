using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Device;
using NOVORA.LinkEngine.Metrics;
using NOVORA.LinkEngine.Network;
using NOVORA.LinkEngine.Transport;

namespace NOVORA.LinkEngine.Recovery;

public sealed class LERecoveryManager : IAsyncDisposable
{
    private static readonly TimeSpan SocketRecoveryTimeoutLE =
        TimeSpan.FromSeconds(15);

    private static readonly TimeSpan InfrastructureRecoveryTimeoutLE =
        TimeSpan.FromSeconds(25);

    private static readonly TimeSpan PollIntervalLE =
        TimeSpan.FromMilliseconds(200);

    private static readonly TimeSpan AndroidHandshakeTimeoutLE =
        TimeSpan.FromSeconds(15);

    private const long RequiredRecoveryHeartbeatsLE =
        3;

    private readonly LEDeviceManager _device;
    private readonly LETransportManager _transport;
    private readonly LENetworkManager _network;
    private readonly LEMetricsCollector _metrics;

    private readonly ConcurrentDictionary<string, byte> _registered =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _recoveryLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _initialized;
    private bool _disposed;

    public LERecoveryManager(
        LEDeviceManager device,
        LETransportManager transport,
        LENetworkManager network,
        LEMetricsCollector metrics)
    {
        _device =
            device ??
            throw new ArgumentNullException(nameof(device));

        _transport =
            transport ??
            throw new ArgumentNullException(nameof(transport));

        _network =
            network ??
            throw new ArgumentNullException(nameof(network));

        _metrics =
            metrics ??
            throw new ArgumentNullException(nameof(metrics));
    }

    public Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        cancellationToken.ThrowIfCancellationRequested();

        _initialized = true;

        return Task.CompletedTask;
    }

    public Task RegisterAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        cancellationToken.ThrowIfCancellationRequested();

        if (!_initialized)
        {
            throw new InvalidOperationException(
                "LERecoveryManager no está inicializado.");
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException(
                "El serial es obligatorio.",
                nameof(serial));
        }

        _registered[serial.Trim()] = 0;

        return Task.CompletedTask;
    }

    public Task UnregisterAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(serial))
        {
            _registered.TryRemove(
                serial.Trim(),
                out _);
        }

        return Task.CompletedTask;
    }

    public bool IsHealthy(
        string serial)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(serial))
        {
            return false;
        }

        serial = serial.Trim();

        if (!_registered.ContainsKey(serial))
        {
            return false;
        }

        LETransportSession? session =
            _transport.GetSessionLE(serial);

        if (session is null)
        {
            return false;
        }

        bool transportHealthy =
            session.State ==
                LETransportState.Connected &&
            session.ReverseConfigured &&
            session.ReverseVerified &&
            session.ListenerStarted &&
            session.ClientConnected &&
            session.HandshakeVerified &&
            session.SessionHealthy;

        if (!transportHealthy)
        {
            return false;
        }

        return _device.IsConnected(serial) &&
               (
                   !_network.IsActive(serial) ||
                   _metrics.GetSnapshot(serial).InternetActive
               );
    }

    /*
     * =========================================================
     * LE-004F-A
     * =========================================================
     *
     * Recuperación ligera.
     *
     * El listener y adb reverse siguen vivos.
     * Solo esperamos que Android reconstruya TcpClient.
     * =========================================================
     */

    public Task<LECoreResult> RecoverAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        return RecoverSocketAsync(
            serial,
            SocketRecoveryTimeoutLE,
            RequiredRecoveryHeartbeatsLE,
            cancellationToken);
    }

    public async Task<LECoreResult> RecoverAsync(
        string serial,
        TimeSpan timeout,
        long requiredHeartbeats,
        CancellationToken cancellationToken = default)
    {
        return await RecoverSocketAsync(
                serial,
                timeout,
                requiredHeartbeats,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LECoreResult> RecoverSocketAsync(
        string serial,
        TimeSpan timeout,
        long requiredHeartbeats,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        LECoreResult validation =
            ValidateRecoveryRequestLE(
                serial,
                timeout,
                requiredHeartbeats);

        if (!validation.Success)
        {
            return validation;
        }

        serial = serial.Trim();

        SemaphoreSlim recoveryLock =
            GetRecoveryLockLE(serial);

        await recoveryLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            LETransportSession? before =
                _transport.GetSessionLE(serial);

            if (before is null)
            {
                return LECoreResult.Fail(
                    "LETransportManager no tiene una sesión que recuperar.");
            }

            long baselineHeartbeatCount =
                before.HeartbeatCount;

            var metrics =
                _metrics.GetOrCreate(serial);

            metrics.SetState(
                LECoreStates.Recovering);

            DateTimeOffset deadline =
                DateTimeOffset.UtcNow +
                timeout;

            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                LETransportSession? current =
                    _transport.GetSessionLE(serial);

                if (current is null)
                {
                    metrics.RegisterRecoveryAttempt(false);

                    return LECoreResult.Fail(
                        "LETransportManager perdió la sesión durante recovery.");
                }

                if (!current.ReverseConfigured ||
                    !current.ReverseVerified)
                {
                    metrics.RegisterRecoveryAttempt(false);

                    return LECoreResult.Fail(
                        "adb reverse no está disponible. " +
                        "Se requiere RecoverInfrastructureAsync.");
                }

                if (!current.ListenerStarted ||
                    !_transport.IsListenerActiveLE(serial))
                {
                    metrics.RegisterRecoveryAttempt(false);

                    return LECoreResult.Fail(
                        "LETransportListener no está disponible. " +
                        "Se requiere RecoverInfrastructureAsync.");
                }

                long newHeartbeats =
                    Math.Max(
                        0,
                        current.HeartbeatCount -
                        baselineHeartbeatCount);

                if (IsRecoveredLE(
                        current,
                        newHeartbeats,
                        requiredHeartbeats))
                {
                    metrics.RegisterRecoveryAttempt(true);

                    metrics.SetState(
                        LECoreStates.Connected);

                    return LECoreResult.Ok(
                        "LE-004F-A recovery correcto. " +
                        $"Nuevos HEARTBEAT: {newHeartbeats}.");
                }

                await Task.Delay(
                        PollIntervalLE,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            metrics.RegisterRecoveryAttempt(false);

            return LECoreResult.Fail(
                "Recovery de socket agotó el timeout.");
        }
        finally
        {
            recoveryLock.Release();
        }
    }

    /*
     * =========================================================
     * LE-004F-B
     * =========================================================
     *
     * Recuperación de infraestructura.
     *
     * Puede reconstruir:
     *
     *   LETransportSession
     *   LETransportListener
     *   adb reverse
     *   accept loop
     *
     * Después espera que Android reconecte automáticamente.
     * =========================================================
     */

    public async Task<LECoreResult> RecoverInfrastructureAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        return await RecoverInfrastructureAsync(
                serial,
                InfrastructureRecoveryTimeoutLE,
                RequiredRecoveryHeartbeatsLE,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LECoreResult> RecoverInfrastructureAsync(
        string serial,
        TimeSpan timeout,
        long requiredHeartbeats,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        LECoreResult validation =
            ValidateRecoveryRequestLE(
                serial,
                timeout,
                requiredHeartbeats);

        if (!validation.Success)
        {
            return validation;
        }

        serial = serial.Trim();

        SemaphoreSlim recoveryLock =
            GetRecoveryLockLE(serial);

        await recoveryLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var metrics =
                _metrics.GetOrCreate(serial);

            metrics.SetState(
                LECoreStates.Recovering);

            /*
             * El dispositivo debe seguir disponible vía ADB.
             *
             * LE-004F-B recupera infraestructura de transporte.
             * No recupera todavía una desconexión física USB/ADB.
             *
             * Eso corresponde a LE-004F-C.
             */

            if (!_device.IsConnected(serial))
            {
                metrics.RegisterRecoveryAttempt(false);

                return LECoreResult.Fail(
                    "LEDeviceManager ya no está conectado. " +
                    "LE-004F-B requiere ADB online; " +
                    "la caída física se probará en LE-004F-C.");
            }

            /*
             * Intentamos verificar primero.
             *
             * Si el mapping desapareció, LETransportManager marcará
             * la sesión como Failed.
             */

            try
            {
                await _transport
                    .VerifyAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                /*
                 * El rebuild siguiente es la autoridad.
                 */
            }

            /*
             * Cerramos cualquier infraestructura dañada.
             *
             * CloseAsync:
             *
             *   - detiene accept loop
             *   - cierra TcpClient
             *   - limpia reverse si aún existe
             *   - detiene listener
             *   - libera puerto
             */

            LECoreResult close =
                await _transport
                    .CloseAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!close.Success)
            {
                metrics.RegisterRecoveryAttempt(false);

                return LECoreResult.Fail(
                    "LERecoveryManager no pudo cerrar el transporte dañado: " +
                    close.Message);
            }

            /*
             * OpenAsync reconstruye desde cero:
             *
             *   PortAllocator
             *   TcpListener
             *   adb reverse
             *   reverse verification
             *   accept loop
             */

            LECoreResult open =
                await _transport
                    .OpenAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!open.Success)
            {
                metrics.RegisterRecoveryAttempt(false);

                return LECoreResult.Fail(
                    "LERecoveryManager no pudo reconstruir LETransportManager: " +
                    open.Message);
            }

            LECoreResult verify =
                await _transport
                    .VerifyAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!verify.Success)
            {
                metrics.RegisterRecoveryAttempt(false);

                return LECoreResult.Fail(
                    "LETransportManager fue recreado, pero adb reverse " +
                    "no pudo verificarse: " +
                    verify.Message);
            }

            LETransportSession? rebuilt =
                _transport.GetSessionLE(serial);

            if (rebuilt is null)
            {
                metrics.RegisterRecoveryAttempt(false);

                return LECoreResult.Fail(
                    "LETransportManager no expuso la nueva sesión.");
            }

            if (!rebuilt.ReverseConfigured ||
                !rebuilt.ReverseVerified ||
                !rebuilt.ListenerStarted ||
                !_transport.IsListenerActiveLE(serial))
            {
                metrics.RegisterRecoveryAttempt(false);

                return LECoreResult.Fail(
                    "La infraestructura fue recreada parcialmente, " +
                    "pero no está READY.");
            }

            /*
             * Android ya posee reconnect loop.
             *
             * Al reaparecer adb reverse, debe crear un socket nuevo
             * sin reiniciar la aplicación.
             */

            LECoreResult handshake =
                await _transport
                    .WaitForHandshakeAsync(
                        serial,
                        AndroidHandshakeTimeoutLE,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!handshake.Success)
            {
                metrics.RegisterRecoveryAttempt(false);

                return LECoreResult.Fail(
                    "La infraestructura fue reconstruida, " +
                    "pero Android no completó el nuevo HELLO/ACK: " +
                    handshake.Message);
            }

            DateTimeOffset deadline =
                DateTimeOffset.UtcNow +
                timeout;

            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                LETransportSession? current =
                    _transport.GetSessionLE(serial);

                if (current is null)
                {
                    metrics.RegisterRecoveryAttempt(false);

                    return LECoreResult.Fail(
                        "La sesión reconstruida desapareció.");
                }

                /*
                 * La sesión es nueva, por lo que HeartbeatCount
                 * parte desde cero.
                 */

                if (IsRecoveredLE(
                        current,
                        current.HeartbeatCount,
                        requiredHeartbeats))
                {
                    metrics.RegisterRecoveryAttempt(true);

                    metrics.SetState(
                        LECoreStates.Connected);

                    return LECoreResult.Ok(
                        "LE-004F-B recovery correcto. " +
                        "LETransportManager fue reconstruido, adb reverse fue recreado, " +
                        "Android reconectó y la sesión volvió a HEALTHY con " +
                        $"{current.HeartbeatCount} HEARTBEAT/ACK.");
                }

                await Task.Delay(
                        PollIntervalLE,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            metrics.RegisterRecoveryAttempt(false);

            LETransportSession? final =
                _transport.GetSessionLE(serial);

            return LECoreResult.Fail(
                "LE-004F-B agotó el timeout. " +
                $"Estado: {final?.State.ToString() ?? "SIN SESIÓN"}; " +
                $"Client: {final?.ClientConnected ?? false}; " +
                $"Handshake: {final?.HandshakeVerified ?? false}; " +
                $"Healthy: {final?.SessionHealthy ?? false}; " +
                $"Heartbeats: {final?.HeartbeatCount ?? 0}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var metrics =
                _metrics.GetOrCreate(serial);

            metrics.RegisterRecoveryAttempt(false);

            return LECoreResult.Fail(
                $"LE-004F-B LERecoveryManager falló: {ex.Message}");
        }
        finally
        {
            recoveryLock.Release();
        }
    }

    private LECoreResult ValidateRecoveryRequestLE(
        string serial,
        TimeSpan timeout,
        long requiredHeartbeats)
    {
        if (!_initialized)
        {
            return LECoreResult.Fail(
                "LERecoveryManager no está inicializado.");
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            return LECoreResult.Fail(
                "El serial es obligatorio.");
        }

        serial = serial.Trim();

        if (!_registered.ContainsKey(serial))
        {
            return LECoreResult.Fail(
                "La sesión no está registrada en LERecoveryManager.");
        }

        if (timeout <= TimeSpan.Zero)
        {
            return LECoreResult.Fail(
                "El timeout debe ser mayor que cero.");
        }

        if (requiredHeartbeats <= 0)
        {
            return LECoreResult.Fail(
                "requiredHeartbeats debe ser mayor que cero.");
        }

        return LECoreResult.Ok(
            "Recovery request válido.");
    }

    private static bool IsRecoveredLE(
        LETransportSession session,
        long heartbeatCount,
        long requiredHeartbeats)
    {
        return
            session.State ==
                LETransportState.Connected &&
            session.ReverseConfigured &&
            session.ReverseVerified &&
            session.ListenerStarted &&
            session.ClientConnected &&
            session.HandshakeVerified &&
            session.SessionHealthy &&
            heartbeatCount >=
                requiredHeartbeats;
    }

    private SemaphoreSlim GetRecoveryLockLE(
        string serial)
    {
        return _recoveryLocks.GetOrAdd(
            serial,
            static _ =>
                new SemaphoreSlim(1, 1));
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        _registered.Clear();

        foreach (SemaphoreSlim recoveryLock in
                 _recoveryLocks.Values)
        {
            recoveryLock.Dispose();
        }

        _recoveryLocks.Clear();

        _initialized = false;

        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }
}

