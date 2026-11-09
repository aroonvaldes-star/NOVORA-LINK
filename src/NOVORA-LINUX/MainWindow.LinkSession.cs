using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NOVORA.Linux.Control;
using NOVORA.Service;

namespace NOVORA.Linux;

/// <summary>
/// LinkEngine session parts of Windows NLUIWindowMainLinkEngine / LinkSpeedTest:
/// friendly session summary, technical details, SpeedTest from PC, manual traffic
/// sample, session export and the wireless ADB button.
/// </summary>
public sealed partial class MainWindow
{
    private readonly TextBlock _linkRecoveryStatus = new() { Text = "En espera" };
    private readonly TextBlock _linkInternetStatus = new() { Text = LinuxLinkMeasurements.NoInternet };
    private readonly TextBlock _linkTunnelStatus = new() { Text = LinuxLinkMeasurements.NoTunnel };
    private readonly TextBlock _linkTrafficStatus = new() { Text = LinuxLinkMeasurements.NoSession };
    private readonly Button _linkExportButton = new() { Content = "EXPORTAR RESUMEN DE LA SESIÓN", IsEnabled = false };
    private readonly Button _linkWifiAdbButton = new() { Content = "CONFIGURAR ADB INALÁMBRICO" };
    private readonly DispatcherTimer _linkSessionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private NLServiceADB? _linkAdb;
    private bool _linkSessionInitialized;
    private bool _linkWifiBusy;

    private NLServiceADB LinkAdb => _linkAdb ??= new NLServiceADB(new NLServiceNovoraPaths());

    private void InitializeLinkSessionVE()
    {
        if (_linkSessionInitialized) return;
        _linkSessionInitialized = true;
        foreach (TextBlock text in new[] { _linkInternetStatus, _linkTunnelStatus, _linkTrafficStatus })
        {
            text.FontSize = 13;
            text.Foreground = Brush("#a8b7ca");
            text.TextWrapping = TextWrapping.Wrap;
        }
        _linkInternetStatus.Margin = _linkTunnelStatus.Margin = new Thickness(0, 6, 0, 0);
        _linkRecoveryStatus.TextWrapping = TextWrapping.Wrap;
        StyleFlatButton(_linkExportButton);
        _linkExportButton.Height = 38;
        StyleFlatButton(_linkWifiAdbButton);
        _linkInternetTestButton.Click += async (_, _) => await StartLinkSpeedTestFromPcAsync("internet");
        _linkTunnelTestButton.Click += async (_, _) => await StartLinkSpeedTestFromPcAsync("tunnel");
        _linkTrafficButton.Click += (_, _) => _host.LinkMeasurements.ObserveTraffic(_host.CurrentLinkEngine);
        _linkExportButton.Click += async (_, _) => await ExportLinkSessionAsync();
        _linkWifiAdbButton.Click += async (_, _) => await ConfigureWirelessAdbAsync();
        _host.LinkMeasurements.Changed += (_, _) => Dispatcher.UIThread.Post(ApplyLinkSessionVE);
        _linkSessionTimer.Tick += (_, _) => ApplyLinkSessionVE();
        _linkSessionTimer.Start();
        Closed += (_, _) => _linkSessionTimer.Stop();
        ApplyLinkSessionVE();
    }

    /// <summary>Windows LESessionPresentation for the Linux engines (State, Link, Recovery).</summary>
    internal static (string State, string Link, string Recovery) LinkPresentationVE(ILinuxLinkEngine? engine)
    {
        string transport = engine?.Transport ?? "Sin transporte";
        string raw = engine?.State ?? "Stopped";
        bool ready = engine?.IsConnected == true && raw == "Connected";
        string state = ready ? "Conectada" : raw switch
        {
            "Stopped" or "Unavailable" => "Detenida",
            "Error" => "Necesita atención",
            "Degraded" => "Reconectando…",
            _ => "Conectando…"
        };
        int restarts = engine?.RelayRestarts ?? 0;
        string recovery = raw switch
        {
            "Degraded" => "Reconectando…",
            "Error" => "Necesita atención",
            "Waiting" => "Esperando teléfono",
            _ when ready && restarts > 0 => $"Recuperada · {restarts} reinicio{(restarts == 1 ? "" : "s")} del relay",
            _ => ready ? "Sin incidencias" : "En espera"
        };
        return (state, ready ? transport + " activo" : transport + " · " + state, recovery);
    }

