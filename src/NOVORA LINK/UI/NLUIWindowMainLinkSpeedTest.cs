using NOVORA.Control;
using NOVORA.LinkEngine.Metrics;
using NOVORA.LinkEngine.Device;
using NOVORA.LinkEngine.Network;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private LESpeedTestServer? _linkSpeedTestServer;
    private DispatcherTimer? _linkMonitorTimer;
    private readonly LESessionMeasurements _linkMeasurements = new();
    private string? _linkMeasurementSourceKey;
    private string? _linkTestSessionKey;
    private string? _linkTestId;

    private (string Key, string Transport, LENetworkRelay Relay, string? Serial)? ActiveLinkSourceLE()
    {
        if (_androidLanDataGateway?.IsConnected == true && _androidLanControl?.IsAuthorized == true && _androidLanRelay is { } lanRelay)
            return ($"LAN:{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_androidLanDataGateway)}:{_androidControlGeneration}:{_androidLanDataGateway.ConnectionGeneration}", "LAN", lanRelay, null);
        var runtime = _linkEngineRuntimeLE;
        var session = runtime?.SessionLE;
        if (runtime?.IsRunningLE == true && session is { State: NOVORA.LinkEngine.Runtime.LERuntimeState.Running, SessionHealthy: true } &&
            runtime.EngineLE?.Network.PolicyRelayLE is { } relay)
            return ($"{session.Serial}:{session.StartedAtUtc:O}:{session.ConnectedAtUtc:O}:{session.ConnectionType}",
                session.ConnectionType == LEDeviceConnection.Wifi ? "LAN" : "USB", relay, session.Serial);
        return null;
    }

    private void SyncLinkMeasurementsLE()
    {
        var source = ActiveLinkSourceLE();
        if (_androidLanDataGateway is not null && (source is null || source.Value.Serial is null))
        {
            LinkEngineDeviceStatus.Text = source is not null ? "Transporte: LAN autorizada" : "LAN: sin sesión DATA autorizada";
            LinkEngineTransportStatus.Text = source is not null ? "Gateway DATA: conectado · Relay local: 27184" : "Gateway DATA: esperando teléfono";
            LinkEngineClientStatus.Text = source is not null ? "Android: conectado" : "Android: esperando";
            LinkEngineProtocolStatus.Text = "Canal DATA LAN con TLS";
            LinkEngineResultStatus.Text = source is not null ? "LE conectado por LAN." : "Esperando conexión DATA del teléfono por LAN.";
            LinkEngineFriendlyConnectionStatus.Text = source is not null ? "Activa · LAN" : "Preparando LAN";
            LinkEngineFriendlyPhoneStatus.Text = source is not null ? "Conectado" : "Esperando teléfono";
            LinkEngineFriendlyUsbStatus.Text = source is not null ? "LAN activa" : "LAN en preparación";
            LinkEngineFriendlyRecoveryStatus.Text = source is not null ? "Sin incidencias" : "Esperando conexión";
        }
        else if (_linkEngineRuntimeLE?.SessionLE is { } runtimeSession)
        {
            ApplyFriendlyLinkEngineStatusLE(runtimeSession);
            LinkEngineResultStatus.Text = BuildRuntimeResultTextLE(runtimeSession);
            if (runtimeSession.State == NOVORA.LinkEngine.Runtime.LERuntimeState.Stopped)
            {
                LinkEngineDeviceStatus.Text = "Sin sesión activa";
                LinkEngineTransportStatus.Text = "Sin transporte activo";
                LinkEngineClientStatus.Text = "Sin cliente conectado";
                LinkEngineProtocolStatus.Text = "Sin protocolo activo";
            }
        }
        else
        {
            LinkEngineResultStatus.Text = "Conecta el teléfono por USB o LAN y activa LinkEngine desde AppControl.";
            LinkEngineFriendlyConnectionStatus.Text = "Detenida";
            LinkEngineFriendlyPhoneStatus.Text = "Sin comunicación";
            LinkEngineFriendlyUsbStatus.Text = "Sin enlace activo";
            LinkEngineFriendlyRecoveryStatus.Text = "En espera";
            LinkEngineDeviceStatus.Text = "Sin sesión activa";
            LinkEngineTransportStatus.Text = "Sin transporte activo";
            LinkEngineClientStatus.Text = "Sin cliente conectado";
            LinkEngineProtocolStatus.Text = "Sin protocolo activo";
        }
        if (source?.Key == _linkMeasurementSourceKey) return;
        StopLinkLiveMonitor();
        _linkMeasurementSourceKey = source?.Key;
        _linkSpeedTestServer?.Invalidate();
        _linkTestSessionKey = _linkTestId = null;
        _linkMeasurements.End();
        LinkInternetMeasurementsStatus.Text = "Internet: sin medir en esta sesión.";
        LinkTunnelMeasurementsStatus.Text = "Enlace PC ↔ Android: sin medir en esta sesión.";
        LinkLiveMonitorStatus.Text = "Sin sesión activa. No se conservan mediciones anteriores.";
        if (source is { } active)
        {
            _linkMeasurements.Begin(Guid.NewGuid().ToString("N"), active.Transport, DateTimeOffset.UtcNow);
            LinkLiveMonitorButton.Content = "MEDIR TRÁFICO ACTUAL";
            LinkLiveMonitorStatus.Text = "Medición manual disponible para esta sesión. No se sondea tráfico en segundo plano.";
        }
    }

    private string BeginLinkMeasurementLE(string transport)
    {
        SyncLinkMeasurementsLE();
        if (_linkMeasurements.SessionKey is not { } key || _linkMeasurements.Transport != transport)
            throw new InvalidOperationException("Inicia LE en ese transporte antes de medir.");
        var server = EnsureLinkSpeedTestServer();
        string url = server.Begin(transport);
        _linkTestSessionKey = key;
        _linkTestId = server.CurrentTestId;
        return url;
    }

    private bool IsCurrentLinkTestLE(string id)
    {
        SyncLinkMeasurementsLE();
        return _linkMeasurements.SessionKey is not null && _linkTestSessionKey == _linkMeasurements.SessionKey && _linkTestId == id;
    }

    private LESpeedTestServer EnsureLinkSpeedTestServer()
    {
        if (_linkSpeedTestServer is not null) return _linkSpeedTestServer;
        _linkSpeedTestServer = new LESpeedTestServer();
        _linkSpeedTestServer.ResultReceived += (_, result) =>
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (!_closing && IsCurrentLinkTestLE(result.SessionId) && _linkMeasurements.SetTunnel(_linkTestSessionKey!, result))
                    LinkTunnelMeasurementsStatus.Text =
                        $"Túnel LE {result.Transport} · ↓ {result.DownloadMbps:F1} Mbps · " +
                        $"↑ {result.UploadMbps:F1} Mbps · RTT HTTP {result.LatencyMs:F1} ms · " +
                        $"variación {result.JitterMs:F1} ms · {result.SampledAtUtc.ToLocalTime():HH:mm:ss}";
            });
        _linkSpeedTestServer.InternetResultReceived += (_, result) =>
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (!_closing && IsCurrentLinkTestLE(result.SessionId) && _linkMeasurements.SetInternet(_linkTestSessionKey!, result))
                    LinkInternetMeasurementsStatus.Text =
                        $"Internet por LE {result.Transport} · ↓ {result.DownloadMbps:F1} Mbps · " +
                        $"↑ {result.UploadMbps:F1} Mbps · RTT HTTP {result.LatencyMs:F1} ms · " +
                        $"variación {result.JitterMs:F1} ms · {result.Provider} · {result.SampledAtUtc.ToLocalTime():HH:mm:ss}";
            });
        return _linkSpeedTestServer;
    }

    private NLControlReply StartLinkTunnelTest(NLControlRequest request, string transport)
    {
        try
        {
            if (transport is not ("USB" or "LAN"))
                return new(NLControlProtocol.Version, request.Id, false, "Transporte LE no disponible.");
            string url = BeginLinkMeasurementLE(transport);
            LinkTunnelMeasurementsStatus.Text = "SpeedTest preparado. Ejecútalo en el navegador de Android.";
            return new(NLControlProtocol.Version, request.Id, true,
                "Abre la prueba del túnel en Android.", CaptureAndroidControlSnapshot(), Value: url);
        }
        catch (Exception ex)
        {
            return new(NLControlProtocol.Version, request.Id, false,
                "No se pudo preparar la prueba del túnel: " + ex.Message,
                CaptureAndroidControlSnapshot());
        }
    }

    private async void LinkInternetSpeedTestButton_Click(object sender, RoutedEventArgs e) =>
        await StartLinkSpeedTestFromPcAsync("internet");

    private async void LinkTunnelSpeedTestButton_Click(object sender, RoutedEventArgs e) =>
        await StartLinkSpeedTestFromPcAsync("tunnel");

    private async Task StartLinkSpeedTestFromPcAsync(string mode)
    {
        SyncLinkMeasurementsLE();
        var source = ActiveLinkSourceLE();
        var status = mode == "internet" ? LinkInternetMeasurementsStatus : LinkTunnelMeasurementsStatus;
        if (source is null)
        {
            status.Text = "Inicia LE y espera la conexión del teléfono.";
            return;
        }
        if (source.Value.Serial is not { } serial)
        {
            status.Text = "LAN activa. Abre la medición de LE desde AppControl en el teléfono.";
            return;
        }
        string transport = source.Value.Transport;
        string? measurementSession = _linkMeasurements.SessionKey;
        var button = mode == "internet" ? LinkInternetSpeedTestButton : LinkTunnelSpeedTestButton;
        button.IsEnabled = false;
        try
        {
            string url = BeginLinkMeasurementLE(transport) + "?mode=" + mode;
            string output = await _adb.ExecuteRawAsync(new[]
            {
                "-s", serial, "shell", "am", "start", "-a", "android.intent.action.VIEW",
                "-d", url
            });
            if (_linkMeasurements.SessionKey != measurementSession) return;
            if (output.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Permission Denial", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("unable to resolve", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(output.Trim());
            status.Text = mode == "internet"
                ? "SpeedTest de Internet abierto en el teléfono. Esperando resultado real por LE…"
                : "Prueba del túnel abierta en el teléfono. Esperando resultado real por LE…";
        }
        catch (Exception)
        {
            if (_linkMeasurements.SessionKey == measurementSession)
                status.Text = "No se pudo abrir el navegador. Abre la medición de LE desde AppControl.";
        }
        finally { button.IsEnabled = true; }
    }

    private void LinkLiveMonitorButton_Click(object sender, RoutedEventArgs e)
    {
        SyncLinkMeasurementsLE();
        if (_linkMeasurements.SessionKey is null)
            LinkLiveMonitorStatus.Text = "Inicia LE antes de medir el tráfico.";
        else LinkLiveMonitorTick(sender, e);
    }

    private void LinkLiveMonitorTick(object? sender, EventArgs e)
    {
        SyncLinkMeasurementsLE();
        if (_linkMeasurements.SessionKey is not { } key || ActiveLinkSourceLE() is not { } source) return;
        _linkMeasurements.Observe(key, source.Relay.GetTrafficSnapshotLE(), DateTimeOffset.UtcNow);
        LinkLiveMonitorStatus.Text = _linkMeasurements.UploadMbps is double up && _linkMeasurements.DownloadMbps is double down
            ? $"Subida desde Android: {up:F2} Mbps · Bajada hacia Android: {down:F2} Mbps\n" +
              $"Datos observados en esta sesión: ↑ {_linkMeasurements.UploadedBytes / 1_000_000d:F2} MB · ↓ {_linkMeasurements.DownloadedBytes / 1_000_000d:F2} MB"
            : "Tráfico actual: sin medir; esperando contadores recientes de esta sesión.";
    }

    private void StopLinkLiveMonitor()
    {
        if (_linkMonitorTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= LinkLiveMonitorTick;
        }
        _linkMonitorTimer = null;
        _linkMeasurements.ClearLive();
        if (!_closing)
        {
            LinkLiveMonitorButton.Content = "MEDIR TRÁFICO ACTUAL";
            LinkLiveMonitorStatus.Text = "Tráfico actual: sin medir. Sin sondeo automático ni historial.";
        }
    }

    private void ExportLinkSession_Click(object sender, RoutedEventArgs e)
    {
        SyncLinkMeasurementsLE();
        if (_linkMeasurements.SessionKey is null)
        {
            LinkLiveMonitorStatus.Text = "No hay una sesión activa para exportar.";
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Resumen JSON (*.json)|*.json", FileName = "LE-sesion.json", AddExtension = true
        };
        string sessionKey = _linkMeasurements.SessionKey;
        if (dialog.ShowDialog(this) != true) return;
        SyncLinkMeasurementsLE();
        if (_linkMeasurements.SessionKey != sessionKey)
        {
            LinkLiveMonitorStatus.Text = "La sesión cambió; no se exportó el resumen anterior.";
            return;
        }
        try
        {
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_linkMeasurements,
                new JsonSerializerOptions { WriteIndented = true }));
            LinkLiveMonitorStatus.Text = "Resumen de esta sesión exportado. El guardado automático sigue desactivado.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LinkLiveMonitorStatus.Text = "No se pudo exportar el resumen: " + ex.Message;
        }
    }
}
