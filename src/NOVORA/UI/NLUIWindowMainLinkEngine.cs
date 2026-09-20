using System;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Device;
using NOVORA.LinkEngine.Recovery;
using NOVORA.LinkEngine.Runtime;
using NOVORA.LinkEngine.Transport;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private LERuntimeManager? _linkEngineRuntimeLE;

    /*
     * ============================================================
     * INITIALIZE
     * ============================================================
     */

    private void InitializeLinkEngineRuntimeLE()
    {
        if (_linkEngineRuntimeLE is not null)
        {
            return;
        }

        var runtime =
            new LERuntimeManager(
                _adb);

        runtime.StatusChangedLE +=
            LinkEngineRuntime_StatusChangedLE;

        _linkEngineRuntimeLE =
            runtime;

        ApplyLinkEngineStoppedUiLE();
    }

    /*
     * ============================================================
     * RUNTIME STATUS EVENT
     * ============================================================
     */

    private void LinkEngineRuntime_StatusChangedLE(
        object? sender,
        LERuntimeSession session)
    {
        if (_closing)
        {
            return;
        }

        try
        {
            RefreshSTEngineSnapshot14();

            if (Dispatcher.CheckAccess())
            {
                ApplyLinkEngineRuntimeSessionLE(
                    session);

                return;
            }

            _ =
                Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (!_closing)
                        {
                            ApplyLinkEngineRuntimeSessionLE(
                                session);
                        }
                    });
        }
        catch
        {
            /*
             * La UI nunca debe romper LERuntimeManager.
             */
        }
    }

    /*
     * ============================================================
     * APPLY LIVE SESSION
     * ============================================================
     */

    private void ApplyLinkEngineRuntimeSessionLE(
        LERuntimeSession session)
    {
        if (_closing)
        {
            return;
        }

        string connection =
            session.ConnectionType switch
            {
                LEDeviceConnection.Usb =>
                    "USB",

                LEDeviceConnection.Wifi =>
                    "WIFI",

                _ =>
                    "UNKNOWN"
            };

        LinkEngineDeviceStatus.Text =
            $"ADB: {(session.DeviceOnline ? "ONLINE" : "OFFLINE")} - " +
            $"Conexion: {connection}";

        string reverse =
            session.ReverseVerified
                ? "VERIFIED"
                : session.ReverseConfigured
                    ? "CREATED"
                    : "DOWN";

        string listener =
            session.ListenerStarted
                ? "LISTENING"
                : "STOPPED";

        string port =
            GetRuntimePortLE(
                session);

        LinkEngineTransportStatus.Text =
            $"Puerto: {port} - Reverse: {reverse} - " +
            $"Listener: {listener}";

        string client =
            session.ClientConnected
                ? "CONNECTED"
                : "WAITING";

        string handshake =
            session.HandshakeVerified
                ? "VERIFIED"
                : "WAITING";

        LinkEngineClientStatus.Text =
            $"Android: {client} - Handshake: {handshake}";

        string clientId =
            string.IsNullOrWhiteSpace(
                session.ClientId)
                ? "-"
                : session.ClientId;

        LinkEngineProtocolStatus.Text =
            $"NOVORA-LINK/1 - Client ID: {clientId} - " +
            $"Heartbeat: {session.HeartbeatCount}";

        LinkEngineResultStatus.Text =
            BuildRuntimeResultTextLE(
                session);

        ApplyFriendlyLinkEngineStatusLE(
            session);

        UpdateLinkEngineButtonLE();
        AndroidEngineStateChanged();
    }

    /*
     * ============================================================
     * FRIENDLY MAINWINDOW STATUS
     * ============================================================
     */

    private void ApplyFriendlyLinkEngineStatusLE(
        LERuntimeSession session)
    {
        LinkEngineFriendlyConnectionStatus.Text =
            session.State switch
            {
                LERuntimeState.Running
                    when session.SessionHealthy =>
                        "Activa",

                LERuntimeState.Degraded =>
                    "Reconectando...",

                LERuntimeState.Failed =>
                    "No se pudo conectar",

                LERuntimeState.Stopped =>
                    "Detenida",

                LERuntimeState.Stopping =>
                    "Deteniendo...",

                _ =>
                    "Conectando..."
            };

        LinkEngineFriendlyPhoneStatus.Text =
            session.DeviceOnline
                ? "Conectado"
                : "Sin comunicación";

        bool usbReady =
            session.DeviceOnline &&
            session.ReverseVerified &&
            session.ListenerStarted &&
            session.ClientConnected &&
            session.HandshakeVerified;

        LinkEngineFriendlyUsbStatus.Text =
            usbReady
                ? "Activo"
                : session.DeviceOnline &&
                  session.State is not
                      LERuntimeState.Failed and not
                      LERuntimeState.Stopped
                    ? "Preparando..."
                    : "No disponible";

        LinkEngineFriendlyRecoveryStatus.Text =
            session.RecoveryState switch
            {
                LERecoveryMonitorState.RecoveringSocket =>
                    "Reconectando...",

                LERecoveryMonitorState.RecoveringInfrastructure =>
                    "Reconectando...",

                LERecoveryMonitorState.WaitingForDevice =>
                    "Esperando teléfono",

                LERecoveryMonitorState.Failed =>
                    "Necesita atención",

                LERecoveryMonitorState.Degraded =>
                    "Revisando...",

                _ =>
                    "En espera"
            };
    }

    /*
     * ============================================================
     * PORT
     * ============================================================
     */

    private string GetRuntimePortLE(
        LERuntimeSession session)
    {
        LERuntimeManager? runtime =
            _linkEngineRuntimeLE;

        LETransportSession? transport =
            runtime?
                .EngineLE?
                .Transport
                .GetSessionLE(
                    session.Serial);

        if (transport is null ||
            transport.DevicePort <= 0)
        {
            return "-";
        }

        return transport
            .DevicePort
            .ToString();
    }

    /*
     * ============================================================
     * RESULT TEXT
     * ============================================================
     */

    private static string BuildRuntimeResultTextLE(
        LERuntimeSession session)
    {
        if (session.State ==
                LERuntimeState.Running &&
            session.SessionHealthy)
        {
            return
                $"HEALTHY - HEARTBEAT #{session.HeartbeatSequence} - " +
                $"RECOVERIES {session.RecoverySuccessCount}";
        }

        if (session.State ==
            LERuntimeState.Degraded)
        {
            return
                $"RECOVERING - " +
                $"{FormatRecoveryStateLE(session.RecoveryState)} - " +
                session.Message;
        }

        return session.State switch
        {
            LERuntimeState.Starting =>
                "STARTING - inicializando LinkEngine...",

            LERuntimeState.ConnectingDevice =>
                "STARTING - conectando LEDeviceManager...",

            LERuntimeState.OpeningTransport =>
                "STARTING - abriendo LETransportManager...",

            LERuntimeState.WaitingForAndroid =>
                "WAITING - esperando Android HELLO/ACK...",

            LERuntimeState.StartingRecoveryMonitor =>
                "STARTING - iniciando LERecoveryMonitor...",

            LERuntimeState.Stopping =>
                "STOPPING - cerrando LERuntimeManager...",

            LERuntimeState.Failed =>
                $"FAILED - {session.LastError ?? session.Message}",

            LERuntimeState.Stopped =>
                "STOPPED - LinkEngine Runtime detenido.",

            _ =>
                $"{session.State.ToString().ToUpperInvariant()} - " +
                session.Message
        };
    }

    /*
     * ============================================================
     * RECOVERY STATE
     * ============================================================
     */

    private static string FormatRecoveryStateLE(
        LERecoveryMonitorState state)
    {
        return state switch
        {
            LERecoveryMonitorState.WaitingForDevice =>
                "WAITING DEVICE",

            LERecoveryMonitorState.RecoveringSocket =>
                "SOCKET",

            LERecoveryMonitorState.RecoveringInfrastructure =>
                "INFRASTRUCTURE",

            LERecoveryMonitorState.Recovered =>
                "RECOVERED",

            LERecoveryMonitorState.Healthy =>
                "HEALTHY",

            LERecoveryMonitorState.Failed =>
                "FAILED",

            LERecoveryMonitorState.Degraded =>
                "DEGRADED",

            _ =>
                state
                    .ToString()
                    .ToUpperInvariant()
        };
    }

    /*
     * ============================================================
     * BUTTON
     * ============================================================
     */

    private void UpdateLinkEngineButtonLE()
    {
        LinkEngineTestButton.IsEnabled = false;
        LinkEngineTestButton.Content = _linkEngineRuntimeLE?.IsRunningLE == true
            ? "LINKENGINE ACTIVO" : "INICIAR DESDE ANDROID USB";
        LinkEngineTestButton.ToolTip = "Inicia Internet USB desde NOVORA Android después de autorizar el control USB y el permiso VPN del teléfono.";
    }

    /*
     * ============================================================
     * STOPPED UI
     * ============================================================
     */

    private void ApplyLinkEngineStoppedUiLE()
    {
        if (_closing)
        {
            return;
        }

        LinkEngineDeviceStatus.Text =
            "ADB: - - Conexion: -";

        LinkEngineTransportStatus.Text =
            "Puerto: - - Reverse: - - Listener: -";

        LinkEngineClientStatus.Text =
            "Android: - - Handshake: -";

        LinkEngineProtocolStatus.Text =
            "NOVORA-LINK/1 - Client ID: - - Heartbeat: 0";

        LinkEngineResultStatus.Text =
            "Inicia Internet USB desde NOVORA Android con el control USB autorizado.";

        LinkEngineFriendlyConnectionStatus.Text =
            "Detenida";

        LinkEngineFriendlyPhoneStatus.Text =
            _viewModel.Device?.Connected == true
                ? "Conectado"
                : "No detectado";

        LinkEngineFriendlyUsbStatus.Text =
            "No iniciado";

        LinkEngineFriendlyRecoveryStatus.Text =
            "En espera";

        UpdateLinkEngineButtonLE();
    }

    /*
     * ============================================================
     * FAILED UI
     * ============================================================
     */

    private void ApplyLinkEngineFailedUiLE(
        string message)
    {
        if (_closing)
        {
            return;
        }

        LinkEngineResultStatus.Text =
            $"FAILED - {message}";

        LinkEngineFriendlyConnectionStatus.Text =
            "No se pudo conectar";

        LinkEngineFriendlyUsbStatus.Text =
            "No disponible";

        LinkEngineFriendlyRecoveryStatus.Text =
            "En espera";

        UpdateLinkEngineButtonLE();
    }

    /*
     * ============================================================
     * MAINWINDOW SHUTDOWN
     * ============================================================
     */

    private async Task ShutdownLinkEngineRuntimeLEAsync()
    {
        LERuntimeManager? runtime =
            _linkEngineRuntimeLE;

        _linkEngineRuntimeLE =
            null;

        if (runtime is null)
        {
            return;
        }

        runtime.StatusChangedLE -=
            LinkEngineRuntime_StatusChangedLE;

        try
        {
            await runtime
                .DisposeAsync();
        }
        catch
        {
            /*
             * LinkEngine no debe impedir que NOVORA cierre.
             */
        }
    }
}
