using NOVORA.Models;
using NOVORA.Services;
using NOVORA.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace NOVORA;

public partial class MainWindow : Window
{
    private static readonly TimeSpan RecoveryCooldown = TimeSpan.FromMinutes(2);

    private readonly NovoraPaths _paths = new();
    private readonly SettingsService _settingsService = new();
    private readonly MonitorService _monitorService = new();
    private readonly OutputProfileService _outputProfileService = new();
    private readonly UpdateService _updateService = new();
    private readonly MainViewModel _viewModel = new();
    private readonly AdbService _adb;
    private readonly DeviceIdentityService _deviceIdentity;
    private readonly NetworkService _networkService;
    private readonly DeviceMetricsService _metricsService;
    private readonly DeviceStateService _deviceState;
    private readonly ScrcpyService _scrcpy;
    private readonly GnirehtetService _gnirehtet;
    private readonly GnirehtetRecoveryService _gnirehtetRecovery;
    private readonly DispatcherTimer _statusTimer;
    private DateTimeOffset _lastRecoveryAttemptUtc = DateTimeOffset.MinValue;
    private bool _closing;
    private bool _refreshingStatus;

    public MainWindow()
    {
        InitializeComponent();
        _adb = new AdbService(_paths);
        _deviceIdentity = new DeviceIdentityService(_adb, _settingsService);
        _networkService = new NetworkService(_adb);
        _metricsService = new DeviceMetricsService(_adb);
        _deviceState = new DeviceStateService(_networkService, _metricsService);
        _scrcpy = new ScrcpyService(_paths);
        _gnirehtet = new GnirehtetService(_paths);
        _gnirehtetRecovery = new GnirehtetRecoveryService(_adb, _gnirehtet);
        _statusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadSettings();
            LoadMonitors();
            await RefreshDevicesAsync(force: true);
            UpdateOutputProfile();
            UpdateRuntimeButtons();
            _statusTimer.Start();
            await RefreshStatusAsync();
        }
        catch (Exception ex) { _viewModel.ConnectionStatus = ex.Message; }
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        _viewModel.AudioEnabled = settings.AudioEnabled;
        _viewModel.Bitrate = settings.Bitrate;
        _viewModel.TargetFps = settings.TargetFps;
        _viewModel.MaxSize = settings.MaxSize;
        _viewModel.Theme = settings.Theme;
        ThemeService.Apply(settings.Theme);
    }

    private void LoadMonitors()
    {
        var monitors = _monitorService.GetMonitors();
        _viewModel.Monitors = monitors;
        var settings = _settingsService.Load();
        _viewModel.SelectedMonitor = monitors.FirstOrDefault(monitor => string.Equals(monitor.DeviceName, settings.SelectedMonitorDeviceName, StringComparison.OrdinalIgnoreCase))
            ?? monitors.FirstOrDefault(monitor => string.Equals(monitor.DisplayLabel, settings.SelectedMonitorLabel, StringComparison.OrdinalIgnoreCase))
            ?? _monitorService.GetBestMonitor(monitors);
    }

    private async Task RefreshDevicesAsync(bool force)
    {
        try
        {
            RefreshDevicesButton.IsEnabled = false;
            var devices = await _deviceIdentity.GetDevicesAsync(force);
            var settings = _settingsService.Load();
            _viewModel.Devices = devices;
            var selected = devices.FirstOrDefault(device => string.Equals(device.Serial, settings.SelectedDeviceSerial, StringComparison.OrdinalIgnoreCase))
                ?? devices.FirstOrDefault();
            _viewModel.Device = selected ?? new DeviceInfo();
            _viewModel.ConnectionStatus = selected is null ? "Sin dispositivo Android." : $"{selected.FriendlyName} conectado por {selected.ConnectionType}.";
            _deviceState.Invalidate();
            _lastRecoveryAttemptUtc = DateTimeOffset.MinValue;
            UpdateOutputProfile();
            UpdateRuntimeButtons();
        }
        catch (Exception ex)
        {
            _viewModel.Devices = Array.Empty<DeviceInfo>();
            _viewModel.Device = new DeviceInfo();
            _viewModel.ConnectionStatus = ex.Message;
            UpdateRuntimeButtons();
        }
        finally { RefreshDevicesButton.IsEnabled = true; }
    }

    private async Task RefreshStatusAsync()
    {
        if (_refreshingStatus) return;
        _refreshingStatus = true;
        try
        {
            var device = _viewModel.Device;
            UpdateRuntimeButtons();
            if (device is null || !device.Connected)
            {
                _redStatus.Text = "Sin dispositivo conectado.";
                _performanceStatus.Text = "Esperando dispositivo...";
                return;
            }

            NetworkStatus? network = null;
            DeviceMetrics? metrics = null;
            string? networkError = null;
            string? metricsError = null;
            try { network = await _deviceState.GetNetworkAsync(device); }
            catch (Exception ex) { networkError = ex.Message; }
            try { metrics = await _deviceState.GetMetricsAsync(device); }
            catch (Exception ex) { metricsError = ex.Message; }

            var tunnelForThisDevice = _gnirehtet.IsActive && string.Equals(_gnirehtet.ActiveSerial, device.Serial, StringComparison.OrdinalIgnoreCase);
            if (network is not null && tunnelForThisDevice && !network.InternetAvailable && await TryRecoverGnirehtetAsync(device))
            {
                _deviceState.Invalidate(device.Serial);
                try { network = await _deviceState.GetNetworkAsync(device); networkError = null; }
                catch (Exception ex) { network = null; networkError = ex.Message; }
            }

            if (network is null)
            {
                _redStatus.Text = string.IsNullOrWhiteSpace(networkError) ? "Estado de red no disponible." : "Red no disponible · " + networkError;
            }
            else
            {
                var internet = network.InternetAvailable ? "Internet OK" : "Internet sin respuesta";
                var latency = network.LatencyMs >= 0 ? $"{network.LatencyMs} ms" : "—";
                tunnelForThisDevice = _gnirehtet.IsActive && string.Equals(_gnirehtet.ActiveSerial, device.Serial, StringComparison.OrdinalIgnoreCase);
                var tunnel = tunnelForThisDevice ? "Internet USB activo" : "Internet USB inactivo";
                _redStatus.Text = $"{device.ConnectionType} · {internet} · {latency} · {tunnel}";
            }

            if (metrics is null)
            {
                _performanceStatus.Text = string.IsNullOrWhiteSpace(metricsError) ? "Datos no disponibles." : "Rendimiento no disponible.";
            }
            else
            {
                var memory = metrics.TotalMemoryKb > 0 ? $"RAM {metrics.UsedMemoryKb / 1024d:0}/{metrics.TotalMemoryKb / 1024d:0} MB" : "RAM —";
                _performanceStatus.Text = $"CPU {metrics.CpuPercent:0.#}% · {memory} · Batería {metrics.BatteryPercent}% · {metrics.BatteryTemperatureC:0.#} °C";
            }
        }
        finally
        {
            UpdateRuntimeButtons();
            _refreshingStatus = false;
        }
    }

    private async Task<bool> TryRecoverGnirehtetAsync(DeviceInfo device)
    {
        if (!_gnirehtet.IsActive || !string.Equals(_gnirehtet.ActiveSerial, device.Serial, StringComparison.OrdinalIgnoreCase)) return false;
        if (DateTimeOffset.UtcNow - _lastRecoveryAttemptUtc < RecoveryCooldown) return false;
        _lastRecoveryAttemptUtc = DateTimeOffset.UtcNow;
        try { return await _gnirehtetRecovery.RecoverAsync(device.Serial); }
        catch { return false; }
    }

    private void UpdateOutputProfile()
    {
        try
        {
            if (_viewModel.Device is not { Connected: true } device || _viewModel.SelectedMonitor is null)
            {
                _viewModel.OutputProfile = null;
                return;
            }
            _viewModel.OutputProfile = _outputProfileService.Calculate(device, _viewModel.SelectedMonitor, _viewModel.Bitrate, _viewModel.TargetFps, _viewModel.MaxSize);
        }
        catch { _viewModel.OutputProfile = null; }
    }

    private void SaveSelection()
    {
        var settings = _settingsService.Load();
        settings.SelectedDeviceSerial = _viewModel.Device?.Serial;
        settings.SelectedMonitorLabel = _viewModel.SelectedMonitor?.DisplayLabel;
        settings.SelectedMonitorDeviceName = _viewModel.SelectedMonitor?.DeviceName;
        settings.AudioEnabled = _viewModel.AudioEnabled;
        settings.Bitrate = _viewModel.Bitrate;
        settings.TargetFps = _viewModel.TargetFps;
        settings.MaxSize = _viewModel.MaxSize;
        settings.Theme = _viewModel.Theme;
        _settingsService.Save(settings);
    }

    private async void RefreshDevices_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync(force: true);

    private async void WifiAdb_Click(object sender, RoutedEventArgs e)
    {
        var device = _viewModel.Device;
        if (device is null || !device.Connected || device.IsWifiConnection)
        {
            MessageBox.Show(device?.IsWifiConnection == true ? "El dispositivo ya usa ADB por Wi-Fi." : "Conecta primero el teléfono por USB.", "NOVORA — ADB Wi-Fi", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            WifiAdbButton.IsEnabled = false;
            _viewModel.ConnectionStatus = "Preparando ADB por Wi-Fi...";
            var endpoint = await _adb.ConnectOverWifiAsync(device.Serial);
            await RefreshDevicesAsync(force: true);
            var wifi = _viewModel.Devices.FirstOrDefault(item => string.Equals(item.Serial, endpoint, StringComparison.OrdinalIgnoreCase));
            if (wifi is not null) _viewModel.Device = wifi;
            _viewModel.ConnectionStatus = "ADB Wi-Fi conectado. Ya puedes retirar el USB.";
            SaveSelection();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "NOVORA — ADB Wi-Fi", MessageBoxButton.OK, MessageBoxImage.Warning);
            _viewModel.ConnectionStatus = "ADB Wi-Fi no conectado.";
        }
        finally { WifiAdbButton.IsEnabled = true; }
    }

    private void Device_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _deviceState.Invalidate();
        _lastRecoveryAttemptUtc = DateTimeOffset.MinValue;
        UpdateOutputProfile();
        SaveSelection();
        _ = RefreshStatusAsync();
        UpdateRuntimeButtons();
    }

    private void Monitor_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateOutputProfile();
        SaveSelection();
    }

    private async void MainActionButton_Click(object sender, RoutedEventArgs e)
    {
        var device = _viewModel.Device;
        if (device is null || !device.Connected || _viewModel.SelectedMonitor is null)
        {
            MessageBox.Show("Selecciona un dispositivo y un monitor.", "NOVORA", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            MainActionButton.IsEnabled = false;
            if (_scrcpy.IsRunning(device.Serial)) await _scrcpy.StopAsync(device.Serial);
            else
            {
                UpdateOutputProfile();
                if (_viewModel.OutputProfile is null) throw new InvalidOperationException("No se pudo calcular el perfil de salida.");
                _scrcpy.StartOptimized(device, _viewModel.SelectedMonitor, _viewModel.OutputProfile, _viewModel.AudioEnabled);
            }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "NOVORA — Screen Mirroring", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { MainActionButton.IsEnabled = true; UpdateRuntimeButtons(); }
    }

    private async void Gnirehtet_Click(object sender, RoutedEventArgs e)
    {
        var device = _viewModel.Device;
        if (device is null || !device.Connected)
        {
            MessageBox.Show("Selecciona un dispositivo conectado.", "NOVORA — Internet USB", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            InternetUsbButton.IsEnabled = false;
            var activeForThisDevice = _gnirehtet.IsActive && string.Equals(_gnirehtet.ActiveSerial, device.Serial, StringComparison.OrdinalIgnoreCase);
            if (activeForThisDevice)
            {
                await _gnirehtet.StopAsync(device.Serial);
                _viewModel.GnirehtetStatus = "Detenido";
                _lastRecoveryAttemptUtc = DateTimeOffset.MinValue;
            }
            else
            {
                var result = await _gnirehtet.StartAsync(device, _viewModel.Devices.Count(item => item.Connected));
                _viewModel.GnirehtetStatus = result.Message;
                if (!result.Success) MessageBox.Show(result.Message, "NOVORA — Internet USB", MessageBoxButton.OK, MessageBoxImage.Warning);
                else _lastRecoveryAttemptUtc = DateTimeOffset.UtcNow;
            }
            _deviceState.Invalidate(device.Serial);
            await RefreshStatusAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "NOVORA — Internet USB", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { InternetUsbButton.IsEnabled = true; UpdateRuntimeButtons(); }
    }

    private void UpdateRuntimeButtons()
    {
        var device = _viewModel.Device;
        var connected = device is { Connected: true } && !string.IsNullOrWhiteSpace(device.Serial);
        var mirroring = connected && _scrcpy.IsRunning(device!.Serial);
        var internetUsb = connected && _gnirehtet.IsActive && string.Equals(_gnirehtet.ActiveSerial, device!.Serial, StringComparison.OrdinalIgnoreCase);
        MainActionButton.Content = mirroring ? "■  STOP" : "▶  PLAY";
        InternetUsbButton.Content = internetUsb ? "■  DETENER INTERNET USB" : "INTERNET USB";
    }

    private void Configuration_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_viewModel) { Owner = this };
        if (window.ShowDialog() == true)
        {
            UpdateOutputProfile();
            SaveSelection();
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var update = await _updateService.CheckForUpdatesAsync();
            if (update is null)
            {
                MessageBox.Show($"NOVORA {_updateService.CurrentVersion} ya está actualizado.", "NOVORA — Actualización", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show($"Disponible NOVORA {update.LatestVersion}.\n\n¿Descargar e instalar ahora?", "NOVORA — Actualización", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var progress = new Progress<int>(value => _viewModel.ConnectionStatus = $"Descargando actualización... {value}%");
            await _updateService.InstallAndRestartAsync(update, progress);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "NOVORA — Actualización", MessageBoxButton.OK, MessageBoxImage.Warning);
            _viewModel.ConnectionStatus = "No se pudo actualizar NOVORA.";
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Bitrate) or nameof(MainViewModel.TargetFps) or nameof(MainViewModel.MaxSize)) UpdateOutputProfile();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        _closing = true;
        _statusTimer.Stop();
        SaveSelection();
        try
        {
            await _scrcpy.StopAllAsync();
            if (_gnirehtet.IsActive) await _gnirehtet.StopAsync(_gnirehtet.ActiveSerial);
            await _adb.StopServerIfNoOtherDevicesAsync(_viewModel.Device?.Serial);
        }
        catch { }
        _scrcpy.Dispose();
        _gnirehtet.Dispose();
        _deviceState.Dispose();
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        e.Cancel = false;
        Close();
    }
}
