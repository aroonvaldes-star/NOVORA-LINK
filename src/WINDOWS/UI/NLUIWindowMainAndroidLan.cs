using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NOVORA.Control;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private void LinkEnginePrepareRoute_Click(object sender, RoutedEventArgs e)
    {
        if (LinkEngineStartRoute.SelectedIndex == 1)
            HomeConnectionLan_Click(sender, e);
        else
            HomeConnectionUsb_Click(sender, e);
    }

    private bool ConnectionRouteBusy()
        => IsVisionEngineRunningVE() || _visionCommandApplyingVE ||
           _pcVideoAuthorizationRequested || _veLanSessionVE is not null ||
           _linkEngineRuntimeLE?.IsRunningLE == true ||
           _androidLanDataGateway is not null ||
           _androidLinkStarting || _androidLinkStopping;

    private async void HomeConnectionUsb_Click(object sender, RoutedEventArgs e)
    {
        if (_closing || _androidControlPreparing || _androidLanRebinding) return;
        try
        {
            if (ConnectionRouteBusy())
            {
                ShowTopMessage14("Detén VisionEngine y LinkEngine antes de cambiar la conexión.", NLUIMessageKind14.Warning);
                return;
            }
            await RefreshDevicesAsync(force: true);
            if (!_viewModel.Devices.Any(device => device.Connected && !device.IsWifiConnection))
            {
                ShowTopMessage14("Conecta el teléfono por USB y autoriza la depuración para preparar esta ruta.", NLUIMessageKind14.Info);
                return;
            }
            if (_androidControl?.IsAuthorized == true)
            {
                ShowTopMessage14("La conexión USB ya está autorizada.", NLUIMessageKind14.Info);
                return;
            }
            if (_androidLanControl?.IsAuthorized == true)
            {
                if (ConnectionRouteBusy())
                {
                    ShowTopMessage14("Detén VisionEngine y LinkEngine antes de cambiar la conexión.", NLUIMessageKind14.Warning);
                    return;
                }
                await StopAndroidControlAsync();
            }
            _automaticUsbRecovery.Reset();
            _automaticUsb.Retry();
            QueueAutomaticUsb();
            ShowTopMessage14("Preparando conexión USB. Autoriza el teléfono si lo solicita.", NLUIMessageKind14.Info);
        }
        catch (Exception ex)
        {
            ShowTopMessage14("No se pudo preparar USB: " + ex.Message, NLUIMessageKind14.Error);
        }
    }

    private void HomeConnectionLan_Click(object sender, RoutedEventArgs e)
    {
        if (_closing || _androidControlPreparing || _androidLanRebinding) return;
        if (_androidLanControl?.IsAuthorized == true)
        {
            ShowTopMessage14("La conexión LAN ya está autorizada.", NLUIMessageKind14.Info);
            return;
        }
        if (ConnectionRouteBusy())
        {
            ShowTopMessage14("Detén VisionEngine y LinkEngine antes de cambiar la conexión.", NLUIMessageKind14.Warning);
            return;
        }
        PrepareAndroidLan_Click(sender, e);
    }

    private static bool IsPrivateLanAddress(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168));
    }

    /// <summary>
    /// Redes donde puede estar el teléfono. Los adaptadores virtuales (WSL/Hyper-V,
    /// VirtualBox, VMware) también son "Ethernet" con IP privada; sin descartarlos la
    /// PC ve varias redes, no reactiva la escucha LAN sola y el teléfono no la encuentra.
    /// </summary>
    private static NetworkInterface[] AndroidLanInterfaces()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 &&
                !IsVirtualAdapter(n))
            .ToArray();
        var routed = candidates.Where(n => n.GetIPProperties().GatewayAddresses
            .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))).ToArray();
        return (routed.Length > 0 ? routed : candidates)
            .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 0)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsVirtualAdapter(NetworkInterface adapter)
    {
        string text = adapter.Name + " " + adapter.Description;
        return new[] { "Hyper-V", "vEthernet", "VirtualBox", "VMware", "WSL", "Virtual Adapter", "Host-Only" }
            .Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static IPAddress[] GetAndroidLanAddresses() => AndroidLanInterfaces()
        .SelectMany(n => n.GetIPProperties().UnicastAddresses
            .Where(a => IsPrivateLanAddress(a.Address))
            .Select(a => a.Address))
        .Distinct().ToArray();

    private IPAddress? ChooseAndroidLanAddress(bool createInvitation = true)
    {
        // Conexión dinámica: la red LAN se elige sola según la prioridad de adaptadores
        // (misma regla que la escucha automática); no hay diálogo de selección.
        var addresses = AndroidLanInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses
                .Where(a => IsPrivateLanAddress(a.Address))
                .Select(a => a.Address))
            .Distinct().ToArray();
        if (addresses.Length == 0)
            throw new InvalidOperationException("Conecta la PC a una red local IPv4 privada por Wi-Fi o Ethernet.");
        string? chosen = NLControlLanAutoPolicy.ChooseAddress(addresses.Select(a => a.ToString()), null);
        return chosen is null ? null : IPAddress.Parse(chosen);
    }

    private async void PrepareAndroidLan_Click(object sender, RoutedEventArgs e)
    {
        if (_androidControlPreparing || _androidLanRebinding) return;
        _androidControlPreparing = true;
        NLControlTrustServer? created = null;
        try
        {
            IPAddress? address = ChooseAndroidLanAddress();
            if (address is null || _closing) return;
            await StopAndroidLanListenerAsync();
            long generation = _androidControlGeneration;
            _viewModel.RefreshAudioOutputOptions(_paths);
            var store = GetAndroidTrustStore();
            NLControlTrustServer? server = null;
            server = new NLControlTrustServer(address, request => ReferenceEquals(_androidLanControl, server)
                ? HandleAndroidControlAsync(request, generation, "LAN", server)
                : Task.FromResult(new NLControlReply(NLControlProtocol.Version, request.Id, false, "Sesión revocada.")), store);
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
                UpdateRuntimeButtons();
                AndroidEngineStateChanged();
                // A cable may already be attached while LAN owns the session.
                // Re-evaluate that physical device when LAN authorization ends:
                // track-devices does not emit a second attach event in this case.
                if (!server.IsAuthorized) QueueAutomaticUsb();
                if (server.IsAuthorized) RequestWaitingExternalVisionFromAndroid();
                if (server.IsAuthorized || server.IsClosed || !server.IsInvitationOpen)
                {
                    invitationDialog?.Close();
                }
            }));
            server.Start();
            store.SetListening(address.ToString(), true);
            var discovery = new NLControlLanDiscovery(
                new(Environment.MachineName, address.ToString(), server.Invitation.Port,
                    server.Invitation.Fingerprint), persistent: true);
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
            var details = new TextBlock { Text = $"{discoveryStatus}\n\nEl código caduca en 2 minutos, limita los intentos incorrectos por equipo y sólo autoriza una vinculación. El código y los datos de confianza viajan únicamente por TLS. Guardar esta PC en Android es opcional.\n\nSi Windows pide acceso de red, permite NOVORA sólo en tu red privada. No compartas el código.", TextWrapping = TextWrapping.Wrap };
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
            if (!server.IsAuthorized && store.Devices.Count == 0 && ReferenceEquals(_androidLanControl, server))
                AndroidControlStatus.Text = "Esperando un enlace autorizado; el código LAN temporal se cerró.";
        }
        catch (Exception ex)
        {
            if (created is not null && ReferenceEquals(_androidLanControl, created)) await StopAndroidLanListenerAsync();
            if (!_closing) AndroidControlStatus.Text = "No se pudo preparar LAN: " + ex.Message;
        }
        finally
        {
            _androidControlPreparing = false;
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
                ["-s", serial, "shell", "pm", "path", "com.linkcontrol.novora"], timeout.Token);
            if (!installed.Contains("package:com.linkcontrol.novora", StringComparison.Ordinal)) return false;
            var invitation = server.Invitation;
            var handoff = new NLControlLanCodeHandoff(
                Environment.MachineName, invitation.Host, invitation.Port,
                invitation.Fingerprint, server.PairingCode, invitation.ExpiresUnix);
            handoff.Validate();
            string payload = Convert.ToBase64String(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(handoff));
            string launch = await _adb.ExecuteRawAsync(
                ["-s", serial, "shell", "am", "start", "--user", "0", "-n",
                    "com.linkcontrol.novora/.LanCodeActivity", "--es", "novora.lan.code", payload],
                timeout.Token);
            return !launch.Contains("Error:", StringComparison.OrdinalIgnoreCase) &&
                !launch.Contains("Permission Denial", StringComparison.OrdinalIgnoreCase) &&
                !launch.Contains("SecurityException", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }
}
