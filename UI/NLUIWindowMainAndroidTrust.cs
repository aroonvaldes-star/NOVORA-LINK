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
    private NLControlTrustStore? _androidUsbTrustStore;
    private bool _androidLanNetworkSubscribed;
    private bool _androidLanRebinding;
    private NLControlTrustStore GetAndroidUsbTrustStore() => _androidUsbTrustStore ??= new NLControlTrustStore(Path.Combine(AndroidTrustDirectory, "USB"));
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
        if (!_androidLanNetworkSubscribed)
        {
            NetworkChange.NetworkAddressChanged += AndroidLanNetworkAddressChanged;
            _androidLanNetworkSubscribed = true;
        }
        if (_closing || AndroidControlSessionOpen || !File.Exists(Path.Combine(AndroidTrustDirectory, NLControlTrustStore.FileName))) return;
        try
        {
            var store = GetAndroidTrustStore();
            if (!store.ListenEnabled || store.Devices.Count == 0) return;
            IPAddress? address = IPAddress.TryParse(store.LastHost, out var saved) && IsAvailableAndroidLanAddress(saved)
                ? saved : GetAndroidLanAddresses() is { Length: 1 } addresses ? addresses[0] : null;
            if (address is null)
            {
                AndroidControlStatus.Text = "La red guardada cambió. Elige la red actual en Activar reconexión LAN.";
                return;
            }
            await StartAndroidTrustedListenerAsync(address);
        }
        catch (Exception ex) { AndroidControlStatus.Text = "No se pudo recuperar la confianza LAN: " + ex.Message; }
    }

    private void AndroidLanNetworkAddressChanged(object? sender, EventArgs e)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            if (_closing || _androidLanRebinding || _androidControlPreparing) return;
            _androidLanRebinding = true;
            try
            {
                var store = _androidTrustStore;
                if (store?.ListenEnabled != true || store.Devices.Count == 0 ||
                    IPAddress.TryParse(store.LastHost, out var current) && IsAvailableAndroidLanAddress(current)) return;
                IPAddress[] addresses = GetAndroidLanAddresses();
                if (addresses.Length == 1) await StartAndroidTrustedListenerAsync(addresses[0]);
                else AndroidControlStatus.Text = "La IP de la PC cambió. Elige la red actual en Activar reconexión LAN.";
            }
            catch (Exception ex) { AndroidControlStatus.Text = "No se pudo reactivar LAN tras cambiar de IP: " + ex.Message; }
            finally { _androidLanRebinding = false; }
        });
    }

    private void StopAndroidLanNetworkWatch()
    {
        if (!_androidLanNetworkSubscribed) return;
        NetworkChange.NetworkAddressChanged -= AndroidLanNetworkAddressChanged;
        _androidLanNetworkSubscribed = false;
    }

    private async Task StartAndroidTrustedListenerAsync(IPAddress address)
    {
        await StopAndroidControlAsync(preserveTrustListening: true);
        if (_closing) return;
        var store = GetAndroidTrustStore();
        long generation = _androidControlGeneration;
        var server = new NLControlTrustServer(address, request => HandleAndroidControlAsync(request, generation, "LAN"), store, allowPairing: false);
        _androidLanControl = server;
        server.StatusChanged += (_, status) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ReferenceEquals(_androidLanControl, server)) {
                AndroidControlStatus.Text = _androidControl?.IsAuthorized == true
                    ? "USB activo; LAN permanece disponible como respaldo."
                    : status;
                if (!server.IsAuthorized && !AndroidControlAuthorized) { ResetAndroidFileTransfer(); _ = FinishAndroidRecordingAsync(); }
                if (server.IsAuthorized) RequestWaitingExternalVisionFromAndroid();
                UpdateHomeLanConnectionButton();
                UpdateRuntimeButtons();
                AndroidEngineStateChanged();
            }
        }));
        try
        {
            server.Start();
            store.SetListening(address.ToString(), true);
            var discovery = new NLControlLanDiscovery(
                new(Environment.MachineName, address.ToString(), server.Port, store.Fingerprint),
                persistent: true);
            bool discoverable = true;
            try { discovery.Start(); _androidLanDiscovery = discovery; }
            catch
            {
                discoverable = false;
                await discovery.DisposeAsync();
            }
            AndroidControlStatus.Text = discoverable
                ? "Reconexión LAN activa para teléfonos autorizados. No hay código temporal abierto."
                : "Reconexión LAN activa; el descubrimiento no está disponible en esta red.";
            UpdateHomeLanConnectionButton();
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
                AndroidControlStatus.Text = "Primero enlaza mediante el código LAN de seis dígitos.";
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
            var usbStore = GetAndroidUsbTrustStore();
            var devices = store.Devices.Concat(usbStore.Devices.Select(d => d with { Name = d.Name + " · USB" })).ToArray();
            if (devices.Length == 0) { AndroidControlStatus.Text = "No hay teléfonos con confianza guardada."; return; }
            var body = new StackPanel();
            var intro = new TextBlock { Text = "Revocar elimina la autorización guardada en PC y cierra la sesión LAN actual. El teléfono necesitará un código LAN nuevo.", TextWrapping = TextWrapping.Wrap };
            ApplyNovoraText(intro);
            body.Children.Add(intro);
            var list = new System.Windows.Controls.ListBox { ItemsSource = devices, DisplayMemberPath = "Name", SelectedIndex = 0, Height = 180, Margin = new Thickness(0, 12, 0, 12) };
            ApplyNovoraInput(list);
            body.Children.Add(list);
            var dialog = CreateNovoraDialog("Teléfonos autorizados", 510, body);
            var revoke = new System.Windows.Controls.Button { Content = "REVOCAR TELÉFONO SELECCIONADO", Padding = new Thickness(10) };
            ApplyNovoraActionButton(revoke);
            revoke.Click += (_, _) => { if (list.SelectedItem is not null) dialog.DialogResult = true; };
            body.Children.Add(revoke);
            if (dialog.ShowDialog() != true || list.SelectedItem is not NLControlTrustedDevice selected) return;
            bool enabled = store.ListenEnabled;
            string? host = store.LastHost;
            if (usbStore.Contains(selected.DeviceId)) usbStore.Revoke(selected.DeviceId); else store.Revoke(selected.DeviceId);
            await StopAndroidControlAsync();
            if (enabled && store.Devices.Count > 0 && IPAddress.TryParse(host, out var address) && IsAvailableAndroidLanAddress(address))
                await StartAndroidTrustedListenerAsync(address);
            AndroidControlStatus.Text = "Teléfono revocado. Su autorización anterior ya no permite conectar.";
        }
        catch (Exception ex) { AndroidControlStatus.Text = "No se pudo revocar la autorización: " + ex.Message; }
        finally { _androidControlPreparing = false; }
    }
}
