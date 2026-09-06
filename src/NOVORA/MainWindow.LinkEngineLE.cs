using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Device;
using NOVORA.LinkEngine.Recovery;
using NOVORA.LinkEngine.Runtime;
using NOVORA.LinkEngine.Transport;

namespace NOVORA;

public partial class MainWindow
{
    private ManagerRuntimeLE? _linkEngineRuntimeLE;

    private ProvisioningDeviceLE? _provisioningDeviceLE;

    private bool _linkEngineRuntimeOperationLE;

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

        _provisioningDeviceLE ??=

            new ProvisioningDeviceLE(

                _adb);


        var runtime =
            new ManagerRuntimeLE(
                _adb);

        runtime.StatusChangedLE +=
            LinkEngineRuntime_StatusChangedLE;

        _linkEngineRuntimeLE =
            runtime;

        ApplyLinkEngineStoppedUiLE();
    }

    /*
     * ============================================================
     * START / STOP BUTTON
     * ============================================================
     */

    private async void LinkEngineTest_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_closing ||
            _linkEngineRuntimeOperationLE)
        {
            return;
        }

        InitializeLinkEngineRuntimeLE();

        ManagerRuntimeLE? runtime =
            _linkEngineRuntimeLE;

        if (runtime is null)
        {
            return;
        }

        SessionRuntimeLE? current =
            runtime.SessionLE;

        bool runtimeActive =
            current is not null &&
            current.State is not
                StateRuntimeLE.Stopped and not
                StateRuntimeLE.Failed;

        if (runtimeActive)
        {
            await StopLinkEngineRuntimeFromUiLEAsync();

            return;
        }

        var device =
            _viewModel.Device;

        if (device is null ||
            !device.Connected ||
            string.IsNullOrWhiteSpace(
                device.Serial))
        {
            MessageBox.Show(
                "Selecciona primero un dispositivo Android conectado y autorizado por ADB.",
                "NOVORA - LinkEngine",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        _linkEngineRuntimeOperationLE =
            true;

        LinkEngineTestButton.IsEnabled =
            false;

        LinkEngineTestButton.Content =
            "STARTING...";

        LinkEngineDeviceStatus.Text =
            $"ADB: CONNECTING - Conexion: {device.ConnectionType}";

        LinkEngineTransportStatus.Text =
            "Puerto: - - Reverse: PREPARANDO - Listener: PREPARANDO";

        LinkEngineClientStatus.Text =
            "Android: WAITING - Handshake: WAITING";

        LinkEngineProtocolStatus.Text =
            "NOVORA-LINK/1 - Client ID: - - Heartbeat: 0";

        LinkEngineResultStatus.Text =
            "LE-006 - iniciando ManagerRuntimeLE...";

        try
        {
            ProvisioningDeviceLE provisioning =
                _provisioningDeviceLE
                ?? throw new InvalidOperationException(
                    "ProvisioningDeviceLE no fue inicializado.");

            LinkEngineResultStatus.Text =
                "Preparando LinkEngine Android...";

            ProvisioningResultLE provisioningResult =
                await provisioning
                    .EnsureInstalledAndLaunchAsync(
                        device.Serial);

            if (!provisioningResult.Success)
            {
                ApplyLinkEngineFailedUiLE(
                    provisioningResult.Message);

                MessageBox.Show(
                    provisioningResult.Message,
                    "NOVORA - LINKENGINE ANDROID",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var result =
                await runtime
                    .StartAsync(
                        device.Serial);

            if (!result.Success)
            {
                SessionRuntimeLE? failed =
                    runtime.SessionLE;

                if (failed is not null)
                {
                    ApplyLinkEngineRuntimeSessionLE(
                        failed);
                }
                else
                {
                    ApplyLinkEngineFailedUiLE(
                        result.Message);
                }

                MessageBox.Show(
                    result.Message,
                    "NOVORA - LINKENGINE START FAILED",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            SessionRuntimeLE? session =
                runtime.SessionLE;

            if (session is not null)
            {
                ApplyLinkEngineRuntimeSessionLE(
                    session);
            }
        }
        catch (OperationCanceledException)
        {
            ApplyLinkEngineFailedUiLE(
                "Inicio cancelado.");
        }
        catch (Exception ex)
        {
            ApplyLinkEngineFailedUiLE(
                ex.Message);

            MessageBox.Show(
                ex.Message,
                "NOVORA - LINKENGINE RUNTIME",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _linkEngineRuntimeOperationLE =
                false;

            LinkEngineTestButton.IsEnabled =
                !_closing;

            UpdateLinkEngineButtonLE();
        }
    }

    /*
     * ============================================================
     * STOP
     * ============================================================
     */

    private async Task StopLinkEngineRuntimeFromUiLEAsync()
    {
        ManagerRuntimeLE? runtime =
            _linkEngineRuntimeLE;

        if (runtime is null)
        {
            ApplyLinkEngineStoppedUiLE();

            return;
        }

        if (_linkEngineRuntimeOperationLE)
        {
            return;
        }

        _linkEngineRuntimeOperationLE =
            true;

        LinkEngineTestButton.IsEnabled =
            false;

        LinkEngineTestButton.Content =
            "STOPPING...";

        LinkEngineResultStatus.Text =
            "STOPPING - desmontando VPN Android y LinkEngine Runtime...";

        /*
         * Guardamos el serial ANTES de StopAsync(), porque el runtime
         * puede limpiar/modificar la sesión durante el apagado.
         */
        string serial =
            runtime.SessionLE?.Serial ??
            _viewModel.Device?.Serial ??
            string.Empty;

        try
        {
            /*
             * FIX LE-006 / A15:
             *
             * El runtime Windows y el VpnService Android son dos ciclos de
             * vida distintos. Hasta ahora STOP cerraba Windows pero podía
             * dejar VpnNetworkLE activo con 0.0.0.0/0 dentro del TUN.
             *
             * Primero pedimos a Android desmontar la VPN. Esto devuelve el
             * routing normal (Wi-Fi/datos) inmediatamente al teléfono.
             */
            ResultCoreLE? androidStopResult =
                null;

            if (!string.IsNullOrWhiteSpace(
                    serial))
            {
                ProvisioningDeviceLE provisioning =
                    _provisioningDeviceLE ??
                    new ProvisioningDeviceLE(
                        _adb);

                _provisioningDeviceLE =
                    provisioning;

                androidStopResult =
                    await provisioning
                        .StopVpnAsync(
                            serial);
            }

            var runtimeResult =
                await runtime
                    .StopAsync();

            if (!runtimeResult.Success)
            {
                ApplyLinkEngineFailedUiLE(
                    runtimeResult.Message);

                MessageBox.Show(
                    runtimeResult.Message,
                    "NOVORA - LINKENGINE STOP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (androidStopResult is
                { Success: false })
            {
                ApplyLinkEngineFailedUiLE(
                    androidStopResult.Message);

                MessageBox.Show(
                    "El runtime de Windows se detuvo, pero Android no confirmó " +
                    "el desmontaje de la VPN." +
                    Environment.NewLine +
                    Environment.NewLine +
                    androidStopResult.Message,
                    "NOVORA - LINKENGINE VPN",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            ApplyLinkEngineStoppedUiLE();
        }
        catch (Exception ex)
        {
            ApplyLinkEngineFailedUiLE(
                ex.Message);

            MessageBox.Show(
                ex.Message,
                "NOVORA - LINKENGINE STOP",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _linkEngineRuntimeOperationLE =
                false;

            LinkEngineTestButton.IsEnabled =
                !_closing;

            UpdateLinkEngineButtonLE();
        }
    }

    /*
     * ============================================================
     * RUNTIME STATUS EVENT
     * ============================================================
     */

    private void LinkEngineRuntime_StatusChangedLE(
        object? sender,
        SessionRuntimeLE session)
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
             * La UI nunca debe romper ManagerRuntimeLE.
             */
        }
    }

    /*
     * ============================================================
     * APPLY LIVE SESSION
     * ============================================================
     */

    private void ApplyLinkEngineRuntimeSessionLE(
        SessionRuntimeLE session)
    {
        if (_closing)
        {
            return;
        }

        string connection =
            session.ConnectionType switch
            {
                ConnectionDeviceLE.Usb =>
                    "USB",

                ConnectionDeviceLE.Wifi =>
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

        UpdateLinkEngineButtonLE();
    }

    /*
     * ============================================================
     * PORT
     * ============================================================
     */

    private string GetRuntimePortLE(
        SessionRuntimeLE session)
    {
        ManagerRuntimeLE? runtime =
            _linkEngineRuntimeLE;

        SessionTransportLE? transport =
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
        SessionRuntimeLE session)
    {
        if (session.State ==
                StateRuntimeLE.Running &&
            session.SessionHealthy)
        {
            return
                $"HEALTHY - HEARTBEAT #{session.HeartbeatSequence} - " +
                $"RECOVERIES {session.RecoverySuccessCount}";
        }

        if (session.State ==
            StateRuntimeLE.Degraded)
        {
            return
                $"RECOVERING - " +
                $"{FormatRecoveryStateLE(session.RecoveryState)} - " +
                session.Message;
        }

        return session.State switch
        {
            StateRuntimeLE.Starting =>
                "STARTING - inicializando LinkEngine...",

            StateRuntimeLE.ConnectingDevice =>
                "STARTING - conectando ManagerDeviceLE...",

            StateRuntimeLE.OpeningTransport =>
                "STARTING - abriendo ManagerTransportLE...",

            StateRuntimeLE.WaitingForAndroid =>
                "WAITING - esperando Android HELLO/ACK...",

            StateRuntimeLE.StartingRecoveryMonitor =>
                "STARTING - iniciando MonitorRecoveryLE...",

            StateRuntimeLE.Stopping =>
                "STOPPING - cerrando ManagerRuntimeLE...",

            StateRuntimeLE.Failed =>
                $"FAILED - {session.LastError ?? session.Message}",

            StateRuntimeLE.Stopped =>
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
        RecoveryMonitorStateLE state)
    {
        return state switch
        {
            RecoveryMonitorStateLE.WaitingForDevice =>
                "WAITING DEVICE",

            RecoveryMonitorStateLE.RecoveringSocket =>
                "SOCKET",

            RecoveryMonitorStateLE.RecoveringInfrastructure =>
                "INFRASTRUCTURE",

            RecoveryMonitorStateLE.Recovered =>
                "RECOVERED",

            RecoveryMonitorStateLE.Healthy =>
                "HEALTHY",

            RecoveryMonitorStateLE.Failed =>
                "FAILED",

            RecoveryMonitorStateLE.Degraded =>
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
        if (_closing)
        {
            LinkEngineTestButton.IsEnabled =
                false;

            return;
        }

        if (_linkEngineRuntimeOperationLE)
        {
            LinkEngineTestButton.IsEnabled =
                false;

            return;
        }

        SessionRuntimeLE? session =
            _linkEngineRuntimeLE?
                .SessionLE;

        bool active =
            session is not null &&
            session.State is not
                StateRuntimeLE.Stopped and not
                StateRuntimeLE.Failed;

        LinkEngineTestButton.Content =
            active
                ? "STOP LINKENGINE"
                : "START LINKENGINE";

        LinkEngineTestButton.IsEnabled =
            true;
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
            "STOPPED - Runtime detenido - listo para iniciar LinkEngine.";

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

        UpdateLinkEngineButtonLE();
    }

    /*
     * ============================================================
     * MAINWINDOW SHUTDOWN
     * ============================================================
     */

    private async Task ShutdownLinkEngineRuntimeLEAsync()
    {
        ManagerRuntimeLE? runtime =
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
