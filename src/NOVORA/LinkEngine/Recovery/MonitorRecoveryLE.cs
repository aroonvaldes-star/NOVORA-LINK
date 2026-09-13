using System;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Device;
using NOVORA.LinkEngine.Transport;
using NOVORA.Services;

namespace NOVORA.LinkEngine.Recovery;

public enum RecoveryMonitorStateLE
{
    Stopped,
    Starting,
    Healthy,
    Degraded,
    WaitingForDevice,
    RecoveringSocket,
    RecoveringInfrastructure,
    Recovered,
    Failed,
    Stopping
}

public sealed record RecoveryMonitorStatusLE(
    string Serial,
    RecoveryMonitorStateLE State,
    string Message,
    long CheckCount,
    long RecoveryAttemptCount,
    long RecoverySuccessCount,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastHealthyAtUtc,
    DateTimeOffset? LastRecoveryAtUtc,
    string? LastError);

/// <summary>
/// Monitor event-driven de Recovery para LinkEngine.
///
/// Regla arquitectónica NOVORA:
/// - NO sondeo periódico para descubrir cambios.
/// - SessionChangedLE despierta la evaluación.
/// - Los timers sólo confirman un fallo persistente (deadline).
/// - Congestión/WouldBlock/backpressure nunca disparan Recovery.
/// </summary>
public sealed class MonitorRecoveryLE : IAsyncDisposable
{
    private static readonly TimeSpan SocketFailureConfirmationLE =
        TimeSpan.FromSeconds(3);

    private static readonly TimeSpan InfrastructureFailureConfirmationLE =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan SocketRecoveryTimeoutLE =
        TimeSpan.FromSeconds(15);

    private static readonly TimeSpan InfrastructureRecoveryTimeoutLE =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan DeviceReturnTimeoutLE =
        TimeSpan.FromSeconds(60);

    private const long RequiredRecoveryHeartbeatsLE = 3;

    private readonly ManagerDeviceLE _device;
    private readonly ManagerTransportLE _transport;
    private readonly ManagerRecoveryLE _recovery;
    private readonly AdbService _adb;

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _signalLE = new(0, 1);

    private CancellationTokenSource? _lifetimeCts;
    private Task? _workerTask;
    private string? _serial;
    private long _checkCount;
    private long _recoveryAttemptCount;
    private long _recoverySuccessCount;
    private RecoveryMonitorStatusLE? _status;
    private bool _transportSubscribedLE;
    private bool _disposed;

