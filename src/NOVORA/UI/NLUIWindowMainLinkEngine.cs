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

        Closed += (_, _) =>
        {
            StopLinkLiveMonitor();
            _linkMeasurements.End();
            _linkSpeedTestServer?.Dispose();
        };

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
            $"Conexión: {connection}";

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
        var summary = NOVORA.LinkEngine.Metrics.LESessionPresentation.FromRuntime(session);
        LinkEngineFriendlyConnectionStatus.Text = summary.State;
        LinkEngineFriendlyPhoneStatus.Text = summary.Phone;
        LinkEngineFriendlyUsbStatus.Text = summary.Link;
        LinkEngineFriendlyRecoveryStatus.Text = summary.Recovery;
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
            session.SessionHealthy && session.ClientConnected && session.HandshakeVerified)
        {
            return "La sesión de LinkEngine está conectada.";
        }

        if (session.State ==
            LERuntimeState.Degraded)
        {
            return "La conexión se interrumpió; NOVORA está intentando recuperarla.";
        }

        return session.State switch
        {
            LERuntimeState.Starting =>
                "Preparando la conexión...",

            LERuntimeState.ConnectingDevice =>
                "Buscando el teléfono...",

            LERuntimeState.OpeningTransport =>
                "Preparando la comunicación con el teléfono...",

            LERuntimeState.WaitingForAndroid =>
                "Esperando respuesta del teléfono...",

            LERuntimeState.StartingRecoveryMonitor =>
                "Comprobando que la conexión siga activa...",

            LERuntimeState.Stopping =>
                "Cerrando la conexión...",

            LERuntimeState.Failed =>
                "No se pudo conectar. Revisa el cable o la conexión Wi-Fi y vuelve a intentarlo.",

            LERuntimeState.Stopped =>
                "Conecta el teléfono por USB o prepara una conexión Wi-Fi.",

            _ =>
                "Preparando la conexión con el teléfono."
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
        bool running =
            _linkEngineRuntimeLE?.IsRunningLE == true || _androidLanDataGateway?.IsConnected == true;

        LinkEngineTestButton.IsEnabled =
            !_closing;

        LinkEngineTestButton.Content =
            running
                ? "VER ESTADO DE LINKENGINE"
                : "VER PASOS PARA ACTIVAR INTERNET";

        LinkEngineTestButton.ToolTip =
            running
                ? "Muestra el estado actual de LinkEngine en PC."
                : "Muestra los pasos para iniciar LinkEngine desde LINK CONTROL por USB o LAN.";
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
            "ADB: - - Conexión: -";

        LinkEngineTransportStatus.Text =
            "Puerto: - - Reverse: - - Listener: -";

        LinkEngineClientStatus.Text =
            "Android: - - Handshake: -";

        LinkEngineProtocolStatus.Text =
            "NOVORA-LINK/1 - Client ID: - - Heartbeat: 0";

        LinkEngineResultStatus.Text =
            "Conecta el teléfono por USB o LAN y activa LinkEngine desde AppControl.";

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
            "No se pudo conectar. Revisa el cable, las autorizaciones y vuelve a intentarlo.";

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
        StopLinkLiveMonitor();
        _linkMeasurements.End();
        _linkSpeedTestServer?.Invalidate();
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
