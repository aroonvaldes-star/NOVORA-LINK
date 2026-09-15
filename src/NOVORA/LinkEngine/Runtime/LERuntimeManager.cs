using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Device;
using NOVORA.LinkEngine.Network;
using NOVORA.LinkEngine.Recovery;
using NOVORA.LinkEngine.Transport;
using NOVORA.Service;

namespace NOVORA.LinkEngine.Runtime;

/// <summary>
/// Propietario persistente de LinkEngine.
///
/// LE-005D-FIX:
///
/// LERuntimeSession es la única fuente observable de verdad.
///
/// LERecoveryMonitor tiene prioridad sobre snapshots antiguos
/// conservados por LEDeviceManager o LETransportManager.
///
/// Esto evita estados imposibles como:
///
/// ADB realmente OFFLINE
/// +
/// UI mostrando ONLINE / CONNECTED / HEALTHY.
/// </summary>
public sealed class LERuntimeManager :
    IAsyncDisposable
{
    private static readonly TimeSpan AndroidHandshakeTimeoutLE =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan InitialHealthTimeoutLE =
        TimeSpan.FromSeconds(15);

    private const long RequiredInitialHeartbeatsLE =
        3;

    private readonly NLServiceADB _adb;

    private readonly SemaphoreSlim _lifecycleGate =
        new(1, 1);

    /*
     * LERuntimeSession puede recibir actualizaciones desde:
     *
     * - worker de estado
     * - LERecoveryMonitor
     * - StartAsync
     * - StopAsync
     *
     * Este gate evita snapshots construidos/publicados
     * simultáneamente.
     */
    private readonly object _sessionGate =
        new();

    private LECoreEngine? _engine;

    private LERecoveryMonitor? _recoveryMonitor;

    private CancellationTokenSource? _runtimeCts;

    private LERuntimeSession? _session;

    private bool _disposed;

    public LERuntimeManager(
        NLServiceADB adb)
    {
        _adb =
            adb ??
            throw new ArgumentNullException(
                nameof(adb));
    }

    public event EventHandler<LERuntimeSession>?
        StatusChangedLE;

    public LERuntimeSession? SessionLE
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
            LERuntimeSession? session =
                SessionLE;

            return
                session?.State ==
                    LERuntimeState.Running &&
                session.SessionHealthy &&
                _engine is not null &&
                _runtimeCts is
                {
                    IsCancellationRequested: false
                };
        }
    }

    public LECoreEngine? EngineLE =>
        _engine;

    // ============================================================
    // START
    // ============================================================

    public async Task<LECoreResult> StartAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        if (string.IsNullOrWhiteSpace(serial))
        {
            return LECoreResult.Fail(
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
                LERuntimeSession? current =
                    SessionLE;

                if (string.Equals(
                        current?.Serial,
                        serial,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return LECoreResult.Ok(
                        "LinkEngine Runtime ya está iniciado para este dispositivo.");
                }

                return LECoreResult.Fail(
                    "LinkEngine Runtime ya controla otro dispositivo.");
            }

            SetSessionLE(
                LERuntimeSession.CreateInitialLE(
                    serial) with
                {
                    State =
                        LERuntimeState.Starting,

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

            LECoreEngine engine =
                LECoreEngine.CreateDefault(
                    _adb);

            _engine =
                engine;

            engine.Network.SessionChangedLE +=
                Network_SessionChangedLE;

            try
            {
                // ====================================================
                // ENGINE
                // ====================================================

                LECoreResult initialization =
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
                    LERuntimeState.ConnectingDevice,
                    "Conectando LEDeviceManager.");

                LECoreResult device =
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

                LEDeviceSession? deviceSession =
                    engine.Device.GetSession(
                        serial);

                if (deviceSession?.AdbOnline != true)
                {
                    return await FailStartLEAsync(
                            serial,
                            "LEDeviceManager no confirmó ADB ONLINE.")
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
                    LERuntimeState.OpeningTransport,
                    "Abriendo LETransportManager.");

                LECoreResult transport =
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

                LECoreResult verification =
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

                RefreshSessionLE(
                    LERuntimeState.WaitingForAndroid,
                    "CONTROL listo. Esperando NOVORA-LINK Android.",
                    null);

                LECoreResult handshake =
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

                LECoreResult internet =
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

                LETransportSession? healthy =
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
                    LERuntimeState.StartingRecoveryMonitor,
                    "Iniciando LERecoveryMonitor.");

                var monitor =
                    new LERecoveryMonitor(
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
                    LERuntimeState.Running,
                    "LINKENGINE RUNTIME HEALTHY.",
                    null);

                return LECoreResult.Ok(
                    "LE-005 LinkEngine Runtime iniciado. " +
                    "LEDeviceManager, LETransportManager y LERecoveryMonitor permanecen activos.");
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
                        $"LERuntimeManager no pudo iniciar: {ex.Message}")
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

    public async Task<LECoreResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return LECoreResult.Ok(
                "LERuntimeManager ya fue liberado.");
        }

        await _lifecycleGate
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (_engine is null)
            {
                LERuntimeSession? current =
                    SessionLE;

                if (current is not null)
                {
                    SetSessionLE(
                        BuildStoppedSessionLE(
                            current,
                            "LinkEngine Runtime detenido."));
                }

                return LECoreResult.Ok(
                    "LinkEngine Runtime ya estaba detenido.");
            }

            string serial =
                SessionLE?.Serial ??
                string.Empty;

            UpdateRuntimeStateLE(
                LERuntimeState.Stopping,
                "Deteniendo LinkEngine Runtime.");

            await CleanupRuntimeLEAsync(
                    serial)
                .ConfigureAwait(false);

            LERuntimeSession? stopped =
                SessionLE;

            if (stopped is not null)
            {
                SetSessionLE(
                    BuildStoppedSessionLE(
                        stopped,
                        "LinkEngine Runtime detenido correctamente."));
            }

            return LECoreResult.Ok(
                "LinkEngine Runtime detenido correctamente.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    // ============================================================
    // NETWORK EVENT
    // ============================================================

    private void Network_SessionChangedLE(
        object? sender,
        LENetworkSession network)
    {
        LERuntimeSession? current = SessionLE;
        if (current is null ||
            !string.Equals(current.Serial, network.Serial, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (network.State == LENetworkState.Online && network.RelayRunning && network.DataReverseConfigured)
        {
            RefreshSessionLE(
                LERuntimeState.Running,
                network.Message,
                null);
            return;
        }

        RefreshSessionLE(
            LERuntimeState.Degraded,
            network.Message,
            network.LastError);
    }

    // ============================================================
    // RECOVERY EVENT
    // ============================================================

    private void RecoveryMonitor_StatusChangedLE(
        object? sender,
        LERecoveryMonitorStatus status)
    {
        LERuntimeSession? current =
            SessionLE;

        if (current is null)
        {
            return;
        }

        /*
         * Este estado procede de una comprobación ADB REAL.
         *
         * Tiene prioridad absoluta sobre cualquier LEDeviceSession
         * o LETransportSession todavía almacenado en memoria.
         */
        if (status.State ==
            LERecoveryMonitorState.WaitingForDevice)
        {
            ApplyDeviceOfflineSnapshotLE(
                status);

            return;
        }

        LERuntimeState runtimeState =
            status.State switch
            {
                LERecoveryMonitorState.Healthy =>
                    LERuntimeState.Running,

                LERecoveryMonitorState.Recovered =>
                    LERuntimeState.Running,

                LERecoveryMonitorState.Starting =>
                    LERuntimeState.StartingRecoveryMonitor,

                LERecoveryMonitorState.Stopped =>
                    _engine is null
                        ? LERuntimeState.Stopped
                        : current.State,

                LERecoveryMonitorState.Stopping =>
                    LERuntimeState.Stopping,

                LERecoveryMonitorState.Degraded =>
                    LERuntimeState.Degraded,

                LERecoveryMonitorState.RecoveringSocket =>
                    LERuntimeState.Degraded,

                LERecoveryMonitorState.RecoveringInfrastructure =>
                    LERuntimeState.Degraded,

                LERecoveryMonitorState.Failed =>
                    LERuntimeState.Degraded,

                _ =>
                    LERuntimeState.Degraded
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
        LERuntimeState requestedState,
        string message,
        string? error)
    {
        LECoreEngine? engine =
            _engine;

        LERuntimeSession? current =
            SessionLE;

        if (current is null ||
            engine is null)
        {
            return;
        }

        LERecoveryMonitorStatus? recovery =
            _recoveryMonitor?.StatusLE;

        /*
         * LERecoveryMonitor sabe que ADB está OFFLINE.
         *
         * No permitimos que LEDeviceManager/LETransportManager antiguos
         * vuelvan a publicar HEALTHY.
         */
        if (recovery?.State ==
            LERecoveryMonitorState.WaitingForDevice)
        {
            ApplyDeviceOfflineSnapshotLE(
                recovery);

            return;
        }

        LEDeviceSession? device =
            engine.Device.GetSession(
                current.Serial);

        LETransportSession? transport =
            engine.Transport.GetSessionLE(
                current.Serial);

        LENetworkSession? network =
            engine.Network.GetSessionLE(
                current.Serial);

        LERuntimeState effectiveState =
            CalculateRuntimeStateLE(
                recovery,
                transport,
                network);

        /*
         * Las fases explícitas del lifecycle tienen prioridad
         * durante START/STOP.
         */
        if (requestedState is
                LERuntimeState.Starting or
                LERuntimeState.ConnectingDevice or
                LERuntimeState.OpeningTransport or
                LERuntimeState.WaitingForAndroid or
                LERuntimeState.StartingRecoveryMonitor or
                LERuntimeState.Stopping or
                LERuntimeState.Failed or
                LERuntimeState.Stopped)
        {
            effectiveState =
                requestedState;
        }

        bool monitorHealthy =
            recovery is null ||
            recovery.State is
                LERecoveryMonitorState.Healthy or
                LERecoveryMonitorState.Recovered;

        bool deviceOnline =
            device?.AdbOnline == true;

        bool transportHealthy =
            transport is not null &&
            transport.State ==
                LETransportState.Connected &&
            transport.ReverseConfigured &&
            transport.ReverseVerified &&
            transport.ListenerStarted &&
            transport.ClientConnected &&
            transport.HandshakeVerified &&
            transport.SessionHealthy;

        bool networkHealthy =
            network is not null &&
            network.State == LENetworkState.Online &&
            network.RelayRunning &&
            network.DataReverseConfigured;

        bool sessionHealthy =
            deviceOnline &&
            monitorHealthy &&
            transportHealthy &&
            networkHealthy &&
            effectiveState ==
                LERuntimeState.Running;

        if (effectiveState ==
                LERuntimeState.Running &&
            !sessionHealthy)
        {
            effectiveState =
                LERuntimeState.Degraded;
        }

        LERuntimeSession next =
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
                    LETransportState.Closed,

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
                    LERecoveryMonitorState.Stopped,

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
        LERecoveryMonitorStatus status)
    {
        LERuntimeSession? current =
            SessionLE;

        if (current is null)
        {
            return;
        }

        LERuntimeSession offline =
            current with
            {
                State =
                    LERuntimeState.Degraded,

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
                    LETransportState.Degraded,

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
                    LERecoveryMonitorState.WaitingForDevice,

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

    private static LERuntimeState CalculateRuntimeStateLE(
        LERecoveryMonitorStatus? recovery,
        LETransportSession? transport,
        LENetworkSession? network)
    {
        if (recovery is not null)
        {
            switch (recovery.State)
            {
                case LERecoveryMonitorState.WaitingForDevice:
                case LERecoveryMonitorState.Degraded:
                case LERecoveryMonitorState.RecoveringSocket:
                case LERecoveryMonitorState.RecoveringInfrastructure:
                case LERecoveryMonitorState.Failed:
                    return LERuntimeState.Degraded;

                case LERecoveryMonitorState.Starting:
                    return LERuntimeState.StartingRecoveryMonitor;

                case LERecoveryMonitorState.Stopping:
                    return LERuntimeState.Stopping;
            }
        }

        if (transport is null)
        {
            return LERuntimeState.Degraded;
        }

        bool healthy =
            transport.State ==
                LETransportState.Connected &&
            transport.ReverseConfigured &&
            transport.ReverseVerified &&
            transport.ListenerStarted &&
            transport.ClientConnected &&
            transport.HandshakeVerified &&
            transport.SessionHealthy &&
            network is not null &&
            network.State == LENetworkState.Online &&
            network.RelayRunning &&
            network.DataReverseConfigured;

        return healthy
            ? LERuntimeState.Running
            : LERuntimeState.Degraded;
    }

    private static string BuildRuntimeMessageLE(
        LERuntimeState state,
        LERecoveryMonitorStatus? recovery,
        LETransportSession? transport)
    {
        if (recovery?.State ==
            LERecoveryMonitorState.WaitingForDevice)
        {
            return
                "ADB OFFLINE - WAITING DEVICE.";
        }

        if (state ==
            LERuntimeState.Running)
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
        LERuntimeState state,
        string message)
    {
        LERuntimeSession? current =
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
        LERuntimeSession session)
    {
        EventHandler<LERuntimeSession>? handler;

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
             * Una UI nunca debe romper LERuntimeManager.
             */
        }
    }

    // ============================================================
    // FAILED START
    // ============================================================

    private async Task<LECoreResult> FailStartLEAsync(
        string serial,
        string message)
    {
        LERuntimeSession? current =
            SessionLE;

        if (current is not null)
        {
            SetSessionLE(
                current with
                {
                    State =
                        LERuntimeState.Failed,

                    DeviceOnline =
                        false,

                    TransportOpen =
                        false,

                    TransportState =
                        LETransportState.Failed,

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

        return LECoreResult.Fail(
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

        runtimeCts?.Dispose();

        LERecoveryMonitor? monitor =
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

        LECoreEngine? engine =
            _engine;

        _engine =
            null;

        if (engine is null)
        {
            return;
        }

        engine.Network.SessionChangedLE -=
            Network_SessionChangedLE;

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

    private static async Task<LETransportSession?>
        WaitForHealthyLEAsync(
            LETransportManager transport,
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

            LETransportSession? session =
                transport.GetSessionLE(
                    serial);

            if (session is null)
            {
                return null;
            }

            if (session.State ==
                LETransportState.Failed)
            {
                return null;
            }

            if (session.State ==
                    LETransportState.Connected &&
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

    private static LERuntimeSession BuildStoppedSessionLE(
        LERuntimeSession current,
        string message)
    {
        return current with
        {
            State =
                LERuntimeState.Stopped,

            DeviceOnline =
                false,

            ConnectionType =
                LEDeviceConnection.Unknown,

            TransportOpen =
                false,

            TransportState =
                LETransportState.Closed,

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
                LERecoveryMonitorState.Stopped,

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
