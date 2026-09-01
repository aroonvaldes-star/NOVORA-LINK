using NOVORA.Models;
using NOVORA.Services;
using NOVORA.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NOVORA;

public partial class MainWindow : Window
{
    private static readonly TimeSpan RecoveryCooldown =
        TimeSpan.FromMinutes(2);

    private readonly NovoraPaths _paths =
        new();

    private readonly SettingsService _settingsService =
        new();

    private readonly MonitorService _monitorService =
        new();

    private readonly OutputProfileService _outputProfileService =
        new();

    private readonly UpdateService _updateService =
        new();

    private readonly MainViewModel _viewModel =
        new();

    private readonly AdbService _adb;

    private readonly DeviceIdentityService _deviceIdentity;

    private readonly NetworkService _networkService;

    private readonly DeviceMetricsService _metricsService;

    private readonly DeviceStateService _deviceState;

    private readonly NCP _pollingCenter;

    private readonly ScrcpyService _scrcpy;

    private readonly GnirehtetService _gnirehtet;

    private readonly GnirehtetRecoveryService _gnirehtetRecovery;

    private DateTimeOffset _lastRecoveryAttemptUtc =
        DateTimeOffset.MinValue;

    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();

        _adb =
            new AdbService(
                _paths);

        _deviceIdentity =
            new DeviceIdentityService(
                _adb,
                _settingsService);

        _networkService =
            new NetworkService(
                _adb);

        _metricsService =
            new DeviceMetricsService(
                _adb);

        _deviceState =
            new DeviceStateService(
                _networkService,
                _metricsService);

        /*
         * NCP sustituye al DispatcherTimer local.
         *
         * Un solo centro de polling.
         * 30 segundos.
         * Una sola lectura compartida.
         */
        _pollingCenter =
            new NCP(
                _deviceState,
                () => _viewModel.Device,
                TimeSpan.FromSeconds(30));

        _pollingCenter.SnapshotUpdated +=
            PollingCenter_SnapshotUpdated;

        _scrcpy =
            new ScrcpyService(
                _paths);

        _gnirehtet =
            new GnirehtetService(
                _paths);

        _gnirehtetRecovery =
            new GnirehtetRecoveryService(
                _adb,
                _gnirehtet);

        DataContext =
            _viewModel;