    /// <summary>Windows LinkEngine technical status lines (device, transport, client, protocol).</summary>
    internal static string LinkTechnicalDetailsVE(ILinuxLinkEngine? engine)
    {
        if (engine is null || !engine.IsStarted)
            return "Sin sesión activa\nSin transporte activo\nSin cliente conectado\nSin protocolo activo";
        string relay = engine.RelayRestarts > 0 ? $"Relay local 27184 · reinicios: {engine.RelayRestarts}" : "Relay local 27184";
        if (engine.Transport == "LAN")
            return $"Transporte: LAN autorizada\n" +
                   $"Gateway DATA: {(engine.IsConnected ? "conectado" : "esperando teléfono")} · {relay}\n" +
                   $"Android: {(engine.IsConnected ? "conectado" : "esperando")}\n" +
                   "Canal DATA LAN con TLS";
        return $"ADB: {engine.Serial ?? "-"} - Conexión: USB\n" +
               $"Puertos: CONTROL 27183 · DATA 27184 - Reverse: VERIFIED - Listener: LISTENING · {relay}\n" +
               $"Android: {(engine.IsConnected ? "CONNECTED" : "WAITING")} - Handshake: {(engine.IsConnected ? "VERIFIED" : "WAITING")}\n" +
               $"NOVORA-LINK/2 - Client ID: {engine.ClientId} - Heartbeat: {engine.HeartbeatCount}";
    }

    private void ApplyLinkSessionVE()
    {
        if (_shutdownStarted) return;
        ILinuxLinkEngine? engine = _host.CurrentLinkEngine;
        LinuxLinkMeasurements measurements = _host.LinkMeasurements;
        measurements.Sync(engine);
        var presentation = LinkPresentationVE(engine);
        _linkRecoveryStatus.Text = presentation.Recovery;
        _linkTechnicalStatus.Text = LinkTechnicalDetailsVE(engine);
        _linkInternetStatus.Text = measurements.InternetText;
        _linkTunnelStatus.Text = measurements.TunnelText;
        _linkTrafficStatus.Text = measurements.TrafficText;
        bool session = measurements.SessionKey is not null;
        _linkInternetTestButton.IsEnabled = session;
        _linkTunnelTestButton.IsEnabled = session;
        _linkTrafficButton.IsEnabled = session;
        _linkExportButton.IsEnabled = session;
        _linkWifiAdbButton.IsEnabled = !_linkWifiBusy;
    }

