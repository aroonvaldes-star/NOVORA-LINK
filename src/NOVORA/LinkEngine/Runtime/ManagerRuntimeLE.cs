using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Device;
using NOVORA.LinkEngine.Network;
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
        CancellationToken cancellationToken = default,
        bool launchAndroidClient = true)
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

            engine.Network.SessionChangedLE +=
                Network_SessionChangedLE;

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

                RefreshSessionLE(
                    StateRuntimeLE.WaitingForAndroid,
                    "CONTROL listo. Esperando NOVORA-LINK Android.",
                    null);

                if (launchAndroidClient)
                {
                    var androidClient =
                        new ManagerAndroidClientLE();

                /*
                 * El token remoto queda opcional en esta etapa.
                 * ManagerAndroidClientLE siempre envía
                 * novora_autostart_link=true para que Android arranque
                 * su cliente de transporte automáticamente.
                 *
                 * Cuando el servidor RemoteNV exponga su token real a
                 * ManagerRuntimeLE, se pasa aquí sin cambiar el manager.
                 */
                ResultCoreLE androidReady =
                    await androidClient
                        .EnsureReadyAndLaunchAsync(
                            serial,
                            remoteSessionToken: null,
                            cancellationToken:
                                cancellationToken)
                        .ConfigureAwait(false);

                if (!androidReady.Success)
                {
                    return await FailStartLEAsync(
                            serial,
                            androidReady.Message)
                        .ConfigureAwait(false);
                }

                    RefreshSessionLE(
                        StateRuntimeLE.WaitingForAndroid,
                        "NOVORA-LINK Android lanzado. Esperando HELLO/ACK.",
                        null);
                }
                else
                {
                    /*
                     * Remote PrepareLink:
                     *
                     * Windows deja CONTROL/ADB reverse listo.
                     * La APK es dueña del permiso VPN y del arranque
                     * de VpnNetworkLE.
                     */
                    RefreshSessionLE(
                        StateRuntimeLE.WaitingForAndroid,
                        "CONTROL listo. Esperando inicio VPN desde NOVORA-LINK Android.",
                        null);
                }

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
    // NETWORK EVENT
    // ============================================================

    private void Network_SessionChangedLE(
        object? sender,
        SessionNetworkLE network)
    {
        SessionRuntimeLE? current = SessionLE;
        if (current is null ||
            !string.Equals(current.Serial, network.Serial, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (network.State == StateNetworkLE.Online && network.RelayRunning && network.DataReverseConfigured)
        {
            RefreshSessionLE(
                StateRuntimeLE.Running,
                network.Message,
                null);
            return;
        }

        RefreshSessionLE(
            StateRuntimeLE.Degraded,
            network.Message,
            network.LastError);
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

        SessionNetworkLE? network =
            engine.Network.GetSessionLE(
                current.Serial);

        StateRuntimeLE effectiveState =
            CalculateRuntimeStateLE(
                recovery,
                transport,
                network);

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

        bool networkHealthy =
            network is not null &&
            network.State == StateNetworkLE.Online &&
            network.RelayRunning &&
            network.DataReverseConfigured;

        bool sessionHealthy =
            deviceOnline &&
            monitorHealthy &&
            transportHealthy &&
            networkHealthy &&
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
        SessionTransportLE? transport,
        SessionNetworkLE? network)
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
            transport.SessionHealthy &&
            network is not null &&
            network.State == StateNetworkLE.Online &&
            network.RelayRunning &&
            network.DataReverseConfigured;

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

/// <summary>
/// Garantiza que el cliente Android de NOVORA esté disponible y lo inicia.
///
/// P0:
/// - usa el ADB empaquetado con NOVORA;
/// - instala/actualiza con adb install -r;
/// - evita reinstalar el mismo APK usando una huella SHA-256 por dispositivo;
/// - nunca fuerza downgrade;
/// - resuelve MAIN/LAUNCHER dinámicamente;
/// - inicia la app con novora_autostart_link=true;
/// - admite novora_remote_token cuando Runtime disponga del token real.
/// </summary>
public sealed class ManagerAndroidClientLE
{
    private const string PackageNameLE =
        "com.novora.linkengine";

    private const string AutoStartExtraLE =
        "novora_autostart_link";

    private const string RemoteTokenExtraLE =
        "novora_remote_token";

    private readonly string _adbPath;
    private readonly string _apkPath;
    private readonly string _cacheDirectory;

    public ManagerAndroidClientLE(
        string? baseDirectory = null)
    {
        string root =
            Path.GetFullPath(
                baseDirectory ??
                AppContext.BaseDirectory);

        _adbPath =
            Path.Combine(
                root,
                "Tools",
                "adb.exe");

        _apkPath =
            Path.Combine(
                root,
                "Tools",
                "Android",
                "NOVORA.LinkEngine.Android.apk");

        _cacheDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "NOVORA",
                "AndroidClient");
    }

    public async Task<ResultCoreLE> EnsureReadyAndLaunchAsync(
        string serial,
        string? remoteSessionToken = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return ResultCoreLE.Fail(
                "ANDROID CLIENT: serial ADB vacío.");
        }

        serial =
            serial.Trim();

        if (!File.Exists(_adbPath))
        {
            return ResultCoreLE.Fail(
                "ANDROID CLIENT: ADB MISSING - falta Tools\\adb.exe.");
        }

        if (!File.Exists(_apkPath))
        {
            return ResultCoreLE.Fail(
                "ANDROID CLIENT: APK MISSING - falta Tools\\Android\\NOVORA.LinkEngine.Android.apk.");
        }

        AdbCommandResultLE state =
            await RunAdbAsync(
                    new[]
                    {
                        "-s",
                        serial,
                        "get-state"
                    },
                    cancellationToken)
                .ConfigureAwait(false);

        if (state.ExitCode != 0 ||
            !string.Equals(
                state.StandardOutput.Trim(),
                "device",
                StringComparison.OrdinalIgnoreCase))
        {
            return ResultCoreLE.Fail(
                "ANDROID CLIENT: ADB OFFLINE - el dispositivo no está en estado device. " +
                BuildSafeDetailLE(
                    state,
                    remoteSessionToken));
        }

        string bundledHash =
            await ComputeSha256Async(
                    _apkPath,
                    cancellationToken)
                .ConfigureAwait(false);

        bool packageInstalled =
            await IsPackageInstalledAsync(
                    serial,
                    cancellationToken)
                .ConfigureAwait(false);

        string cachePath =
            GetCachePathLE(
                serial);

        string? cachedHash =
            await ReadCachedHashAsync(
                    cachePath,
                    cancellationToken)
                .ConfigureAwait(false);

        bool bundleAlreadyChecked =
            packageInstalled &&
            string.Equals(
                cachedHash,
                bundledHash,
                StringComparison.OrdinalIgnoreCase);

        if (!bundleAlreadyChecked)
        {
            AdbCommandResultLE install =
                await RunAdbAsync(
                        new[]
                        {
                            "-s",
                            serial,
                            "install",
                            "-r",
                            _apkPath
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

            string installText =
                install.CombinedOutput;

            bool downgradeBlocked =
                installText.Contains(
                    "INSTALL_FAILED_VERSION_DOWNGRADE",
                    StringComparison.OrdinalIgnoreCase);

            bool installed =
                install.ExitCode == 0 &&
                installText.Contains(
                    "Success",
                    StringComparison.OrdinalIgnoreCase);

            if (!installed &&
                !downgradeBlocked)
            {
                return ResultCoreLE.Fail(
                    "ANDROID CLIENT: INSTALL FAILED. " +
                    BuildSafeDetailLE(
                        install,
                        remoteSessionToken));
            }

            if (installed)
            {
                packageInstalled =
                    await IsPackageInstalledAsync(
                            serial,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (!packageInstalled)
                {
                    return ResultCoreLE.Fail(
                        "ANDROID CLIENT: INSTALL FAILED - ADB informó Success pero el paquete no aparece instalado.");
                }
            }
            else if (downgradeBlocked &&
                     !packageInstalled)
            {
                return ResultCoreLE.Fail(
                    "ANDROID CLIENT: INSTALL FAILED - Android rechazó un downgrade y no hay una instalación utilizable.");
            }

            // También cacheamos el hash si Android rechazó un downgrade:
            // significa que el dispositivo ya tiene una versión más nueva y
            // no queremos repetir el intento en cada arranque.
            await WriteCachedHashAsync(
                    cachePath,
                    bundledHash,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!packageInstalled)
        {
            packageInstalled =
                await IsPackageInstalledAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);
        }

        if (!packageInstalled)
        {
            return ResultCoreLE.Fail(
                "ANDROID CLIENT: PACKAGE QUERY FAILED - com.novora.linkengine no está instalado.");
        }

        string? launcher =
            await ResolveLauncherAsync(
                    serial,
                    cancellationToken)
                .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(launcher))
        {
            return ResultCoreLE.Fail(
                "ANDROID CLIENT: LAUNCHER NOT FOUND - Android no resolvió una Activity MAIN/LAUNCHER.");
        }

        ResultCoreLE launch =
            await LaunchAsync(
                    serial,
                    launcher,
                    remoteSessionToken,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!launch.Success)
        {
            return launch;
        }

        return ResultCoreLE.Ok(
            bundleAlreadyChecked
                ? "ANDROID CLIENT: READY - cliente Android lanzado."
                : "ANDROID CLIENT: READY - cliente Android instalado/actualizado y lanzado.");
    }

    private async Task<bool> IsPackageInstalledAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        AdbCommandResultLE result =
            await RunAdbAsync(
                    new[]
                    {
                        "-s",
                        serial,
                        "shell",
                        "pm",
                        "path",
                        PackageNameLE
                    },
                    cancellationToken)
                .ConfigureAwait(false);

        return result.ExitCode == 0 &&
               result.StandardOutput
                   .Split(
                       new[] { '\r', '\n' },
                       StringSplitOptions.RemoveEmptyEntries)
                   .Any(
                       line =>
                           line.TrimStart()
                               .StartsWith(
                                   "package:",
                                   StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> ResolveLauncherAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        AdbCommandResultLE result =
            await RunAdbAsync(
                    new[]
                    {
                        "-s",
                        serial,
                        "shell",
                        "cmd",
                        "package",
                        "resolve-activity",
                        "--brief",
                        "-a",
                        "android.intent.action.MAIN",
                        "-c",
                        "android.intent.category.LAUNCHER",
                        PackageNameLE
                    },
                    cancellationToken)
                .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        string[] lines =
            result.StandardOutput
                .Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

        for (int index = lines.Length - 1;
             index >= 0;
             index--)
        {
            string candidate =
                lines[index];

            if (Regex.IsMatch(
                    candidate,
                    "^[A-Za-z0-9._-]+/[A-Za-z0-9._$-]+$"))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<ResultCoreLE> LaunchAsync(
        string serial,
        string launcher,
        string? remoteSessionToken,
        CancellationToken cancellationToken)
    {
        var arguments =
            new System.Collections.Generic.List<string>
            {
                "-s",
                serial,
                "shell",
                "am",
                "start",
                "-W",
                "-a",
                "android.intent.action.MAIN",
                "-c",
                "android.intent.category.LAUNCHER",
                "-n",
                launcher,
                "--ez",
                AutoStartExtraLE,
                "true"
            };

        if (!string.IsNullOrWhiteSpace(
                remoteSessionToken))
        {
            arguments.Add(
                "--es");
            arguments.Add(
                RemoteTokenExtraLE);
            arguments.Add(
                remoteSessionToken);
        }

        AdbCommandResultLE result =
            await RunAdbAsync(
                    arguments.ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);

        string output =
            result.CombinedOutput;

        bool rejected =
            result.ExitCode != 0 ||
            output.Contains(
                "Error type 3",
                StringComparison.OrdinalIgnoreCase) ||
            output.Contains(
                "does not exist",
                StringComparison.OrdinalIgnoreCase) ||
            output.Contains(
                "unable to resolve Intent",
                StringComparison.OrdinalIgnoreCase) ||
            output.Contains(
                "SecurityException",
                StringComparison.OrdinalIgnoreCase);

        if (rejected)
        {
            return ResultCoreLE.Fail(
                "ANDROID CLIENT: LAUNCH FAILED. " +
                BuildSafeDetailLE(
                    result,
                    remoteSessionToken));
        }

        return ResultCoreLE.Ok(
            "ANDROID CLIENT: LAUNCHED.");
    }

    private async Task<AdbCommandResultLE> RunAdbAsync(
        string[] arguments,
        CancellationToken cancellationToken)
    {
        var startInfo =
            new ProcessStartInfo
            {
                FileName = _adbPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(
                argument);
        }

        using var process =
            new Process
            {
                StartInfo = startInfo
            };

        try
        {
            if (!process.Start())
            {
                return new AdbCommandResultLE(
                    -1,
                    string.Empty,
                    "No se pudo iniciar adb.exe.");
            }

            Task<string> stdoutTask =
                process.StandardOutput
                    .ReadToEndAsync();

            Task<string> stderrTask =
                process.StandardError
                    .ReadToEndAsync();

            try
            {
                await process
                    .WaitForExitAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(
                            entireProcessTree: true);
                    }
                }
                catch
                {
                }

                throw;
            }

            string stdout =
                await stdoutTask
                    .ConfigureAwait(false);

            string stderr =
                await stderrTask
                    .ConfigureAwait(false);

            return new AdbCommandResultLE(
                process.ExitCode,
                stdout,
                stderr);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AdbCommandResultLE(
                -1,
                string.Empty,
                ex.Message);
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream =
            new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);

        byte[] hash =
            await SHA256.HashDataAsync(
                    stream,
                    cancellationToken)
                .ConfigureAwait(false);

        return Convert.ToHexString(
            hash);
    }

    private string GetCachePathLE(
        string serial)
    {
        string safeSerial =
            string.Concat(
                serial.Select(
                    character =>
                        Path.GetInvalidFileNameChars()
                            .Contains(character)
                            ? '_'
                            : character));

        return Path.Combine(
            _cacheDirectory,
            safeSerial + ".sha256");
    }

    private static async Task<string?> ReadCachedHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return (
                await File.ReadAllTextAsync(
                        path,
                        cancellationToken)
                    .ConfigureAwait(false))
                .Trim();
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteCachedHashAsync(
        string path,
        string hash,
        CancellationToken cancellationToken)
    {
        string? directory =
            Path.GetDirectoryName(
                path);

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        await File.WriteAllTextAsync(
                path,
                hash,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string BuildSafeDetailLE(
        AdbCommandResultLE result,
        string? remoteSessionToken)
    {
        string detail =
            result.CombinedOutput.Trim();

        if (!string.IsNullOrWhiteSpace(
                remoteSessionToken))
        {
            detail =
                detail.Replace(
                    remoteSessionToken,
                    "[REDACTED]",
                    StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(
                detail))
        {
            detail =
                $"ADB exit code {result.ExitCode}.";
        }

        const int MaxDetailLengthLE =
            700;

        if (detail.Length >
            MaxDetailLengthLE)
        {
            detail =
                detail[..MaxDetailLengthLE] +
                "...";
        }

        return detail;
    }

    private sealed record AdbCommandResultLE(
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public string CombinedOutput =>
            string.IsNullOrWhiteSpace(
                StandardError)
                ? StandardOutput
                : string.IsNullOrWhiteSpace(
                    StandardOutput)
                    ? StandardError
                    : StandardOutput +
                      Environment.NewLine +
                      StandardError;
    }
}