        _viewModel.PropertyChanged +=
            ViewModel_PropertyChanged;
    }

    private async void Window_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            LoadSettings();

            LoadMonitors();

            await RefreshDevicesAsync(
                force: true);

            UpdateOutputProfile();

            UpdateRuntimeButtons();

            /*
             * Start() hace una primera lectura inmediata.
             * Después continúa cada 30 segundos.
             */
            _pollingCenter.Start();
        }
        catch (Exception ex)
        {
            _viewModel.ConnectionStatus =
                ex.Message;
        }
    }

    private void LoadSettings()
    {
        var settings =
            _settingsService.Load();

        _viewModel.AudioEnabled =
            settings.AudioEnabled;

        _viewModel.Bitrate =
            settings.Bitrate;

        _viewModel.TargetFps =
            settings.TargetFps;

        _viewModel.MaxSize =
            settings.MaxSize;

        _viewModel.Theme =
            settings.Theme;

        ThemeService.Apply(
            settings.Theme);
    }

    private void LoadMonitors()
    {
        var monitors =
            _monitorService.GetMonitors();

        _viewModel.Monitors =
            monitors;

        var settings =
            _settingsService.Load();

        _viewModel.SelectedMonitor =
            monitors.FirstOrDefault(
                monitor =>
                    string.Equals(
                        monitor.DeviceName,
                        settings.SelectedMonitorDeviceName,
                        StringComparison.OrdinalIgnoreCase))
            ??
            monitors.FirstOrDefault(
                monitor =>
                    string.Equals(
                        monitor.DisplayLabel,
                        settings.SelectedMonitorLabel,
                        StringComparison.OrdinalIgnoreCase))
            ??
            _monitorService.GetBestMonitor(
                monitors);
    }

    private async Task RefreshDevicesAsync(
        bool force)
    {
        try
        {
            RefreshDevicesButton.IsEnabled =
                false;

            var devices =
                await _deviceIdentity.GetDevicesAsync(
                    force);

            var settings =
                _settingsService.Load();

            _viewModel.Devices =
                devices;

            var selected =
                devices.FirstOrDefault(
                    device =>
                        string.Equals(
                            device.Serial,
                            settings.SelectedDeviceSerial,
                            StringComparison.OrdinalIgnoreCase))
                ??
                devices.FirstOrDefault();

            _viewModel.Device =
                selected ??
                new DeviceInfo();

            _viewModel.ConnectionStatus =
                selected is null
                    ? "Sin dispositivo Android."
                    : $"{selected.FriendlyName} conectado por {selected.ConnectionType}.";

            _deviceState.Invalidate();

            _pollingCenter.Invalidate();

            _lastRecoveryAttemptUtc =
                DateTimeOffset.MinValue;

            UpdateOutputProfile();

            UpdateRuntimeButtons();

            if (_pollingCenter.IsRunning)
            {
                await RefreshPollingNowSafeAsync();
            }
        }
        catch (Exception ex)
        {
            _viewModel.Devices =
                Array.Empty<DeviceInfo>();

            _viewModel.Device =
                new DeviceInfo();

            _viewModel.ConnectionStatus =
                ex.Message;

            UpdateRuntimeButtons();
        }
        finally
        {
            RefreshDevicesButton.IsEnabled =
                true;
        }
    }

    /*
     * NCP puede publicar desde un hilo que no es el Dispatcher de WPF.
     * El evento entra aquí y enviamos únicamente la actualización visual
     * al hilo de interfaz.
     */
    private async void PollingCenter_SnapshotUpdated(
        object? sender,
        NovoraCenterPollingSnapshot snapshot)
    {
        if (_closing)
        {
            return;
        }

        try
        {
            await Dispatcher.InvokeAsync(
                () =>
                    ApplyPollingSnapshot(
                        snapshot));

            if (_closing ||
                !snapshot.HasDevice ||
                snapshot.Device is null ||
                snapshot.Network is null)
            {
                return;
            }

            var device =
                snapshot.Device;

            var tunnelForThisDevice =
                _gnirehtet.IsActive &&
                string.Equals(
                    _gnirehtet.ActiveSerial,
                    device.Serial,
                    StringComparison.OrdinalIgnoreCase);

            /*
             * Conservamos el recovery que ya tenía MainWindow.
             * Sólo se activa cuando:
             *
             * - Gnirehtet está activo para ESTE dispositivo.
             * - Internet dejó de responder.
             * - Se cumple el cooldown.
             */
            if (tunnelForThisDevice &&
                !snapshot.Network.InternetAvailable &&
                await TryRecoverGnirehtetAsync(
                    device))
            {
                _deviceState.Invalidate(
                    device.Serial);

                await RefreshPollingNowSafeAsync();
            }
        }
        catch
        {
            /*
             * El polling no debe tumbar la interfaz.
             */
        }
    }

    private void ApplyPollingSnapshot(
        NovoraCenterPollingSnapshot snapshot)
    {
        if (_closing)
        {
            return;
        }

        UpdateRuntimeButtons();

        var device =
            snapshot.Device;

        if (device is null ||
            !device.Connected ||
            string.IsNullOrWhiteSpace(
                device.Serial))
        {
            _redStatus.Text =
                "Sin dispositivo conectado.";

            _performanceStatus.Text =
                "Esperando dispositivo...";

            return;
        }

        ApplyNetworkStatus(
            device,
            snapshot.Network,
            snapshot.Error);

        ApplyPerformanceStatus(
            snapshot.Metrics,
            snapshot.Error);
    }

    private void ApplyNetworkStatus(
        DeviceInfo device,
        NetworkStatus? network,
        string? error)
    {
        if (network is null)
        {
            _redStatus.Text =
                string.IsNullOrWhiteSpace(
                    error)
                    ? "Estado de red no disponible."
                    : "Red no disponible · " +
                      error;

            return;
        }

        var internet =
            network.InternetAvailable
                ? "Internet OK"
                : "Internet sin respuesta";

        var latency =
            network.LatencyMs >= 0
                ? $"{network.LatencyMs} ms"
                : "—";

        var tunnelForThisDevice =
            _gnirehtet.IsActive &&
            string.Equals(
                _gnirehtet.ActiveSerial,
                device.Serial,
                StringComparison.OrdinalIgnoreCase);

        var tunnel =
            tunnelForThisDevice
                ? "Internet USB activo"
                : "Internet USB inactivo";

        _redStatus.Text =
            $"{device.ConnectionType} · " +
            $"{internet} · " +
            $"{latency} · " +
            $"{tunnel}";
    }

    private void ApplyPerformanceStatus(
        DeviceMetrics? metrics,
        string? error)
    {
        if (metrics is null)
        {
            _performanceStatus.Text =
                string.IsNullOrWhiteSpace(
                    error)
                    ? "Datos no disponibles."
                    : "Rendimiento no disponible.";

            return;
        }

        /*
         * DeviceMetricsService devuelve KB.
         *
         * KB -> MB = / 1024
         * KB -> GB = / 1024 / 1024
         */
        var usedMemoryGb =
            metrics.UsedMemoryKb /
            1024d /
            1024d;

        var totalMemoryGb =
            metrics.TotalMemoryKb /
            1024d /
            1024d;

        var memory =
            metrics.TotalMemoryKb > 0
                ? $"RAM {usedMemoryGb:0.00}/{totalMemoryGb:0.00} GB"
                : "RAM —";

        _performanceStatus.Text =
            $"CPU {metrics.CpuPercent:0.#}% · " +
            $"{memory} · " +
            $"Batería {metrics.BatteryPercent}% · " +
            $"{metrics.BatteryTemperatureC:0.#} °C";
    }

    private async Task RefreshPollingNowSafeAsync()
    {
        if (_closing)
        {
            return;
        }

        try
        {
            await _pollingCenter.RefreshNowAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            if (_closing)
            {
                return;
            }

            try
            {
                await Dispatcher.InvokeAsync(
                    () =>
                        _viewModel.ConnectionStatus =
                            ex.Message);
            }
            catch
            {
            }
        }
    }

    private async Task<bool> TryRecoverGnirehtetAsync(
        DeviceInfo device)
    {
        if (!_gnirehtet.IsActive ||
            !string.Equals(
                _gnirehtet.ActiveSerial,
                device.Serial,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow -
            _lastRecoveryAttemptUtc <
            RecoveryCooldown)
        {
            return false;
        }

        _lastRecoveryAttemptUtc =
            DateTimeOffset.UtcNow;

        try
        {
            return await
                _gnirehtetRecovery.RecoverAsync(
                    device.Serial);
        }
        catch
        {
            return false;
        }
    }

    private void UpdateOutputProfile()
    {
        try
        {
            if (_viewModel.Device is not
                { Connected: true } device ||
                _viewModel.SelectedMonitor is null)
            {
                _viewModel.OutputProfile =
                    null;

                return;
            }

            _viewModel.OutputProfile =
                _outputProfileService.Calculate(
                    device,
                    _viewModel.SelectedMonitor,
                    _viewModel.Bitrate,
                    _viewModel.TargetFps,
                    _viewModel.MaxSize);
        }
        catch
        {
            _viewModel.OutputProfile =
                null;
        }
    }

    private void SaveSelection()
    {
        var settings =
            _settingsService.Load();

        settings.SelectedDeviceSerial =
            _viewModel.Device?.Serial;

        settings.SelectedMonitorLabel =
            _viewModel.SelectedMonitor?.DisplayLabel;

        settings.SelectedMonitorDeviceName =
            _viewModel.SelectedMonitor?.DeviceName;

        settings.AudioEnabled =
            _viewModel.AudioEnabled;

        settings.Bitrate =
            _viewModel.Bitrate;

        settings.TargetFps =
            _viewModel.TargetFps;

        settings.MaxSize =
            _viewModel.MaxSize;

        settings.Theme =
            _viewModel.Theme;

        _settingsService.Save(
            settings);
    }

    private async void RefreshDevices_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RefreshDevicesAsync(
            force: true);
    }

    private async void WifiAdb_Click(
        object sender,
        RoutedEventArgs e)
    {
        var device =
            _viewModel.Device;

        if (device is null ||
            !device.Connected ||
            device.IsWifiConnection)
        {
            MessageBox.Show(
                device?.IsWifiConnection == true
                    ? "El dispositivo ya usa ADB por Wi-Fi."
                    : "Conecta primero el telÃ©fono por USB.",
                "NOVORA â€” ADB Wi-Fi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        try
        {
            WifiAdbButton.IsEnabled =
                false;

            _viewModel.ConnectionStatus =
                "Preparando ADB por Wi-Fi...";

            var endpoint =
                await _adb.ConnectOverWifiAsync(
                    device.Serial);

            await RefreshDevicesAsync(
                force: true);

            var wifi =
                _viewModel.Devices.FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Serial,
                            endpoint,
                            StringComparison.OrdinalIgnoreCase));

            if (wifi is not null)
            {
                _viewModel.Device =
                    wifi;
            }

            _viewModel.ConnectionStatus =
                "ADB Wi-Fi conectado. Ya puedes retirar el USB.";

            SaveSelection();

            _deviceState.Invalidate();

            _pollingCenter.Invalidate();

            await RefreshPollingNowSafeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "NOVORA â€” ADB Wi-Fi",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _viewModel.ConnectionStatus =
                "ADB Wi-Fi no conectado.";
        }
        finally
        {
            WifiAdbButton.IsEnabled =
                true;
        }
    }

    private void Device_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        _deviceState.Invalidate();

        _pollingCenter.Invalidate();

        _lastRecoveryAttemptUtc =
            DateTimeOffset.MinValue;

        UpdateOutputProfile();

        SaveSelection();

        UpdateRuntimeButtons();

        if (_pollingCenter.IsRunning)
        {
            _ =
                RefreshPollingNowSafeAsync();
        }
    }

    private void Monitor_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateOutputProfile();

        SaveSelection();
    }

    private async void MainActionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var device =
            _viewModel.Device;

        if (device is null ||
            !device.Connected ||
            _viewModel.SelectedMonitor is null)
        {
            MessageBox.Show(
                "Selecciona un dispositivo y un monitor.",
                "NOVORA",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        try
        {
            MainActionButton.IsEnabled =
                false;

            if (_scrcpy.IsRunning(
                    device.Serial))
            {
                await _scrcpy.StopAsync(
                    device.Serial);
            }
            else
            {
                UpdateOutputProfile();

                if (_viewModel.OutputProfile is null)
                {
                    throw new InvalidOperationException(
                        "No se pudo calcular el perfil de salida.");
                }

                _scrcpy.StartOptimized(
                    device,
                    _viewModel.SelectedMonitor,
                    _viewModel.OutputProfile,
                    _viewModel.AudioEnabled);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "NOVORA â€” Screen Mirroring",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            MainActionButton.IsEnabled =
                true;

            UpdateRuntimeButtons();
        }
    }

    private async void Gnirehtet_Click(
        object sender,
        RoutedEventArgs e)
    {
        var device =
            _viewModel.Device;

        if (device is null ||
            !device.Connected)
        {
            MessageBox.Show(
                "Selecciona un dispositivo conectado.",
                "NOVORA â€” Internet USB",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        try
        {
            InternetUsbButton.IsEnabled =
                false;

            var activeForThisDevice =
                _gnirehtet.IsActive &&
                string.Equals(
                    _gnirehtet.ActiveSerial,
                    device.Serial,
                    StringComparison.OrdinalIgnoreCase);

            if (activeForThisDevice)
            {
                await _gnirehtet.StopAsync(
                    device.Serial);

                _viewModel.GnirehtetStatus =
                    "Detenido";

                _lastRecoveryAttemptUtc =
                    DateTimeOffset.MinValue;
            }
            else
            {
                var result =
                    await _gnirehtet.StartAsync(
                        device,
                        _viewModel.Devices.Count(
                            item =>
                                item.Connected));

                _viewModel.GnirehtetStatus =
                    result.Message;

                if (!result.Success)
                {
                    MessageBox.Show(
                        result.Message,
                        "NOVORA â€” Internet USB",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    _lastRecoveryAttemptUtc =
                        DateTimeOffset.UtcNow;
                }
            }

            _deviceState.Invalidate(
                device.Serial);

            await RefreshPollingNowSafeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "NOVORA â€” Internet USB",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            InternetUsbButton.IsEnabled =
                true;

            UpdateRuntimeButtons();
        }
    }

    private void UpdateRuntimeButtons()
    {
        var device =
            _viewModel.Device;

        var connected =
            device is { Connected: true } &&
            !string.IsNullOrWhiteSpace(
                device.Serial);

        var mirroring =
            connected &&
            _scrcpy.IsRunning(
                device!.Serial);

        var internetUsb =
            connected &&
            _gnirehtet.IsActive &&
            string.Equals(
                _gnirehtet.ActiveSerial,
                device!.Serial,
                StringComparison.OrdinalIgnoreCase);

        MainActionButton.Content =
            mirroring
                ? "⏹️ STOP"
                : "▶️ PLAY";

        InternetUsbButton.Content =
            internetUsb
                ? "â–   DETENER INTERNET USB"
                : "INTERNET USB";
    }

    private void Configuration_Click(
        object sender,
        RoutedEventArgs e)
    {
        var window =
            new SettingsWindow(
                _viewModel)
            {
                Owner = this
            };

        if (window.ShowDialog() == true)
        {
            UpdateOutputProfile();

            SaveSelection();
        }
    }

    private async void Update_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var update =
                await _updateService.CheckForUpdatesAsync();

            if (update is null)
            {
                MessageBox.Show(
                    $"NOVORA {_updateService.CurrentVersion} ya estÃ¡ actualizado.",
                    "NOVORA â€” ActualizaciÃ³n",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            if (MessageBox.Show(
                    $"Disponible NOVORA {update.LatestVersion}.\n\nÂ¿Descargar e instalar ahora?",
                    "NOVORA â€” ActualizaciÃ³n",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                return;
            }

            var progress =
                new Progress<int>(
                    value =>
                        _viewModel.ConnectionStatus =
                            $"Descargando actualizaciÃ³n... {value}%");

            await _updateService.InstallAndRestartAsync(
                update,
                progress);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "NOVORA â€” ActualizaciÃ³n",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _viewModel.ConnectionStatus =
                "No se pudo actualizar NOVORA.";
        }
    }

    private void ViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(MainViewModel.Bitrate)
            or nameof(MainViewModel.TargetFps)
            or nameof(MainViewModel.MaxSize))
        {
            UpdateOutputProfile();
        }
    }

    private void TitleBar_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ButtonState ==
            MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(
        object sender,
        RoutedEventArgs e)
    {
        WindowState =
            WindowState.Minimized;
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private async void Window_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        e.Cancel =
            true;

        _closing =
            true;

        SaveSelection();

        /*
         * Primero detenemos NCP para que no siga solicitando
         * métricas mientras cerramos ADB/Gnirehtet/scrcpy.
         */
        try
        {
            await _pollingCenter.StopAsync();
        }
        catch
        {
        }

        _pollingCenter.SnapshotUpdated -=
            PollingCenter_SnapshotUpdated;

        try
        {
            await _scrcpy.StopAllAsync();

            if (_gnirehtet.IsActive)
            {
                await _gnirehtet.StopAsync(
                    _gnirehtet.ActiveSerial);
            }

            await _adb.StopServerIfNoOtherDevicesAsync(
                _viewModel.Device?.Serial);
        }
        catch
        {
        }

        _scrcpy.Dispose();

        _gnirehtet.Dispose();

        try
        {
            await _pollingCenter.DisposeAsync();
        }
        catch
        {
        }

        _deviceState.Dispose();

        _viewModel.PropertyChanged -=
            ViewModel_PropertyChanged;

        e.Cancel =
            false;

        Close();
    }
}
