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
            return "La conexión está lista para usar Internet USB.";
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
            _linkEngineRuntimeLE?.IsRunningLE == true;

        LinkEngineTestButton.IsEnabled =
            !_closing;

        LinkEngineTestButton.Content =
            running
                ? "VER ESTADO DE INTERNET USB"
                : "CÓMO ACTIVAR INTERNET USB";

        LinkEngineTestButton.ToolTip =
            running
                ? "Internet USB está activo; consulta aquí su estado."
                : "Internet USB se activa desde NOVORA-LINK Android después de autorizar USB y aceptar el permiso VPN.";
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
            "Conecta el teléfono y activa Internet USB desde NOVORA-LINK Android.";

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
