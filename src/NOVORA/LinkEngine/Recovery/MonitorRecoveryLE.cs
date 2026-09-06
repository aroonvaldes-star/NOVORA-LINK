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

public sealed class MonitorRecoveryLE :
    IAsyncDisposable
{
    private static readonly TimeSpan MonitorIntervalLE =
        TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan SocketRecoveryTimeoutLE =
        TimeSpan.FromSeconds(15);

    private static readonly TimeSpan InfrastructureRecoveryTimeoutLE =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan DeviceReturnTimeoutLE =
        TimeSpan.FromSeconds(60);

    private const long RequiredRecoveryHeartbeatsLE =
        3;

    private readonly ManagerDeviceLE _device;
    private readonly ManagerTransportLE _transport;
    private readonly ManagerRecoveryLE _recovery;
    private readonly AdbService _adb;

    private readonly SemaphoreSlim _lifecycleGate =
        new(1, 1);

    private CancellationTokenSource? _lifetimeCts;
    private Task? _workerTask;

    private string? _serial;

    private long _checkCount;
    private long _recoveryAttemptCount;
    private long _recoverySuccessCount;

    private RecoveryMonitorStatusLE? _status;

    private bool _disposed;

    public MonitorRecoveryLE(
        ManagerDeviceLE device,
        ManagerTransportLE transport,
        ManagerRecoveryLE recovery,
        AdbService adb)
    {
        _device =
            device ??
            throw new ArgumentNullException(nameof(device));

        _transport =
            transport ??
            throw new ArgumentNullException(nameof(transport));

        _recovery =
            recovery ??
            throw new ArgumentNullException(nameof(recovery));

        _adb =
            adb ??
            throw new ArgumentNullException(nameof(adb));
    }

    public event EventHandler<RecoveryMonitorStatusLE>?
        StatusChangedLE;

    public RecoveryMonitorStatusLE? StatusLE =>
        _status;

    public bool IsRunningLE =>
        _workerTask is
        {
            IsCompleted: false
        };

    public long RecoverySuccessCountLE =>
        Interlocked.Read(
            ref _recoverySuccessCount);

    public async Task StartAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException(
                "El serial es obligatorio.",
                nameof(serial));
        }

        serial =
            serial.Trim();

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
                if (string.Equals(
                        _serial,
                        serial,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                throw new InvalidOperationException(
                    "MonitorRecoveryLE ya vigila otro dispositivo.");
            }

            _serial =
                serial;

            _checkCount =
                0;

            _recoveryAttemptCount =
                0;

            _recoverySuccessCount =
                0;

            _lifetimeCts?.Dispose();

            _lifetimeCts =
                new CancellationTokenSource();

            PublishStatusLE(
                RecoveryMonitorStateLE.Starting,
                "Iniciando MonitorRecoveryLE.",
                null);

            _workerTask =
                Task.Run(
                    () =>
                        RunMonitorLEAsync(
                            serial,
                            _lifetimeCts.Token),
                    CancellationToken.None);
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
            PublishStatusLE(
                RecoveryMonitorStateLE.Stopping,
                "Deteniendo MonitorRecoveryLE.",
                null);

            CancellationTokenSource? cts =
                _lifetimeCts;

            Task? worker =
                _workerTask;

            cts?.Cancel();

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
                }
                catch (TimeoutException)
                {
                }
                catch
                {
                }
            }

            _workerTask =
                null;

            _lifetimeCts?.Dispose();

            _lifetimeCts =
                null;

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

    private async Task RunMonitorLEAsync(
    string serial,
    CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Interlocked.Increment(
                    ref _checkCount);

                /*
                 * ====================================================
                 * 1. COMPROBAR ADB / DISPOSITIVO
                 * ====================================================
                 */

                bool adbOnline =
                    await IsAdbOnlineLEAsync(
                            serial,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (!adbOnline)
                {
                    PublishStatusLE(
                        RecoveryMonitorStateLE.WaitingForDevice,
                        "ADB/device perdido. Esperando el regreso del dispositivo.",
                        null);

                    bool returned =
                        await WaitForDeviceReturnLEAsync(
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

                        await DelayMonitorLEAsync(
                                cancellationToken)
                            .ConfigureAwait(false);

                        continue;
                    }

                    /*
                     * El dispositivo volvió.
                     *
                     * Reconstruimos la infraestructura necesaria:
                     *
                     * ADB
                     *   ↓
                     * reverse
                     *   ↓
                     * listener
                     *   ↓
                     * CONTROL
                     *   ↓
                     * DATA
                     */

                    await RecoverAfterDeviceReturnLEAsync(
                            serial,
                            cancellationToken)
                        .ConfigureAwait(false);

                    continue;
                }

                /*
                 * ====================================================
                 * 2. OBTENER SESIÓN ACTUAL
                 * ====================================================
                 */

                SessionTransportLE? session =
                    _transport.GetSessionLE(
                        serial);

                if (session is null)
                {
                    await RecoverInfrastructureLEAsync(
                            serial,
                            "SessionTransportLE no existe.",
                            cancellationToken)
                        .ConfigureAwait(false);

                    continue;
                }

                /*
                 * ====================================================
                 * 3. VERIFICAR INFRAESTRUCTURA
                 * ====================================================
                 *
                 * VerifyAsync comprueba el estado real del transporte,
                 * especialmente adb reverse.
                 */

                ResultCoreLE verify =
                    await _transport
                        .VerifyAsync(
                            serial,
                            cancellationToken)
                        .ConfigureAwait(false);

                /*
                 * VerifyAsync puede reconstruir, reemplazar o eliminar
                 * la sesión.
                 *
                 * Por eso NO utilizamos ciegamente la referencia
                 * obtenida antes de VerifyAsync.
                 *
                 * Volvemos a consultar ManagerTransportLE.
                 */

                session =
                    _transport.GetSessionLE(
                        serial);

                /*
                 * ====================================================
                 * 4. GUARD CLAUSE NULLABLE
                 * ====================================================
                 *
                 * IMPORTANTE:
                 *
                 * Este guard explícito elimina CS8602.
                 *
                 * No usamos:
                 *
                 * session!
                 *
                 * porque queremos comprobar realmente que la sesión
                 * exista en runtime, no solamente silenciar al
                 * compilador.
                 */

                if (session is null)
                {
                    await RecoverInfrastructureLEAsync(
                            serial,
                            !string.IsNullOrWhiteSpace(
                                verify.Message)
                                ? verify.Message
                                : "SessionTransportLE desapareció después de VerifyAsync.",
                            cancellationToken)
                        .ConfigureAwait(false);

                    continue;
                }

                /*
                 * A partir de este punto el compilador y el runtime
                 * saben que session NO es null.
                 */

                bool listenerActive =
                    _transport.IsListenerActiveLE(
                        serial);

                /*
                 * ====================================================
                 * 5. ESTADO DE INFRAESTRUCTURA
                 * ====================================================
                 */

                bool infrastructureBroken =
                    !verify.Success ||
                    !session.ReverseConfigured ||
                    !session.ReverseVerified ||
                    !session.ListenerStarted ||
                    !listenerActive;

                if (infrastructureBroken)
                {
                    string infrastructureError =
                        !string.IsNullOrWhiteSpace(
                            session.LastError)
                            ? session.LastError
                            : verify.Message;

                    if (string.IsNullOrWhiteSpace(
                            infrastructureError))
                    {
                        infrastructureError =
                            "Infraestructura de transporte degradada.";
                    }

                    await RecoverInfrastructureLEAsync(
                            serial,
                            infrastructureError,
                            cancellationToken)
                        .ConfigureAwait(false);

                    continue;
                }

                /*
                 * ====================================================
                 * 6. CANAL LÓGICO / SOCKET
                 * ====================================================
                 *
                 * La infraestructura puede seguir existiendo:
                 *
                 * adb reverse     OK
                 * listener        OK
                 *
                 * pero el canal lógico Android <-> Windows puede
                 * haberse degradado.
                 *
                 * Revisamos:
                 *
                 * - State
                 * - ClientConnected
                 * - HandshakeVerified
                 * - SessionHealthy
                 */

                bool socketBroken =
                    session.State !=
                        StateTransportLE.Connected ||
                    !session.ClientConnected ||
                    !session.HandshakeVerified ||
                    !session.SessionHealthy;

                if (socketBroken)
                {
                    await RecoverSocketLEAsync(
                            serial,
                            session,
                            cancellationToken)
                        .ConfigureAwait(false);

                    continue;
                }

                /*
                 * ====================================================
                 * 7. HEALTHY
                 * ====================================================
                 */

                PublishStatusLE(
                    RecoveryMonitorStateLE.Healthy,
                    $"HEALTHY · heartbeat #{session.HeartbeatSequence}.",
                    null);
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

            /*
             * ========================================================
             * 8. INTERVALO DEL MONITOR
             * ========================================================
             */

            await DelayMonitorLEAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
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
             * esperábamos el socket.
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
        DateTimeOffset deadline =
            DateTimeOffset.UtcNow +
            timeout;

        int consecutiveOnline =
            0;

        const int requiredConsecutiveOnline =
            3;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            bool online =
                await IsAdbOnlineLEAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (online)
            {
                consecutiveOnline++;

                if (consecutiveOnline >=
                    requiredConsecutiveOnline)
                {
                    return true;
                }
            }
            else
            {
                consecutiveOnline =
                    0;
            }

            await Task.Delay(
                    MonitorIntervalLE,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return false;
    }

    private async Task<bool> IsAdbOnlineLEAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _adb
                .IsDeviceOnlineAsync(
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
            return false;
        }
    }

    private static async Task DelayMonitorLEAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(
                    MonitorIntervalLE,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
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

            _lifecycleGate.Dispose();
        }
    }
}
