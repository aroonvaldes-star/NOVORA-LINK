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
    private LERelayTrafficSnapshot? _linkMonitorPrevious;
    private string? _linkMonitorLogPath;

    private LESpeedTestServer EnsureLinkSpeedTestServer()
    {
        if (_linkSpeedTestServer is not null) return _linkSpeedTestServer;
        _linkSpeedTestServer = new LESpeedTestServer();
        _linkSpeedTestServer.ResultReceived += (_, result) =>
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (!_closing)
                    LinkTunnelMeasurementsStatus.Text =
                        $"Túnel LE {result.Transport} · ↓ {result.DownloadMbps:F1} Mbps · " +
                        $"↑ {result.UploadMbps:F1} Mbps · RTT HTTP {result.LatencyMs:F1} ms · " +
                        $"variación {result.JitterMs:F1} ms · {result.SampledAtUtc.ToLocalTime():HH:mm:ss}";
            });
        _linkSpeedTestServer.InternetResultReceived += (_, result) =>
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (!_closing)
                    LinkInternetMeasurementsStatus.Text =
                        $"Internet por LE {result.Transport} · ↓ {result.DownloadMbps:F1} Mbps · " +
                        $"↑ {result.UploadMbps:F1} Mbps · RTT HTTP {result.LatencyMs:F1} ms · " +
                        $"variación {result.JitterMs:F1} ms · {result.Provider} · {result.SampledAtUtc.ToLocalTime():HH:mm:ss}";
            });
        Closed += (_, _) => _linkSpeedTestServer?.Dispose();
        return _linkSpeedTestServer;
    }

    private NLControlReply StartLinkTunnelTest(NLControlRequest request, string transport)
    {
        try
        {
            if (transport is not ("USB" or "LAN"))
                return new(NLControlProtocol.Version, request.Id, false, "Transporte LE no disponible.");
            string url = EnsureLinkSpeedTestServer().Begin(transport);
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
        var session = _linkEngineRuntimeLE?.SessionLE;
        var status = mode == "internet" ? LinkInternetMeasurementsStatus : LinkTunnelMeasurementsStatus;
        if (_linkEngineRuntimeLE?.IsRunningLE != true || session is null)
        {
            status.Text = "Inicia LE y espera a que el VPN del teléfono esté conectado.";
            return;
        }
        string serial = session.Serial;
        string transport = session.ConnectionType == LEDeviceConnection.Wifi ? "LAN" : "USB";
        var button = mode == "internet" ? LinkInternetSpeedTestButton : LinkTunnelSpeedTestButton;
        button.IsEnabled = false;
        try
        {
            string url = EnsureLinkSpeedTestServer().Begin(transport) + "?mode=" + mode;
            string output = await _adb.ExecuteRawAsync(new[]
            {
                "-s", serial, "shell", "am", "start", "-a", "android.intent.action.VIEW",
                "-d", url
            });
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
            status.Text = "No se pudo abrir el navegador del teléfono por ADB. Comprueba ADB y LE.";
        }
        finally { button.IsEnabled = true; }
    }

    private void LinkLiveMonitorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_linkMonitorTimer is not null) { StopLinkLiveMonitor(); return; }
        if (_linkEngineRuntimeLE?.IsRunningLE != true)
        {
            LinkLiveMonitorStatus.Text = "Inicia LE antes de monitorear el relay.";
            return;
        }
        _linkMonitorPrevious = null;
        string directory = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "NOVORA", "LinkEngine", "Monitoring");
        try
        {
            Directory.CreateDirectory(directory);
            _linkMonitorLogPath = Path.Combine(directory,
                $"LE-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
            _linkMonitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _linkMonitorTimer.Tick += LinkLiveMonitorTick;
            _linkMonitorTimer.Start();
            LinkLiveMonitorButton.Content = "DETENER MONITOREO DE LE";
            LinkLiveMonitorStatus.Text = "Monitoreando tráfico real del relay LE…";
            Closed += (_, _) => StopLinkLiveMonitor();
        }
        catch (Exception ex)
        {
            LinkLiveMonitorStatus.Text = "No se pudo iniciar el monitor LE: " + ex.Message;
        }
    }

    private void LinkLiveMonitorTick(object? sender, EventArgs e)
    {
        if (_linkEngineRuntimeLE?.IsRunningLE != true)
        {
            StopLinkLiveMonitor();
            LinkLiveMonitorStatus.Text = "Monitor detenido: LE se desconectó.";
            return;
        }
        var sample = _linkEngineRuntimeLE.EngineLE?.Network.GetTrafficSnapshotLE();
        if (sample is null || DateTimeOffset.UtcNow - sample.SampledAtUtc > TimeSpan.FromSeconds(3))
        {
            LinkLiveMonitorStatus.Text = "Esperando contadores recientes del relay LE…";
            return;
        }
        if (_linkMonitorPrevious is { } previous &&
            sample.SampledAtUtc > previous.SampledAtUtc &&
            sample.TxBytes >= previous.TxBytes && sample.RxBytes >= previous.RxBytes)
        {
            double seconds = (sample.SampledAtUtc - previous.SampledAtUtc).TotalSeconds;
            double tx = (sample.TxBytes - previous.TxBytes) * 8 / seconds / 1_000_000;
            double rx = (sample.RxBytes - previous.RxBytes) * 8 / seconds / 1_000_000;
            LinkLiveMonitorStatus.Text =
                $"EN VIVO LE · TX teléfono→PC {tx:F2} Mbps · RX PC→teléfono {rx:F2} Mbps · " +
                $"paquetes {sample.TxPackets:N0}/{sample.RxPackets:N0} · {sample.SampledAtUtc.ToLocalTime():HH:mm:ss}";
            try
            {
                File.AppendAllText(_linkMonitorLogPath!, JsonSerializer.Serialize(new
                {
                    sample.SampledAtUtc, TxMbps = tx, RxMbps = rx,
                    sample.TxBytes, sample.RxBytes, sample.TxPackets, sample.RxPackets,
                    Source = "NovoraRelay DATA"
                }) + Environment.NewLine);
            }
            catch (Exception ex)
            {
                StopLinkLiveMonitor();
                LinkLiveMonitorStatus.Text = "Monitor detenido: no se pudo guardar la muestra. " + ex.Message;
                return;
            }
        }
        _linkMonitorPrevious = sample;
    }

    private void StopLinkLiveMonitor()
    {
        _linkMonitorTimer?.Stop();
        _linkMonitorTimer = null;
        _linkMonitorPrevious = null;
        if (!_closing)
        {
            LinkLiveMonitorButton.Content = "INICIAR MONITOREO EN VIVO DE LE";
            LinkLiveMonitorStatus.Text = "Monitor LE detenido. Las muestras de esta sesión quedaron guardadas en PC.";
        }
    }
}