    /// <summary>Windows StartLinkSpeedTestFromPcAsync: opens the SpeedTest in the phone browser over LE.</summary>
    private async Task StartLinkSpeedTestFromPcAsync(string mode)
    {
        ILinuxLinkEngine? engine = _host.CurrentLinkEngine;
        LinuxLinkMeasurements measurements = _host.LinkMeasurements;
        measurements.Sync(engine);
        Action<string> status = mode == "internet" ? measurements.SetInternetStatus : measurements.SetTunnelStatus;
        if (engine is null || measurements.SessionKey is null)
        {
            status("Inicia LE y espera la conexión del teléfono.");
            return;
        }
        if (engine.Serial is not { } serial)
        {
            status("LAN activa. Abre la medición de LE desde AppControl en el teléfono.");
            return;
        }
        string? session = measurements.SessionKey;
        Button button = mode == "internet" ? _linkInternetTestButton : _linkTunnelTestButton;
        button.IsEnabled = false;
        try
        {
            string url = measurements.Begin(engine, mode);
            string output = await LinkAdb.ExecuteRawAsync(
                ["-s", serial, "shell", "am", "start", "-a", "android.intent.action.VIEW", "-d", url]);
            if (measurements.SessionKey != session) return;
            if (output.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Permission Denial", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("unable to resolve", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(output.Trim());
            status(mode == "internet"
                ? "SpeedTest de Internet abierto en el teléfono. Esperando resultado real por LE…"
                : "Prueba del túnel abierta en el teléfono. Esperando resultado real por LE…");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Net.Sockets.SocketException)
        {
            if (measurements.SessionKey == session)
                status("No se pudo abrir el navegador. Abre la medición de LE desde AppControl.");
        }
        finally { ApplyLinkSessionVE(); }
    }

    private async Task ExportLinkSessionAsync()
    {
        LinuxLinkMeasurements measurements = _host.LinkMeasurements;
        string? session = measurements.SessionKey;
        if (session is null)
        {
            SetMessage("No hay una sesión activa para exportar.", "#fbbf24");
            return;
        }
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar resumen de LinkEngine",
            SuggestedFileName = "LE-sesion.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("Resumen JSON") { Patterns = ["*.json"] }]
        });
        if (file is null) return;
        if (measurements.SessionKey != session || measurements.ExportJson() is not { } json)
        {
            SetMessage("La sesión cambió; no se exportó el resumen anterior.", "#fbbf24");
            return;
        }
        try
        {
            await using Stream stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);
            SetMessage("Resumen de esta sesión exportado. El guardado automático sigue desactivado.", "#22c55e");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetMessage("No se pudo exportar el resumen: " + ex.Message, "#f87171");
        }
    }

    /// <summary>
    /// Windows WifiAdb_Click: a USB phone gets <c>adb tcpip</c> + connect over its Wi-Fi IP;
    /// otherwise the Wireless debugging endpoint is asked for (pre-filled from mDNS).
    /// </summary>
    private async Task ConfigureWirelessAdbAsync()
    {
        if (_linkWifiBusy) return;
        _linkWifiBusy = true;
        ApplyLinkSessionVE();
        try
        {
            IReadOnlyList<NLServiceADBDevice> devices = [];
            try { devices = await LinkAdb.GetDevicesAsync(forceRefresh: true); }
            catch (InvalidOperationException ex) { SetMessage(ex.Message, "#f87171"); return; }
            string? usbSerial = _host.IsUsbRunning ? _host.UsbSerial : null;
            usbSerial ??= devices.FirstOrDefault(device => device.Connected && device.IsUsb)?.Serial;
            if (usbSerial is not null)
            {
                SetMessage("Preparando la ruta LAN del mismo teléfono…");
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                string lanSerial = await LinkAdb.ConnectOverWifiAsync(usbSerial, cancellationToken: deadline.Token);
                SetMessage($"ADB inalámbrico listo para {lanSerial}.", "#22c55e");
                return;
            }

            string? discovered = null;
            try
            {
                using var discovery = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                IReadOnlyList<string> endpoints = await LinkAdb.GetWirelessDebuggingEndpointsAsync(discovery.Token);
                if (endpoints.Count == 1) discovered = endpoints[0];
            }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException) { }
            string? endpoint = await AskWirelessDebuggingEndpointAsync(discovered);
            if (endpoint is null) return;
            using var connect = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            string serial = await LinkAdb.ConnectWirelessDebuggingAsync(endpoint, connect.Token);
            SetMessage($"ADB inalámbrico conectado a {serial}.", "#22c55e");
        }
        catch (OperationCanceledException)
        {
            SetMessage("La preparación de ADB inalámbrico agotó el tiempo de espera.", "#fbbf24");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
        {
            SetMessage("No se pudo conectar ADB inalámbrico: " + ex.Message, "#f87171");
        }
        finally
        {
            _linkWifiBusy = false;
            ApplyLinkSessionVE();
            _ = RefreshAdbDevicesAsync();
        }
    }

    private async Task<string?> AskWirelessDebuggingEndpointAsync(string? discovered)
    {
        var input = new TextBox { Text = discovered ?? "", FontSize = 16, Watermark = "10.0.0.28:37123" };
        var connect = FlatButton("CONECTAR ADB");
        var cancel = FlatButton("Cancelar");
        string? result = null;
        var dialog = new Window
        {
            Title = "ADB inalámbrico",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#131f31"),
            FontFamily = FontFamily,
            Content = new StackPanel
            {
                Margin = new Thickness(22),
                Spacing = 14,
                Children =
                {
                    SectionTitle("ADB inalámbrico"),
                    Caption("Escribe la Dirección IP y puerto de la pantalla principal de Depuración inalámbrica del teléfono. El puerto de vinculación es diferente."),
                    input,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { cancel, connect }
                    }
                }
            }
        };
        connect.Click += (_, _) => { result = input.Text?.Trim(); dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
