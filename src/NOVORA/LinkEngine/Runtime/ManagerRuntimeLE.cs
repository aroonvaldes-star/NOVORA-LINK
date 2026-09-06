using System;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Device;
using NOVORA.LinkEngine.Recovery;
using NOVORA.LinkEngine.Transport;
using NOVORA.Services;

namespace NOVORA.LinkEngine.Runtime;

/// <summary>
/// Propietario persistente de LinkEngine.
///
/// LE-005D-FIX:
///
/// SessionRuntimeLE es la única fuente observable de verdad.
///
/// MonitorRecoveryLE tiene prioridad sobre snapshots antiguos
/// conservados por ManagerDeviceLE o ManagerTransportLE.
///
/// Esto evita estados imposibles como:
///
/// ADB realmente OFFLINE
/// +
/// UI mostrando ONLINE / CONNECTED / HEALTHY.
/// </summary>
public sealed class ManagerRuntimeLE :
    IAsyncDisposable
{
    private static readonly TimeSpan AndroidHandshakeTimeoutLE =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan InitialHealthTimeoutLE =
        TimeSpan.FromSeconds(15);

    private static readonly TimeSpan StatusRefreshIntervalLE =
        TimeSpan.FromMilliseconds(500);

    private const long RequiredInitialHeartbeatsLE =
        3;

    private readonly AdbService _adb;

    private readonly SemaphoreSlim _lifecycleGate =
        new(1, 1);

    /*
     * SessionRuntimeLE puede recibir actualizaciones desde:
     *
     * - worker de estado
     * - MonitorRecoveryLE
     * - StartAsync
     * - StopAsync
     *
     * Este gate evita snapshots construidos/publicados
     * simultáneamente.
     */
    private readonly object _sessionGate =
        new();

    private EngineCoreLE? _engine;

    private MonitorRecoveryLE? _recoveryMonitor;

    private CancellationTokenSource? _runtimeCts;

    private Task? _statusWorkerTask;

    private SessionRuntimeLE? _session;

    private bool _disposed;

    public ManagerRuntimeLE(
        AdbService adb)
    {
        _adb =
            adb ??
            throw new ArgumentNullException(
                nameof(adb));
    }

    public event EventHandler<SessionRuntimeLE>?
        StatusChangedLE;

    public SessionRuntimeLE? SessionLE
    {
        get
        {
            lock (_sessionGate)
            {
                return _session;
            }
        }
    }

    public bool IsRunningLE
    {
        get
        {
            SessionRuntimeLE? session =
                SessionLE;

            return
                session?.State ==
                    StateRuntimeLE.Running &&
                session.SessionHealthy &&
                _engine is not null &&
                _runtimeCts is
                {
                    IsCancellationRequested: false
                };
        }
    }

    public EngineCoreLE? EngineLE =>
        _engine;

    // ============================================================
    // START
    // ============================================================

    public async Task<ResultCoreLE> StartAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        if (string.IsNullOrWhiteSpace(serial))
        {
            return ResultCoreLE.Fail(
                "El serial del dispositivo es obligatorio.");
        }

        serial =
            serial.Trim();

        await _lifecycleGate
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (_engine is not null)
            {
                SessionRuntimeLE? current =
                    SessionLE;

                if (string.Equals(
                        current?.Serial,
                        serial,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return ResultCoreLE.Ok(
                        "LinkEngine Runtime ya está iniciado para este dispositivo.");
                }

                return ResultCoreLE.Fail(
                    "LinkEngine Runtime ya controla otro dispositivo.");
            }

            SetSessionLE(
                SessionRuntimeLE.CreateInitialLE(
                    serial) with
                {
                    State =
                        StateRuntimeLE.Starting,

                    StartedAtUtc =
                        DateTimeOffset.UtcNow,

                    UpdatedAtUtc =
                        DateTimeOffset.UtcNow,

                    Message =
                        "Iniciando LinkEngine Runtime.",

                    LastError =
                        null
                });

            _runtimeCts =
                new CancellationTokenSource();

            EngineCoreLE engine =
                EngineCoreLE.CreateDefault(
                    _adb);

            _engine =
                engine;

            try
            {
                // ====================================================
                // ENGINE
                // ====================================================

                ResultCoreLE initialization =
                    await engine
                        .InitializeAsync(
                            cancellationToken)
                        .ConfigureAwait(false);

                if (!initialization.Success)
                {
                    return await FailStartLEAsync(
                            serial,
                            initialization.Message)
                        .ConfigureAwait(false);
                }

                // ====================================================
                // DEVICE
                // ====================================================

                UpdateRuntimeStateLE(
                    StateRuntimeLE.ConnectingDevice,
                    "Conectando ManagerDeviceLE.");

                ResultCoreLE device =
                    await engine.Device
                        .ConnectAsync(
                            serial,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (!device.Success)
                {
                    return await FailStartLEAsync(
                            serial,
                            device.Message)
                        .ConfigureAwait(false);
                }

                SessionDeviceLE? deviceSession =
                    engine.Device.GetSession(
                        serial);

                if (deviceSession?.AdbOnline != true)
                {
                    return await FailStartLEAsync(
                            serial,
                            "ManagerDeviceLE no confirmó ADB ONLINE.")
                        .ConfigureAwait(false);
                }

                // ====================================================
                // RECOVERY
                // ====================================================

                await engine.Recovery
                    .RegisterAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

                // ====================================================
                // TRANSPORT
                // ====================================================

                UpdateRuntimeStateLE(
                    StateRuntimeLE.OpeningTransport,
                    "Abriendo ManagerTransportLE.");

                ResultCoreLE transport =
                    await engine.Transport
                        .OpenAsync(
                            serial,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (!transport.Success)
                {
                    return await FailStartLEAsync(
                            serial,
                            transport.Message)
                        .ConfigureAwait(false);
                }

                ResultCoreLE verification =
                    await engine.Transport
                        .VerifyAsync(
                            serial,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (!verification.Success)
                {
                    return await FailStartLEAsync(
                            serial,
                            verification.Message)
                        .ConfigureAwait(false);
                }

                // ====================================================
                // ANDROID CLIENT
                // ====================================================

                UpdateRuntimeStateLE(
                    StateRuntimeLE.WaitingForAndroid,
                    "Esperando cliente Android HELLO/ACK.");

                ResultCoreLE handshake =
                    await engine.Transport
                        .WaitForHandshakeAsync(
                            serial,
                            AndroidHandshakeTimeoutLE,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (!handshake.Success)
                {
                    return await FailStartLEAsync(
                            serial,
                            handshake.Message)
                        .ConfigureAwait(false);
                }

                /*
                 * =============================================
                 * LE-006 DATA PLANE START
                 * =============================================
                 *
                 * CONTROL HELLO/ACK ya fue verificado.
                 *
                 * Arrancamos el relay y adb reverse DATA 27184
                 * antes de esperar los heartbeats iniciales.
                 * =============================================
                 */

                ResultCoreLE internet =
                    await engine
                        .StartInternetAsync(
                            serial,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (!internet.Success)
                {
                    return await FailStartLEAsync(
                            serial,
                            internet.Message)
                        .ConfigureAwait(false);
                }

                SessionTransportLE? healthy =
                    await WaitForHealthyLEAsync(
                            engine.Transport,
                            serial,
                            RequiredInitialHeartbeatsLE,
                            InitialHealthTimeoutLE,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (healthy is null)
                {
                    return await FailStartLEAsync(
                            serial,
                            "LinkEngine Runtime no alcanzó HEALTHY inicial.")
                        .ConfigureAwait(false);
                }

                // ====================================================
                // RECOVERY MONITOR
                // ====================================================

                UpdateRuntimeStateLE(
                    StateRuntimeLE.StartingRecoveryMonitor,
                    "Iniciando MonitorRecoveryLE.");

                var monitor =
                    new MonitorRecoveryLE(
                        engine.Device,
                        engine.Transport,
                        engine.Recovery,
                        _adb);

                monitor.StatusChangedLE +=
                    RecoveryMonitor_StatusChangedLE;

                _recoveryMonitor =
                    monitor;

                await monitor
                    .StartAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

                // ====================================================
                // RUNNING
                // ====================================================

                RefreshSessionLE(
                    StateRuntimeLE.Running,
                    "LINKENGINE RUNTIME HEALTHY.",
                    null);

                CancellationToken runtimeToken =
                    _runtimeCts.Token;

                _statusWorkerTask =
                    Task.Run(
                        () =>
                            RunStatusWorkerLEAsync(
                                serial,
                                runtimeToken),
                        CancellationToken.None);

                return ResultCoreLE.Ok(
                    "LE-005 LinkEngine Runtime iniciado. " +
                    "ManagerDeviceLE, ManagerTransportLE y MonitorRecoveryLE permanecen activos.");
            }
            catch (OperationCanceledException)
            {
                await CleanupRuntimeLEAsync(
                        serial)
                    .ConfigureAwait(false);

                throw;
            }
            catch (Exception ex)
            {
                return await FailStartLEAsync(
                        serial,
                        $"ManagerRuntimeLE no pudo iniciar: {ex.Message}")
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    // ============================================================
    // STOP
    // ============================================================

    public async Task<ResultCoreLE> StopAsync(
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return ResultCoreLE.Ok(
                "ManagerRuntimeLE ya fue liberado.");
        }

        await _lifecycleGate
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (_engine is null)
            {
                SessionRuntimeLE? current =
                    SessionLE;

                if (current is not null)
                {
                    SetSessionLE(
                        BuildStoppedSessionLE(
                            current,
                            "LinkEngine Runtime detenido."));
                }

                return ResultCoreLE.Ok(
                    "LinkEngine Runtime ya estaba detenido.");
            }

            string serial =
                SessionLE?.Serial ??
                string.Empty;

            UpdateRuntimeStateLE(
                StateRuntimeLE.Stopping,
                "Deteniendo LinkEngine Runtime.");

            await CleanupRuntimeLEAsync(
                    serial)
                .ConfigureAwait(false);

            SessionRuntimeLE? stopped =
                SessionLE;

            if (stopped is not null)
            {
                SetSessionLE(
                    BuildStoppedSessionLE(
                        stopped,
                        "LinkEngine Runtime detenido correctamente."));
            }

            return ResultCoreLE.Ok(
                "LinkEngine Runtime detenido correctamente.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    // ============================================================
    // STATUS WORKER
    // ============================================================

    private async Task RunStatusWorkerLEAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                RecoveryMonitorStatusLE? recovery =
                    _recoveryMonitor?.StatusLE;

                SessionTransportLE? transport =
                    _engine?
                        .Transport
                        .GetSessionLE(
                            serial);

                StateRuntimeLE state =
                    CalculateRuntimeStateLE(
                        recovery,
                        transport);

                string message =
                    BuildRuntimeMessageLE(
                        state,
                        recovery,
                        transport);

                string? error =
                    recovery?.LastError ??
                    transport?.LastError;

                RefreshSessionLE(
                    state,
                    message,
                    error);
            }
            catch
            {
                /*
                 * El worker visual/observable jamás debe
                 * destruir el runtime.
                 */
            }

            try
            {
                await Task.Delay(
                        StatusRefreshIntervalLE,
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

    // ============================================================
    // RECOVERY EVENT
    // ============================================================

    private void RecoveryMonitor_StatusChangedLE(
        object? sender,
        RecoveryMonitorStatusLE status)
    {
        SessionRuntimeLE? current =
            SessionLE;

        if (current is null)
        {
            return;
        }

        /*
         * Este estado procede de una comprobación ADB REAL.
         *
         * Tiene prioridad absoluta sobre cualquier SessionDeviceLE
         * o SessionTransportLE todavía almacenado en memoria.
         */
        if (status.State ==
            RecoveryMonitorStateLE.WaitingForDevice)
        {
            ApplyDeviceOfflineSnapshotLE(
                status);

            return;
        }

        StateRuntimeLE runtimeState =
            status.State switch
            {
                RecoveryMonitorStateLE.Healthy =>
                    StateRuntimeLE.Running,

                RecoveryMonitorStateLE.Recovered =>
                    StateRuntimeLE.Running,

                RecoveryMonitorStateLE.Starting =>
                    StateRuntimeLE.StartingRecoveryMonitor,

                RecoveryMonitorStateLE.Stopped =>
                    _engine is null
                        ? StateRuntimeLE.Stopped
                        : current.State,

                RecoveryMonitorStateLE.Stopping =>
                    StateRuntimeLE.Stopping,

                RecoveryMonitorStateLE.Degraded =>
                    StateRuntimeLE.Degraded,

                RecoveryMonitorStateLE.RecoveringSocket =>
                    StateRuntimeLE.Degraded,

                RecoveryMonitorStateLE.RecoveringInfrastructure =>
                    StateRuntimeLE.Degraded,

                RecoveryMonitorStateLE.Failed =>
                    StateRuntimeLE.Degraded,

                _ =>
                    StateRuntimeLE.Degraded
            };

        RefreshSessionLE(
            runtimeState,
            status.Message,
            status.LastError);
    }

    // ============================================================
    // SINGLE SOURCE OF TRUTH
    // ============================================================

    private void RefreshSessionLE(
        StateRuntimeLE requestedState,
        string message,
        string? error)
    {
        EngineCoreLE? engine =
            _engine;

        SessionRuntimeLE? current =
            SessionLE;

        if (current is null ||
            engine is null)
        {
            return;
        }

        RecoveryMonitorStatusLE? recovery =
            _recoveryMonitor?.StatusLE;

        /*
         * MonitorRecoveryLE sabe que ADB está OFFLINE.
         *
         * No permitimos que ManagerDeviceLE/ManagerTransportLE antiguos
         * vuelvan a publicar HEALTHY.
         */
        if (recovery?.State ==
            RecoveryMonitorStateLE.WaitingForDevice)
        {
            ApplyDeviceOfflineSnapshotLE(
                recovery);

            return;
        }

        SessionDeviceLE? device =
            engine.Device.GetSession(
                current.Serial);

        SessionTransportLE? transport =
            engine.Transport.GetSessionLE(
                current.Serial);

        StateRuntimeLE effectiveState =
            CalculateRuntimeStateLE(
                recovery,
                transport);

        /*
         * Las fases explícitas del lifecycle tienen prioridad
         * durante START/STOP.
         */
        if (requestedState is
                StateRuntimeLE.Starting or
                StateRuntimeLE.ConnectingDevice or
                StateRuntimeLE.OpeningTransport or
                StateRuntimeLE.WaitingForAndroid or
                StateRuntimeLE.StartingRecoveryMonitor or
                StateRuntimeLE.Stopping or
                StateRuntimeLE.Failed or
                StateRuntimeLE.Stopped)
        {
            effectiveState =
                requestedState;
        }

        bool monitorHealthy =
            recovery is null ||
            recovery.State is
                RecoveryMonitorStateLE.Healthy or
                RecoveryMonitorStateLE.Recovered;

        bool deviceOnline =
            device?.AdbOnline == true;

        bool transportHealthy =
            transport is not null &&
            transport.State ==
                StateTransportLE.Connected &&
            transport.ReverseConfigured &&
            transport.ReverseVerified &&
            transport.ListenerStarted &&
            transport.ClientConnected &&
            transport.HandshakeVerified &&
            transport.SessionHealthy;

        bool sessionHealthy =
            deviceOnline &&
            monitorHealthy &&
            transportHealthy &&
            effectiveState ==
                StateRuntimeLE.Running;

        if (effectiveState ==
                StateRuntimeLE.Running &&
            !sessionHealthy)
        {
            effectiveState =
                StateRuntimeLE.Degraded;
        }

        SessionRuntimeLE next =
            current with
            {
                State =
                    effectiveState,

                DeviceOnline =
                    deviceOnline,

                ConnectionType =
                    device?.ConnectionType ??
                    current.ConnectionType,

                TransportOpen =
                    engine.Transport.IsOpen(
                        current.Serial),

                TransportState =
                    transport?.State ??
                    StateTransportLE.Closed,

                ReverseConfigured =
                    transport?.ReverseConfigured ??
                    false,

                ReverseVerified =
                    transport?.ReverseVerified ??
                    false,

                ListenerStarted =
                    transport?.ListenerStarted ??
                    false,

                ClientConnected =
                    transport?.ClientConnected ??
                    false,

                HandshakeVerified =
                    transport?.HandshakeVerified ??
                    false,

                SessionHealthy =
                    sessionHealthy,

                ClientId =
                    transport?.ClientId,

                HeartbeatSequence =
                    transport?.HeartbeatSequence ??
                    0,

                HeartbeatCount =
                    transport?.HeartbeatCount ??
                    0,

                RecoveryState =
                    recovery?.State ??
                    RecoveryMonitorStateLE.Stopped,

                RecoveryAttemptCount =
                    recovery?.RecoveryAttemptCount ??
                    0,

                RecoverySuccessCount =
                    recovery?.RecoverySuccessCount ??
                    0,

                ConnectedAtUtc =
                    transport?.ConnectedAtUtc,

                LastHeartbeatAtUtc =
                    transport?.LastHeartbeatAtUtc,

                LastRecoveryAtUtc =
                    recovery?.LastRecoveryAtUtc,

                UpdatedAtUtc =
                    DateTimeOffset.UtcNow,

                Message =
                    message,

                LastError =
                    error
            };

        SetSessionLE(
            next);
    }

    // ============================================================
    // DEVICE OFFLINE SNAPSHOT
    // ============================================================

    private void ApplyDeviceOfflineSnapshotLE(
        RecoveryMonitorStatusLE status)
    {
        SessionRuntimeLE? current =
            SessionLE;

        if (current is null)
        {
            return;
        }

        SessionRuntimeLE offline =
            current with
            {
                State =
                    StateRuntimeLE.Degraded,

                DeviceOnline =
                    false,

                /*
                 * Conservamos USB/WIFI como último transporte
                 * físico conocido, pero ONLINE queda en false.
                 */
                ConnectionType =
                    current.ConnectionType,

                TransportOpen =
                    false,

                TransportState =
                    StateTransportLE.Degraded,

                ReverseConfigured =
                    false,

                ReverseVerified =
                    false,

                ListenerStarted =
                    false,

                ClientConnected =
                    false,

                HandshakeVerified =
                    false,

                SessionHealthy =
                    false,

                ClientId =
                    null,

                HeartbeatSequence =
                    0,

                HeartbeatCount =
                    0,

                RecoveryState =
                    RecoveryMonitorStateLE.WaitingForDevice,

                RecoveryAttemptCount =
                    status.RecoveryAttemptCount,

                RecoverySuccessCount =
                    status.RecoverySuccessCount,

                ConnectedAtUtc =
                    null,

                LastHeartbeatAtUtc =
                    null,

                LastRecoveryAtUtc =
                    status.LastRecoveryAtUtc,

                UpdatedAtUtc =
                    DateTimeOffset.UtcNow,

                Message =
                    "ADB OFFLINE - WAITING DEVICE.",

                LastError =
                    status.LastError
            };

        SetSessionLE(
            offline);
    }

    // ============================================================
    // STATE CALCULATION
    // ============================================================

    private static StateRuntimeLE CalculateRuntimeStateLE(
        RecoveryMonitorStatusLE? recovery,
        SessionTransportLE? transport)
    {
        if (recovery is not null)
        {
            switch (recovery.State)
            {
                case RecoveryMonitorStateLE.WaitingForDevice:
                case RecoveryMonitorStateLE.Degraded:
                case RecoveryMonitorStateLE.RecoveringSocket:
                case RecoveryMonitorStateLE.RecoveringInfrastructure:
                case RecoveryMonitorStateLE.Failed:
                    return StateRuntimeLE.Degraded;

                case RecoveryMonitorStateLE.Starting:
                    return StateRuntimeLE.StartingRecoveryMonitor;

                case RecoveryMonitorStateLE.Stopping:
                    return StateRuntimeLE.Stopping;
            }
        }

        if (transport is null)
        {
            return StateRuntimeLE.Degraded;
        }

        bool healthy =
            transport.State ==
                StateTransportLE.Connected &&
            transport.ReverseConfigured &&
            transport.ReverseVerified &&
            transport.ListenerStarted &&
            transport.ClientConnected &&
            transport.HandshakeVerified &&
            transport.SessionHealthy;

        return healthy
            ? StateRuntimeLE.Running
            : StateRuntimeLE.Degraded;
    }

    private static string BuildRuntimeMessageLE(
        StateRuntimeLE state,
        RecoveryMonitorStatusLE? recovery,
        SessionTransportLE? transport)
    {
        if (recovery?.State ==
            RecoveryMonitorStateLE.WaitingForDevice)
        {
            return
                "ADB OFFLINE - WAITING DEVICE.";
        }

        if (state ==
            StateRuntimeLE.Running)
        {
            return
                "LINKENGINE RUNTIME HEALTHY.";
        }

        return
            recovery?.Message ??
            transport?.LastError ??
            "LinkEngine Runtime degradado.";
    }

    // ============================================================
    // LIFECYCLE STATE
    // ============================================================

    private void UpdateRuntimeStateLE(
        StateRuntimeLE state,
        string message)
    {
        SessionRuntimeLE? current =
            SessionLE;

        if (current is null)
        {
            return;
        }

        SetSessionLE(
            current with
            {
                State =
                    state,

                UpdatedAtUtc =
                    DateTimeOffset.UtcNow,

                Message =
                    message,

                LastError =
                    null
            });
    }

    // ============================================================
    // ATOMIC PUBLICATION
    // ============================================================

    private void SetSessionLE(
        SessionRuntimeLE session)
    {
        EventHandler<SessionRuntimeLE>? handler;

        lock (_sessionGate)
        {
            _session =
                session;

            handler =
                StatusChangedLE;
        }

        try
        {
            handler?.Invoke(
                this,
                session);
        }
        catch
        {
            /*
             * Una UI nunca debe romper ManagerRuntimeLE.
             */
        }
    }

    // ============================================================
    // FAILED START
    // ============================================================

    private async Task<ResultCoreLE> FailStartLEAsync(
        string serial,
        string message)
    {
        SessionRuntimeLE? current =
            SessionLE;

        if (current is not null)
        {
            SetSessionLE(
                current with
                {
                    State =
                        StateRuntimeLE.Failed,

                    DeviceOnline =
                        false,

                    TransportOpen =
                        false,

                    TransportState =
                        StateTransportLE.Failed,

                    ReverseConfigured =
                        false,

                    ReverseVerified =
                        false,

                    ListenerStarted =
                        false,

                    ClientConnected =
                        false,

                    HandshakeVerified =
                        false,

                    SessionHealthy =
                        false,

                    ClientId =
                        null,

                    HeartbeatSequence =
                        0,

                    HeartbeatCount =
                        0,

                    UpdatedAtUtc =
                        DateTimeOffset.UtcNow,

                    Message =
                        message,

                    LastError =
                        message
                });
        }

        await CleanupRuntimeLEAsync(
                serial)
            .ConfigureAwait(false);

        return ResultCoreLE.Fail(
            message);
    }

    // ============================================================
    // CLEANUP
    // ============================================================

    private async Task CleanupRuntimeLEAsync(
        string serial)
    {
        CancellationTokenSource? runtimeCts =
            _runtimeCts;

        _runtimeCts =
            null;

        if (runtimeCts is not null)
        {
            try
            {
                runtimeCts.Cancel();
            }
            catch
            {
            }
        }

        Task? statusWorker =
            _statusWorkerTask;

        _statusWorkerTask =
            null;

        if (statusWorker is not null)
        {
            try
            {
                await statusWorker
                    .WaitAsync(
                        TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        runtimeCts?.Dispose();

        MonitorRecoveryLE? monitor =
            _recoveryMonitor;

        _recoveryMonitor =
            null;

        if (monitor is not null)
        {
            monitor.StatusChangedLE -=
                RecoveryMonitor_StatusChangedLE;

            try
            {
                await monitor
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        EngineCoreLE? engine =
            _engine;

        _engine =
            null;

        if (engine is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(serial))
        {
            try
            {
                await engine.Recovery
                    .UnregisterAsync(
                        serial,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
            }

            /*
             * =============================================
             * LE-006 DATA PLANE STOP
             * =============================================
             *
             * DATA reverse must be removed while ADB is
             * still available.
             * =============================================
             */

            try
            {
                await engine
                    .StopInternetAsync(
                        serial,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await engine.Transport
                    .CloseAsync(
                        serial,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await engine.Device
                    .DisconnectAsync(
                        serial,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        try
        {
            await engine
                .DisposeAsync()
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    // ============================================================
    // INITIAL HEALTH
    // ============================================================

    private static async Task<SessionTransportLE?>
        WaitForHealthyLEAsync(
            ManagerTransportLE transport,
            string serial,
            long requiredHeartbeats,
            TimeSpan timeout,
            CancellationToken cancellationToken)
    {
        DateTimeOffset deadline =
            DateTimeOffset.UtcNow +
            timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            SessionTransportLE? session =
                transport.GetSessionLE(
                    serial);

            if (session is null)
            {
                return null;
            }

            if (session.State ==
                StateTransportLE.Failed)
            {
                return null;
            }

            if (session.State ==
                    StateTransportLE.Connected &&
                session.ReverseConfigured &&
                session.ReverseVerified &&
                session.ListenerStarted &&
                session.ClientConnected &&
                session.HandshakeVerified &&
                session.SessionHealthy &&
                session.HeartbeatCount >=
                    requiredHeartbeats)
            {
                return session;
            }

            await Task.Delay(
                    200,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return null;
    }

    // ============================================================
    // STOPPED SESSION
    // ============================================================

    private static SessionRuntimeLE BuildStoppedSessionLE(
        SessionRuntimeLE current,
        string message)
    {
        return current with
        {
            State =
                StateRuntimeLE.Stopped,

            DeviceOnline =
                false,

            ConnectionType =
                ConnectionDeviceLE.Unknown,

            TransportOpen =
                false,

            TransportState =
                StateTransportLE.Closed,

            ReverseConfigured =
                false,

            ReverseVerified =
                false,

            ListenerStarted =
                false,

            ClientConnected =
                false,

            HandshakeVerified =
                false,

            SessionHealthy =
                false,

            ClientId =
                null,

            HeartbeatSequence =
                0,

            HeartbeatCount =
                0,

            RecoveryState =
                RecoveryMonitorStateLE.Stopped,

            UpdatedAtUtc =
                DateTimeOffset.UtcNow,

            Message =
                message,

            LastError =
                null
        };
    }

    // ============================================================
    // DISPOSE
    // ============================================================

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