    public MonitorRecoveryLE(
        ManagerDeviceLE device,
        ManagerTransportLE transport,
        ManagerRecoveryLE recovery,
        AdbService adb)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    }

    public event EventHandler<RecoveryMonitorStatusLE>? StatusChangedLE;

    public RecoveryMonitorStatusLE? StatusLE => _status;

    public bool IsRunningLE =>
        _workerTask is { IsCompleted: false };

    public long RecoverySuccessCountLE =>
        Interlocked.Read(ref _recoverySuccessCount);

    public async Task StartAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("El serial es obligatorio.", nameof(serial));

        serial = serial.Trim();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_workerTask is { IsCompleted: false })
            {
                if (string.Equals(_serial, serial, StringComparison.OrdinalIgnoreCase))
                    return;

                throw new InvalidOperationException(
                    "MonitorRecoveryLE ya vigila otro dispositivo.");
            }

            _serial = serial;
            _checkCount = 0;
            _recoveryAttemptCount = 0;
            _recoverySuccessCount = 0;

            _lifetimeCts?.Dispose();
            _lifetimeCts = new CancellationTokenSource();

            if (!_transportSubscribedLE)
            {
                _transport.SessionChangedLE += Transport_SessionChangedLE;
                _transportSubscribedLE = true;
            }

            PublishStatusLE(
                RecoveryMonitorStateLE.Starting,
                "MonitorRecoveryLE event-driven iniciado.",
                null);

            _workerTask = Task.Run(
                () => RunMonitorLEAsync(serial, _lifetimeCts.Token),
                CancellationToken.None);

            // Evaluación inicial única; después sólo reaccionamos a eventos.
            SignalMonitorLE();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PublishStatusLE(
                RecoveryMonitorStateLE.Stopping,
                "Deteniendo MonitorRecoveryLE.",
                null);

            if (_transportSubscribedLE)
            {
                _transport.SessionChangedLE -= Transport_SessionChangedLE;
                _transportSubscribedLE = false;
            }

            CancellationTokenSource? cts = _lifetimeCts;
            Task? worker = _workerTask;
            cts?.Cancel();
            SignalMonitorLE();

            if (worker is not null)
            {
                try
                {
                    await worker.WaitAsync(
                            TimeSpan.FromSeconds(3),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (TimeoutException) { }
                catch { }
            }

            _workerTask = null;
            _lifetimeCts?.Dispose();
            _lifetimeCts = null;

            PublishStatusLE(
                RecoveryMonitorStateLE.Stopped,
                "MonitorRecoveryLE detenido.",
                null);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void Transport_SessionChangedLE(
        object? sender,
        SessionTransportLE session)
    {
        string? serial = _serial;
        if (serial is null ||
            !string.Equals(serial, session.Serial, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SignalMonitorLE();
    }

    private void SignalMonitorLE()
    {
        try
        {
            if (_signalLE.CurrentCount == 0)
                _signalLE.Release();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private async Task RunMonitorLEAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _signalLE.WaitAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                Interlocked.Increment(ref _checkCount);
                await EvaluateTransportEventLEAsync(serial, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                PublishStatusLE(
                    RecoveryMonitorStateLE.Failed,
                    $"MonitorRecoveryLE: {ex.Message}",
                    ex.Message);
            }
        }
    }

    private async Task EvaluateTransportEventLEAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        SessionTransportLE? session = _transport.GetSessionLE(serial);

        if (IsSessionHealthyLE(session))
        {
            PublishHealthyTransitionLE(session!);
            return;
        }

        bool infrastructureSuspect =
            session is null ||
            !session.ReverseConfigured ||
            !session.ReverseVerified ||
            !session.ListenerStarted ||
            !_transport.IsListenerActiveLE(serial);

        if (infrastructureSuspect)
        {
            string reason =
                session?.LastError ??
                "Infraestructura de transporte degradada.";

            PublishStatusLE(
                RecoveryMonitorStateLE.Degraded,
                "Infraestructura degradada. Confirmando persistencia; Recovery IDLE.",
                reason);

            await Task.Delay(
                    InfrastructureFailureConfirmationLE,
                    cancellationToken)
                .ConfigureAwait(false);

            session = _transport.GetSessionLE(serial);
            if (IsSessionHealthyLE(session))
            {
                PublishHealthyTransitionLE(session!);
                return;
            }

            infrastructureSuspect =
                session is null ||
                !session.ReverseConfigured ||
                !session.ReverseVerified ||
                !session.ListenerStarted ||
                !_transport.IsListenerActiveLE(serial);

            if (!infrastructureSuspect)
            {
                SignalMonitorLE();
                return;
            }

            // Una sola comprobación ADB tras confirmar un fallo real.
            bool adbOnline = await IsAdbOnlineLEAsync(serial, cancellationToken)
                .ConfigureAwait(false);

            if (!adbOnline)
            {
                PublishStatusLE(
                    RecoveryMonitorStateLE.WaitingForDevice,
                    "ADB/device perdido. Esperando evento wait-for-device.",
                    null);

                bool returned = await WaitForDeviceReturnLEAsync(
                        serial,
                        DeviceReturnTimeoutLE,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!returned)
                {
                    PublishStatusLE(
                        RecoveryMonitorStateLE.Failed,
                        "El dispositivo no regresó dentro del timeout.",
                        "ADB device return timeout.");
                    return;
                }

                await RecoverAfterDeviceReturnLEAsync(serial, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            ResultCoreLE verify = await _transport
                .VerifyAsync(serial, cancellationToken)
                .ConfigureAwait(false);

            session = _transport.GetSessionLE(serial);
            if (IsSessionHealthyLE(session))
            {
                PublishHealthyTransitionLE(session!);
                return;
            }

            string infrastructureError =
                session?.LastError ??
                verify.Message ??
                "Infraestructura degradada después de VerifyAsync.";

            await RecoverInfrastructureLEAsync(
                    serial,
                    infrastructureError,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // Infraestructura presente, pero el canal lógico no está sano.
        PublishStatusLE(
            RecoveryMonitorStateLE.Degraded,
            "Canal lógico degradado. Confirmando persistencia; Recovery IDLE.",
            session?.LastError);

        await Task.Delay(
                SocketFailureConfirmationLE,
                cancellationToken)
            .ConfigureAwait(false);

        session = _transport.GetSessionLE(serial);
        if (IsSessionHealthyLE(session))
        {
            PublishHealthyTransitionLE(session!);
            return;
        }

        if (session is null ||
            !session.ReverseConfigured ||
            !session.ReverseVerified ||
            !session.ListenerStarted ||
            !_transport.IsListenerActiveLE(serial))
        {
            SignalMonitorLE();
            return;
        }

        await RecoverSocketLEAsync(serial, session, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsSessionHealthyLE(
        SessionTransportLE? session)
        => session is not null &&
           session.State == StateTransportLE.Connected &&
           session.ClientConnected &&
           session.HandshakeVerified &&
           session.SessionHealthy;

    private void PublishHealthyTransitionLE(
        SessionTransportLE session)
    {
        // Evita refrescar UI/Runtime por cada heartbeat sano.
        if (_status?.State == RecoveryMonitorStateLE.Healthy)
            return;

        PublishStatusLE(
            RecoveryMonitorStateLE.Healthy,
            $"HEALTHY · heartbeat #{session.HeartbeatSequence}.",
            null);
    }

    private async Task RecoverSocketLEAsync(
        string serial,
        SessionTransportLE session,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(
            ref _recoveryAttemptCount);

        PublishStatusLE(
            RecoveryMonitorStateLE.RecoveringSocket,
            "Canal lógico degradado. Recovery de socket.",
            session.LastError);

        ResultCoreLE result =
            await _recovery
                .RecoverSocketAsync(
                    serial,
                    SocketRecoveryTimeoutLE,
                    RequiredRecoveryHeartbeatsLE,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!result.Success)
        {
            /*
             * Si la recuperación ligera falla, volvemos a mirar
             * infraestructura.
             *
             * Puede haber desaparecido reverse/listener mientras
             * esperÃ¡bamos el socket.
             */

            SessionTransportLE? current =
                _transport.GetSessionLE(
                    serial);

            bool infrastructureBroken =
                current is null ||
                !current.ReverseConfigured ||
                !current.ReverseVerified ||
                !current.ListenerStarted ||
                !_transport.IsListenerActiveLE(
                    serial);

            if (infrastructureBroken)
            {
                await RecoverInfrastructureLEAsync(
                        serial,
                        result.Message,
                        cancellationToken)
                    .ConfigureAwait(false);

                return;
            }

            PublishStatusLE(
                RecoveryMonitorStateLE.Degraded,
                result.Message,
                result.Message);

            return;
        }

        Interlocked.Increment(
            ref _recoverySuccessCount);

        PublishStatusLE(
            RecoveryMonitorStateLE.Recovered,
            result.Message,
            null);
    }

    private async Task RecoverInfrastructureLEAsync(
        string serial,
        string reason,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(
            ref _recoveryAttemptCount);

        PublishStatusLE(
            RecoveryMonitorStateLE.RecoveringInfrastructure,
            $"Reconstruyendo infraestructura: {reason}",
            reason);

        ResultCoreLE result =
            await _recovery
                .RecoverInfrastructureAsync(
                    serial,
                    InfrastructureRecoveryTimeoutLE,
                    RequiredRecoveryHeartbeatsLE,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!result.Success)
        {
            PublishStatusLE(
                RecoveryMonitorStateLE.Degraded,
                result.Message,
                result.Message);

            return;
        }

        Interlocked.Increment(
            ref _recoverySuccessCount);

        PublishStatusLE(
            RecoveryMonitorStateLE.Recovered,
            result.Message,
            null);
    }

    private async Task RecoverAfterDeviceReturnLEAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(
            ref _recoveryAttemptCount);

        PublishStatusLE(
            RecoveryMonitorStateLE.RecoveringInfrastructure,
            "ADB regresó. Restaurando ManagerDeviceLE y ManagerTransportLE.",
            null);

        /*
         * Refrescamos ManagerDeviceLE.
         */

        try
        {
            await _device
                .DisconnectAsync(
                    serial,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
        }

        ResultCoreLE device =
            await _device
                .ConnectAsync(
                    serial,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!device.Success)
        {
            PublishStatusLE(
                RecoveryMonitorStateLE.Degraded,
                $"ManagerDeviceLE no pudo reconectar: {device.Message}",
                device.Message);

            return;
        }

        ResultCoreLE infrastructure =
            await _recovery
                .RecoverInfrastructureAsync(
                    serial,
                    InfrastructureRecoveryTimeoutLE,
                    RequiredRecoveryHeartbeatsLE,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!infrastructure.Success)
        {
            PublishStatusLE(
                RecoveryMonitorStateLE.Degraded,
                infrastructure.Message,
                infrastructure.Message);

            return;
        }

        Interlocked.Increment(
            ref _recoverySuccessCount);

        PublishStatusLE(
            RecoveryMonitorStateLE.Recovered,
            "Dispositivo físico y transporte recuperados automáticamente.",
            null);
    }

    private async Task<bool> WaitForDeviceReturnLEAsync(
        string serial,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var waitCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waitCts.CancelAfter(timeout);

        try
        {
            // adb wait-for-device bloquea hasta el evento del servidor ADB.
            // No consulta periódicamente el estado.
            await _adb.ExecuteRawAsync(
                    new[]
                    {
                        "-s",
                        serial,
                        "wait-for-device"
                    },
                    waitCts.Token)
                .ConfigureAwait(false);

            // Una sola validación al despertar.
            return await IsAdbOnlineLEAsync(serial, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<bool> IsAdbOnlineLEAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _adb
                .IsDeviceOnlineAsync(serial, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private void PublishStatusLE(
        RecoveryMonitorStateLE state,
        string message,
        string? error)
    {
        string serial =
            _serial ??
            string.Empty;

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        RecoveryMonitorStatusLE? previous =
            _status;

        DateTimeOffset? lastHealthy =
            state ==
                RecoveryMonitorStateLE.Healthy
                ? now
                : previous?.LastHealthyAtUtc;

        DateTimeOffset? lastRecovery =
            state ==
                RecoveryMonitorStateLE.Recovered
                ? now
                : previous?.LastRecoveryAtUtc;

        var status =
            new RecoveryMonitorStatusLE(
                Serial:
                    serial,

                State:
                    state,

                Message:
                    message,

                CheckCount:
                    Interlocked.Read(
                        ref _checkCount),

                RecoveryAttemptCount:
                    Interlocked.Read(
                        ref _recoveryAttemptCount),

                RecoverySuccessCount:
                    Interlocked.Read(
                        ref _recoverySuccessCount),

                UpdatedAtUtc:
                    now,

                LastHealthyAtUtc:
                    lastHealthy,

                LastRecoveryAtUtc:
                    lastRecovery,

                LastError:
                    error);

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
            /*
             * Una UI nunca debe romper MonitorRecoveryLE.
             */
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

            _signalLE.Dispose();
            _lifecycleGate.Dispose();
        }
    }
}






