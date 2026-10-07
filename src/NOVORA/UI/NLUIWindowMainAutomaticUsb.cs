// NOVORA_AUTOUSB_V1
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NOVORA.Control;

namespace NOVORA;

public partial class NLUIWindowMain
{
    private readonly NLControlUsbAutoPolicy _automaticUsb = new();
    private HashSet<string>? _automaticUsbOnline;
    private string _automaticUsbSnapshot = string.Empty;
    private bool _automaticUsbLoaded, _automaticUsbQueued, _automaticUsbBusy, _automaticUsbDirty;
    private long _automaticUsbServerEpoch = -1;

    private void InitializeAutomaticUsb()
    {
        Loaded += (_, _) => { _automaticUsbLoaded = true; QueueAutomaticUsb(); };
        Activated += (_, _) => QueueAutomaticUsb();
        Closed += (_, _) => { _automaticUsbLoaded = false; _automaticUsb.Pause(); };
    }

    // Reuses the already running track-devices subscription. Does not launch another ADB server.
    private void AutomaticUsbDevicesChanged(object? sender, string snapshot)
    {
        if (_closing) return;
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_closing) return;
            _automaticUsbSnapshot = snapshot;
            _automaticUsbOnline = NLControlUsbAutoPolicy.ParseOnline(snapshot);
            QueueAutomaticUsb();
        }));
    }

    private void QueueAutomaticUsb()
    {
        if (_closing) return;
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(QueueAutomaticUsb));
            return;
        }
        // USB is an optional transport. Never let hot-plug discovery replace
        // an already authorized LAN control session.
        if (!ShouldPrepareAutomaticUsb(_androidLanControl?.IsAuthorized == true)) return;
        string serial = NLControlUsbAutoPolicy.SelectPhysicalSerial(
            _viewModel.Devices.Select(device =>
                new NLControlUsbCandidate(device.Serial, device.Connected, device.IsWifiConnection)),
            _automaticUsbOnline,
            _viewModel.Device.Serial);
        if (string.IsNullOrWhiteSpace(serial) &&
            !string.IsNullOrWhiteSpace(_visionLanSerialVE) &&
            (_automaticUsbOnline is null || _automaticUsbOnline.Contains(_visionLanSerialVE)))
        {
            serial = _visionLanSerialVE;
        }
        var device = _viewModel.Devices.FirstOrDefault(candidate =>
            string.Equals(candidate.Serial, serial, StringComparison.OrdinalIgnoreCase));
        bool lanFailover = device?.IsWifiConnection == true &&
            string.Equals(serial, _visionLanSerialVE, StringComparison.OrdinalIgnoreCase);
        bool online = device is not null &&
            (_automaticUsbOnline is null || _automaticUsbOnline.Contains(serial));
        // Record every edge before coalescing work. Detach/attach invalidates an old await.
        _automaticUsb.Observe(
            serial,
            device?.Connected == true,
            device?.IsWifiConnection == true,
            online,
            NLControlUsbAutoPolicy.ParseTransportIdentity(_automaticUsbSnapshot, serial),
            allowWifi: lanFailover);
        _automaticUsbDirty = true;
        if (!_automaticUsbLoaded || _automaticUsbQueued || _automaticUsbBusy) return;
        _automaticUsbQueued = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _automaticUsbQueued = false;
            _ = DrainAutomaticUsbAsync();
        }));
    }

    private async Task DrainAutomaticUsbAsync()
    {
        if (_automaticUsbBusy || _closing) return;
        _automaticUsbBusy = true;
        try
        {
            // Drains notifications received during await; not a timed discovery loop.
            do
            {
                _automaticUsbDirty = false;
                if (!ShouldPrepareAutomaticUsb(_androidLanControl?.IsAuthorized == true)) continue;
                string serial = _automaticUsb.Serial;
                long epoch = _automaticUsb.Epoch;
                if (_androidControl is not null &&
                    (_androidControlSerial != serial || _automaticUsbServerEpoch != epoch))
                    await StopAutomaticUsbAsync();
                if (_closing || !_automaticUsb.IsCurrent(serial, epoch)) continue;
                if (_androidControl is { IsAuthorized: true } && _androidControlSerial == serial) continue;
                if (!_automaticUsb.TryBegin(_androidControlPreparing || _androidInstalling)) continue;
                await PrepareAutomaticUsbAsync(serial, epoch);
            }
            while (_automaticUsbDirty && !_closing);
        }
        catch (Exception ex)
        {
            if (!_closing) AndroidControlStatus.Text = "USB automatico: " + ex.GetType().Name + ". Usa Reintentar USB o reconecta el cable.";
        }
        finally { _automaticUsbBusy = false; }
    }

    private void CheckAutomaticUsb(string serial, long epoch, long generation)
    {
        var device = _viewModel.Devices.FirstOrDefault(candidate =>
            string.Equals(candidate.Serial, serial, StringComparison.OrdinalIgnoreCase));
        bool lanFailover = device?.IsWifiConnection == true &&
            string.Equals(serial, _visionLanSerialVE, StringComparison.OrdinalIgnoreCase);
        if (_closing || !_automaticUsb.IsCurrent(serial, epoch) || generation != _androidControlGeneration ||
            device is null || !device.Connected || (device.IsWifiConnection && !lanFailover) ||
            !string.Equals(device.Serial, serial, StringComparison.OrdinalIgnoreCase))
            throw new OperationCanceledException("USB cambio durante la preparacion.");
    }

    private static bool HasAutomaticUsbMapping(string output, bool rejectOtherDestination)
    {
        bool found = false;
        foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] columns = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 3 || columns[columns.Length - 2] != "tcp:27214") continue;
            if (columns[columns.Length - 1] != "tcp:27214")
            {
                if (rejectOtherDestination) throw new InvalidOperationException("27214 apunta a otro destino ADB; no se modifico esa ruta.");
                continue;
            }
            found = true;
        }
        return found;
    }

    internal static bool IsNovoraAndroidInstalled(string output)
        => output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Trim().StartsWith("package:", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("com.novora.appcontrol", StringComparison.OrdinalIgnoreCase));

    internal static string? ResolveNovoraUsbBootstrapComponent(string appControlOutput)
    {
        static bool Installed(string output, string package) => output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Trim().StartsWith("package:", StringComparison.OrdinalIgnoreCase) &&
                line.Contains(package, StringComparison.OrdinalIgnoreCase));

        if (Installed(appControlOutput, "com.novora.appcontrol"))
            return "com.novora.appcontrol/.UsbBootstrapActivity";
        return null;
    }

    internal static bool ShouldPrepareAutomaticUsb(bool lanAuthorized) => !lanAuthorized;

    private async Task PrepareAutomaticUsbAsync(string serial, long epoch)
    {
        _androidControlPreparing = true;
        NLControlTrustServer? created = null;
        try
        {
            bool lanFailover = string.Equals(serial, _visionLanSerialVE, StringComparison.OrdinalIgnoreCase) &&
                serial.Contains(':', StringComparison.Ordinal);
            string visibleTransport = lanFailover ? "LAN" : "USB";
            // LAN remains listening while USB is prepared. Android keeps one active
            // session and USB receives command priority only after authorization.
            if (_androidControl is not null) await StopAutomaticUsbAsync();
            long generation = _androidControlGeneration;
            CheckAutomaticUsb(serial, epoch, generation);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string state = await _adb.ExecuteRawAsync(new[] { "-s", serial, "get-state" }, deadline.Token);
            if (state.Trim() != "device") throw new InvalidOperationException("ADB no esta autorizado para este telefono.");
            CheckAutomaticUsb(serial, epoch, generation);
            string appControlPath = await _adb.ExecuteRawAsync(
                new[] { "-s", serial, "shell", "pm", "path", "com.novora.appcontrol" },
                deadline.Token);
            string? bootstrapComponent = ResolveNovoraUsbBootstrapComponent(appControlPath);
            if (bootstrapComponent is null)
                throw new FileNotFoundException("NOVORA Android no está instalada en el teléfono detectado.");
            CheckAutomaticUsb(serial, epoch, generation);
            string before = await _adb.ExecuteRawAsync(new[] { "-s", serial, "reverse", "--list" }, deadline.Token);
            bool reuseMapping = HasAutomaticUsbMapping(before, true);
            bool reuseVideoMapping = HasAppControlVideoMapping(before);
            bool reuseControlMapping = HasAppControlControlMapping(before);
            bool reuseAudioMapping = HasAppControlAudioMapping(before);
            CheckAutomaticUsb(serial, epoch, generation);
            _viewModel.RefreshAudioOutputOptions(_paths);
            created = new NLControlTrustServer(System.Net.IPAddress.Loopback,
                request => HandleAndroidControlAsync(request, generation, "USB"), GetAndroidUsbTrustStore(), NLControlProtocol.Port,
                allowRemember: () => _allowUsbRemember, transport: "USB");
            // Binding must succeed before adopting an existing exact reverse mapping.
            created.Start();
            _androidControl = created;
            _androidControlSerial = serial;
            _automaticUsbServerEpoch = epoch;
            var server = created;
            server.StatusChanged += (_, status) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ReferenceEquals(_androidControl, server)) return;
                AndroidControlStatus.Text = server.IsAuthorized && lanFailover
                    ? "LAN Failover activo; sesión sincronizada con Android."
                    : !server.IsAuthorized && _androidLanControl?.IsAuthorized == true
                    ? "LAN activa; USB físico detectado y en preparación."
                    : status;
                if (server.IsAuthorized)
                {
                    _ = EnsureExInStandaloneAsync();
                    if (!lanFailover)
                        _ = PrepareVisionLanFallbackVEAsync(serial, epoch, automatic: true);
                }
                if (!server.IsAuthorized && !AndroidControlAuthorized)
                {
                    ResetAndroidFileTransfer();
                    _ = FinishAndroidRecordingAsync();
                    _ = StopAndroidOwnedLinkAsync();
                }
                AndroidEngineStateChanged();
                if (server.IsClosed) _ = StopAutomaticUsbAsync();
            }));
            if (!reuseMapping)
                await _adb.ExecuteRawAsync(new[] { "-s", serial, "reverse", "--no-rebind", "tcp:27214", "tcp:27214" }, deadline.Token);
            _androidControlReverseOwned = !reuseMapping;
            if (!reuseVideoMapping)
            {
                await _adb.ExecuteRawAsync(new[] { "-s", serial, "reverse", "--no-rebind", "tcp:27215", "tcp:27215" }, deadline.Token);
                _androidVideoReverseOwned = true;
            }
            if (!reuseControlMapping)
            {
                await _adb.ExecuteRawAsync(new[] { "-s", serial, "reverse", "--no-rebind", "tcp:27216", "tcp:27216" }, deadline.Token);
                _androidControlInputReverseOwned = true;
            }
            if (!reuseAudioMapping)
            {
                await _adb.ExecuteRawAsync(new[] { "-s", serial, "reverse", "--no-rebind", "tcp:27217", "tcp:27217" }, deadline.Token);
                _androidAudioReverseOwned = true;
            }
            CheckAutomaticUsb(serial, epoch, generation);
            string after = await _adb.ExecuteRawAsync(new[] { "-s", serial, "reverse", "--list" }, deadline.Token);
            if (!HasAutomaticUsbMapping(after, false)) throw new InvalidOperationException("ADB no confirmo la ruta USB 27214.");
            if (!HasAppControlVideoMapping(after)) throw new InvalidOperationException("ADB no confirmo la ruta USB 27215 para video.");
            if (!HasAppControlControlMapping(after)) throw new InvalidOperationException("ADB no confirmo la ruta USB 27216 para control.");
            if (!HasAppControlAudioMapping(after)) throw new InvalidOperationException("ADB no confirmo la ruta USB 27217 para audio.");
            CheckAutomaticUsb(serial, epoch, generation);
            string bootstrap = Convert.ToBase64String(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new NLControlTunnelBootstrap(server.Invitation, visibleTransport)));
            // The protected entry point accepts shell/system callers; MainActivity never trusts external bootstrap extras.
            string launch = await _adb.ExecuteRawAsync(new[] { "-s", serial, "shell", "am", "start", "--user", "0", "-n",
                bootstrapComponent, "--es", "novora.usb", bootstrap }, deadline.Token);
            if (launch.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
                launch.Contains("Error type", StringComparison.OrdinalIgnoreCase) ||
                launch.Contains("Permission Denial", StringComparison.OrdinalIgnoreCase) ||
                launch.Contains("SecurityException", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Android no acepto la entrada USB protegida. Comprueba la APK instalada.");
            CheckAutomaticUsb(serial, epoch, generation);
            if (!server.IsAuthorized) AndroidControlStatus.Text = lanFailover
                ? "Failover LAN detectado. Sincronizando automáticamente NOVORA Android..."
                : "USB detectado. Verificando automaticamente NOVORA Android...";
            AndroidEngineStateChanged();
        }
        catch (Exception ex)
        {
            if (created is not null && ReferenceEquals(_androidControl, created))
                await StopAutomaticUsbAsync();
            else if (created is not null) await created.DisposeAsync();
            if (!_closing && _automaticUsb.IsCurrent(serial, epoch))
                AndroidControlStatus.Text = ex is FileNotFoundException
                    ? ex.Message
                    : "No se completo USB automatico (" + ex.GetType().Name + "). Revisa NOVORA-LINK y reintenta USB.";
        }
        finally { _androidControlPreparing = false; }
    }
}
