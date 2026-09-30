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
        HomeLanConnectionButton.Content = connected ? "CONECTADO POR LAN" : "CÓDIGO LAN";
        HomeLanConnectionButton.IsEnabled = !_androidControlPreparing && !_androidInstalling;
    }

    private static bool IsPrivateLanAddress(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168));
    }

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
                new(Environment.MachineName, address.ToString(), server.Invitation.Port),
                server.ResolvePairingCode);
            _androidLanDiscovery = discovery;
            string discoveryStatus;
            try { discovery.Start(); discoveryStatus = "Android puede buscar esta PC en la red."; }
            catch (Exception)
            {
                await discovery.DisposeAsync();
                _androidLanDiscovery = null;
                discoveryStatus = "La búsqueda no está disponible en esta red. Revisa el firewall privado de Windows.";
            }

            var body = new StackPanel();
            var title = new TextBlock { Text = $"{Environment.MachineName} · {address}\nEscribe este código en NOVORA Android:", FontSize = 17, TextWrapping = TextWrapping.Wrap };
            ApplyNovoraText(title);
            body.Children.Add(title);
            var code = new TextBlock { Text = server.PairingCode, FontSize = 42, FontWeight = FontWeights.Bold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 18) };
            ApplyNovoraText(code);
            body.Children.Add(code);
            var details = new TextBlock { Text = $"{discoveryStatus}\n\nEl código caduca en 2 minutos, admite hasta 5 intentos incorrectos y sólo autoriza una vinculación. Después PC y Android se recordarán mediante credenciales protegidas.\n\nSi Windows pide acceso de red, permite NOVORA sólo en tu red privada. No compartas el código.", TextWrapping = TextWrapping.Wrap };
            ApplyNovoraText(details, "MutedBrush");
            body.Children.Add(details);
            invitationDialog = CreateNovoraDialog("NOVORA Android · Código LAN", 510, body);
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
}
