using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NOVORA.Control;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private void HomeLanConnectionButton_Click(object sender, RoutedEventArgs e)
        => PrepareAndroidLan_Click(sender, e);

    private void UpdateHomeLanConnectionButton()
    {
        if (_closing || HomeLanConnectionButton is null) return;
        bool connected = _androidLanControl?.IsAuthorized == true;
        HomeLanConnectionButton.Content = connected ? "LAN CONECTADA" : "CONECTAR POR LAN";
        HomeLanConnectionButton.IsEnabled = !_androidControlPreparing && !_androidInstalling;
    }

    private static bool IsPrivateLanAddress(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168));
    }

    private static IPAddress[] GetAndroidLanAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up &&
            n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses
            .Where(a => IsPrivateLanAddress(a.Address))
            .Select(a => a.Address))
        .Distinct().ToArray();

    private IPAddress? ChooseAndroidLanAddress(bool createInvitation = true)
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses
                .Where(a => IsPrivateLanAddress(a.Address))
                .Select(a => new { Label = $"{n.Name} · {a.Address}", Address = a.Address }))
            .GroupBy(a => a.Address).Select(g => g.First()).ToArray();
        if (addresses.Length == 0)
            throw new InvalidOperationException("Conecta la PC a una red local IPv4 privada por Wi-Fi o Ethernet.");

        var body = new StackPanel();
        var intro = new TextBlock { Text = "Elige la red que comparte tu teléfono. La invitación autoriza el control. Si eliges Recordar esta PC en Android, podrá reconectar hasta que revoques ese teléfono.", TextWrapping = TextWrapping.Wrap };
        ApplyNovoraText(intro);
        body.Children.Add(intro);
        var choice = new System.Windows.Controls.ComboBox { ItemsSource = addresses, DisplayMemberPath = "Label", SelectedIndex = 0, Margin = new Thickness(0, 16, 0, 16) };
        body.Children.Add(choice);
        var dialog = CreateNovoraDialog("Preparar conexión LAN", 520, body);
        var button = new System.Windows.Controls.Button { Content = createInvitation ? "GENERAR CÓDIGO LAN" : "ACTIVAR RECONEXIÓN LAN", Padding = new Thickness(12) };
        ApplyNovoraActionButton(button);
        button.Click += (_, _) => dialog.DialogResult = true;
        body.Children.Add(button);
        return dialog.ShowDialog() == true ? addresses[choice.SelectedIndex].Address : null;
    }

    private async void PrepareAndroidLan_Click(object sender, RoutedEventArgs e)
    {
        if (_androidControlPreparing || _androidInstalling) return;
        _androidControlPreparing = true;
        UpdateHomeLanConnectionButton();
        NLControlTrustServer? created = null;
        try
        {
            IPAddress? address = ChooseAndroidLanAddress();
            if (address is null || _closing) return;
            await StopAndroidControlAsync();
            long generation = _androidControlGeneration;
            _viewModel.RefreshAudioOutputOptions(_paths);
            var store = GetAndroidTrustStore();
            var server = new NLControlTrustServer(address, request => HandleAndroidControlAsync(request, generation, "LAN"), store);
            created = server;
            _androidLanControl = server;

            Window? invitationDialog = null;
            server.StatusChanged += (_, status) => Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (!ReferenceEquals(_androidLanControl, server)) return;
                if (!server.IsAuthorized && !AndroidControlAuthorized) { ResetAndroidFileTransfer(); await FinishAndroidRecordingAsync(); }
                AndroidControlStatus.Text = _androidControl?.IsAuthorized == true
                    ? "USB activo; LAN permanece disponible como respaldo."
                    : status;
                UpdateHomeLanConnectionButton();
                UpdateRuntimeButtons();
                AndroidEngineStateChanged();
                if (server.IsAuthorized) RequestWaitingExternalVisionFromAndroid();
                if (server.IsAuthorized || server.IsClosed || !server.IsInvitationOpen)
                {
                    var discovery = _androidLanDiscovery;
                    _androidLanDiscovery = null;
                    if (discovery is not null) await discovery.DisposeAsync();
                    invitationDialog?.Close();
                }
            }));
            server.Start();
            store.SetListening(address.ToString(), true);
            var discovery = new NLControlLanDiscovery(
                new(Environment.MachineName, address.ToString(), server.Invitation.Port,
                    server.Invitation.Fingerprint));
            _androidLanDiscovery = discovery;
            string discoveryStatus;
            try { discovery.Start(); discoveryStatus = "Android puede buscar esta PC en la red."; }
            catch (Exception)
            {
                await discovery.DisposeAsync();
                _androidLanDiscovery = null;
                discoveryStatus = "La búsqueda no está disponible en esta red. Revisa el firewall privado de Windows.";
            }
            // Show the invitation immediately; ADB discovery can take several seconds.
            Task<bool> codeDelivery = TryDeliverLanCodeOverAdbAsync(server);

            var body = new StackPanel();
            var title = new TextBlock { Text = $"{Environment.MachineName} · {address}\n" +
                "Escribe este código en NOVORA Android:", FontSize = 17, TextWrapping = TextWrapping.Wrap };
            ApplyNovoraText(title);
            body.Children.Add(title);
            var code = new TextBlock { Text = server.PairingCode, FontSize = 42, FontWeight = FontWeights.Bold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 18) };
            ApplyNovoraText(code);
            body.Children.Add(code);
            var fingerprint = new TextBlock { Text = $"Huella del certificado (compárala completa en Android):\n{server.Invitation.Fingerprint}", TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 12, 0, 0) };
            ApplyNovoraText(fingerprint);
            body.Children.Add(fingerprint);
            var details = new TextBlock { Text = $"{discoveryStatus}\n\nEl código caduca en 2 minutos, admite hasta 5 intentos incorrectos y sólo autoriza una vinculación. El código y los datos de confianza viajan únicamente por TLS. Guardar esta PC en Android es opcional.\n\nSi Windows pide acceso de red, permite NOVORA sólo en tu red privada. No compartas el código.", TextWrapping = TextWrapping.Wrap };
            ApplyNovoraText(details, "MutedBrush");
            body.Children.Add(details);
            invitationDialog = CreateNovoraDialog("NOVORA Android · Código LAN", 510, body);
            invitationDialog.Loaded += async (_, _) =>
            {
                bool delivered = await codeDelivery;
                if (delivered && ReferenceEquals(_androidLanControl, server) &&
                    server.IsInvitationOpen && invitationDialog?.IsVisible == true)
                    title.Text = $"{Environment.MachineName} · {address}\n" +
                        "Código colocado en AppControl por depuración autorizada:";
            };
            AndroidControlStatus.Text = "Código LAN abierto; todavía no hay un Android autorizado.";
            invitationDialog.ShowDialog();
            invitationDialog = null;
            server.CancelInvitation();
            if (!server.IsAuthorized && store.Devices.Count == 0 && ReferenceEquals(_androidLanControl, server)) await StopAndroidControlAsync();
        }
        catch (Exception ex)
        {
            if (created is not null && ReferenceEquals(_androidLanControl, created)) await StopAndroidControlAsync();
            if (!_closing) AndroidControlStatus.Text = "No se pudo preparar LAN: " + ex.Message;
        }
        finally
        {
            _androidControlPreparing = false;
            UpdateHomeLanConnectionButton();
        }
    }

    private async Task<bool> TryDeliverLanCodeOverAdbAsync(NLControlTrustServer server)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var devices = (await _adb.GetDevicesAsync(timeout.Token, forceRefresh: true))
                .Where(device => device.Connected).ToArray();
            if (devices.Length == 0) return false;
            var physicalIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var device in devices)
            {
                string id = (await _adb.ExecuteRawAsync(
                    ["-s", device.Serial, "shell", "getprop", "ro.serialno"], timeout.Token)).Trim();
                if (string.IsNullOrWhiteSpace(id) || id.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                    return false;
                physicalIds.Add(id);
            }
            if (physicalIds.Count != 1) return false;
            string serial = devices[0].Serial;
            string installed = await _adb.ExecuteRawAsync(
                ["-s", serial, "shell", "pm", "path", "com.novora.appcontrol"], timeout.Token);
            if (!installed.Contains("package:com.novora.appcontrol", StringComparison.Ordinal)) return false;
            var invitation = server.Invitation;
            var handoff = new NLControlLanCodeHandoff(
                Environment.MachineName, invitation.Host, invitation.Port,
                invitation.Fingerprint, server.PairingCode, invitation.ExpiresUnix);
            handoff.Validate();
            string payload = Convert.ToBase64String(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(handoff));
            string launch = await _adb.ExecuteRawAsync(
                ["-s", serial, "shell", "am", "start", "--user", "0", "-n",
                    "com.novora.appcontrol/.LanCodeActivity", "--es", "novora.lan.code", payload],
                timeout.Token);
            return !launch.Contains("Error:", StringComparison.OrdinalIgnoreCase) &&
                !launch.Contains("Permission Denial", StringComparison.OrdinalIgnoreCase) &&
                !launch.Contains("SecurityException", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }
}
