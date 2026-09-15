using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using NOVORA.Control;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private NLControlTrustStore? _androidTrustStore;
    private static string AndroidTrustDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NOVORA", "AndroidControl");

    private NLControlTrustStore GetAndroidTrustStore() =>
        _androidTrustStore ??= new NLControlTrustStore(AndroidTrustDirectory);

    private static bool IsAvailableAndroidLanAddress(IPAddress address) => IsPrivateLanAddress(address) &&
        NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == OperationalStatus.Up &&
            n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 &&
            n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(address)));

    private async Task RestoreAndroidTrustAsync()
    {
        if (_closing || AndroidControlSessionOpen || !File.Exists(Path.Combine(AndroidTrustDirectory, NLControlTrustStore.FileName))) return;
        try
        {
            var store = GetAndroidTrustStore();
            if (!store.ListenEnabled || store.Devices.Count == 0) return;
            if (!IPAddress.TryParse(store.LastHost, out var address) || !IsAvailableAndroidLanAddress(address))
            {
                AndroidControlStatus.Text = "La red guardada cambió. Activa reconexión LAN y vuelve a vincular si cambió la dirección de PC.";
                return;
            }
            await StartAndroidTrustedListenerAsync(address);
        }
        catch (Exception ex) { AndroidControlStatus.Text = "No se pudo recuperar la confianza LAN: " + ex.Message; }
    }

    private async Task StartAndroidTrustedListenerAsync(IPAddress address)
    {
        await StopAndroidControlAsync(preserveTrustListening: true);
        if (_closing) return;
        var store = GetAndroidTrustStore();
        long generation = _androidControlGeneration;
        var server = new NLControlTrustServer(address, request => HandleAndroidControlAsync(request, generation), store, allowPairing: false);
        _androidLanControl = server;
        server.StatusChanged += (_, status) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ReferenceEquals(_androidLanControl, server)) {
                AndroidControlStatus.Text = status;
                if (!server.IsAuthorized) { ResetAndroidFileTransfer(); _ = FinishAndroidRecordingAsync(); }
            }
        }));
        try
        {
            server.Start();
            store.SetListening(address.ToString(), true);
            AndroidControlStatus.Text = "Reconexión LAN activa para teléfonos autorizados. No hay invitación QR abierta.";
        }
        catch { await StopAndroidControlAsync(); throw; }
    }

    private async void ActivateAndroidTrust_Click(object sender, RoutedEventArgs e)
    {
        if (_androidControlPreparing || _androidInstalling || _closing) return;
        _androidControlPreparing = true;
        try
        {
            var store = GetAndroidTrustStore();
            if (store.Devices.Count == 0)
            {
                AndroidControlStatus.Text = "Primero enlaza con QR y elige Recordar esta PC en Android.";
                return;
            }
            IPAddress? address = ChooseAndroidLanAddress(createInvitation: false);
            if (address is not null) await StartAndroidTrustedListenerAsync(address);
        }
        catch (Exception ex) { AndroidControlStatus.Text = "No se pudo activar la reconexión: " + ex.Message; }
        finally { _androidControlPreparing = false; }
    }

    private async void ManageAndroidTrust_Click(object sender, RoutedEventArgs e)
    {
        if (_closing || _androidControlPreparing) return;
        _androidControlPreparing = true;
        try
        {
            var store = GetAndroidTrustStore();
            var devices = store.Devices.ToArray();
            if (devices.Length == 0) { AndroidControlStatus.Text = "No hay teléfonos con confianza guardada."; return; }
            var body = new StackPanel { Margin = new Thickness(22) };
            body.Children.Add(new TextBlock { Text = "Revocar elimina la autorización guardada en PC y cierra la sesión LAN actual. El teléfono necesitará un QR nuevo.", TextWrapping = TextWrapping.Wrap });
            var list = new System.Windows.Controls.ListBox { ItemsSource = devices, DisplayMemberPath = "Name", SelectedIndex = 0, Height = 180, Margin = new Thickness(0, 12, 0, 12) };
            body.Children.Add(list);
            var dialog = new Window { Owner = this, Title = "Teléfonos autorizados", Width = 510,
                SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = body };
            var revoke = new System.Windows.Controls.Button { Content = "REVOCAR TELÉFONO SELECCIONADO", Padding = new Thickness(10) };
            revoke.Click += (_, _) => { if (list.SelectedItem is not null) dialog.DialogResult = true; };
            body.Children.Add(revoke);
            if (dialog.ShowDialog() != true || list.SelectedItem is not NLControlTrustedDevice selected) return;
            bool enabled = store.ListenEnabled;
            string? host = store.LastHost;
            store.Revoke(selected.DeviceId);
            await StopAndroidControlAsync();
            if (enabled && store.Devices.Count > 0 && IPAddress.TryParse(host, out var address) && IsAvailableAndroidLanAddress(address))
                await StartAndroidTrustedListenerAsync(address);
            AndroidControlStatus.Text = "Teléfono revocado. Su autorización anterior ya no permite conectar.";
        }
        catch (Exception ex) { AndroidControlStatus.Text = "No se pudo revocar la autorización: " + ex.Message; }
        finally { _androidControlPreparing = false; }
    }
}
