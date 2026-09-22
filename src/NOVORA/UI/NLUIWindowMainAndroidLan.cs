using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NOVORA.Control;
using NOVORA.UI;

namespace NOVORA;

public partial class NLUIWindowMain
{
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
        var button = new System.Windows.Controls.Button { Content = createInvitation ? "CREAR INVITACIÓN QR" : "ACTIVAR RECONEXIÓN LAN", Padding = new Thickness(12) };
        ApplyNovoraActionButton(button);
        button.Click += (_, _) => dialog.DialogResult = true;
        body.Children.Add(button);
        return dialog.ShowDialog() == true ? addresses[choice.SelectedIndex].Address : null;
    }

    private async void PrepareAndroidLan_Click(object sender, RoutedEventArgs e)
    {
        if (_androidControlPreparing || _androidInstalling) return;
        _androidControlPreparing = true;
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
            var discovery = new NLControlLanDiscovery(new(Environment.MachineName, address.ToString(), server.Invitation.Port));
            _androidLanDiscovery = discovery;
            string discoveryStatus;
            try { discovery.Start(); discoveryStatus = "Android puede buscar esta PC en la red."; }
            catch (Exception)
            {
                await discovery.DisposeAsync();
                _androidLanDiscovery = null;
                discoveryStatus = "La búsqueda no está disponible. Puedes enlazar directamente escaneando el QR.";
            }

            var body = new StackPanel();
            var title = new TextBlock { Text = $"{Environment.MachineName} · {address}\nEscanea desde NOVORA Android en la misma red.", FontSize = 17, TextWrapping = TextWrapping.Wrap };
            ApplyNovoraText(title);
            body.Children.Add(title);
            var qr = new System.Windows.Controls.Image { Source = NLUIAndroidQr.Create(server.Invitation.Encode()), Width = 360, Height = 360, Margin = new Thickness(0, 12, 0, 12) };
            RenderOptions.SetBitmapScalingMode(qr, BitmapScalingMode.NearestNeighbor);
            body.Children.Add(qr);
            var copy = new System.Windows.Controls.Button { Content = "COPIAR INVITACIÓN", Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 12) };
            ApplyNovoraActionButton(copy);
            copy.Click += (_, _) =>
            {
                try
                {
                    System.Windows.Clipboard.SetText(server.Invitation.Encode());
                    copy.Content = "COPIADA · CONTIENE AUTORIZACIÓN TEMPORAL";
                }
                catch (Exception) { copy.Content = "NO SE PUDO COPIAR · USA EL QR"; }
            };
            body.Children.Add(copy);
            var details = new TextBlock { Text = $"{discoveryStatus}\n\nEl QR caduca en 2 minutos y solo se usa una vez. Cerrar esta ventana cancela el QR pendiente; los teléfonos recordados conservan su autorización.\n\nSi Windows pide acceso de red, permite NOVORA solo en tu red privada. El QR autoriza el control: no lo compartas.", TextWrapping = TextWrapping.Wrap };
            ApplyNovoraText(details, "MutedBrush");
            body.Children.Add(details);
            invitationDialog = CreateNovoraDialog("NOVORA Android · Enlace LAN seguro", 510, body);
            AndroidControlStatus.Text = "Invitación LAN abierta. Escanea el QR; todavía no hay un Android autorizado.";
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
        finally { _androidControlPreparing = false; }
    }
}
