using NOVORA.LinkEngine.Runtime;
using NOVORA.Models;
using NOVORA.Services;
using NOVORA.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NOVORA;

public partial class MainWindow : Window
{
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

    private readonly DeviceMetricsService _metricsService;

    private bool _closing;

    private bool _refreshingDevices;

    private bool _informationalPollingSuspended14;


    public MainWindow()
    {
        InitializeComponent();

        _adb =
            new AdbService(
                _paths);

        /*
         * LinkEngine usa la misma instancia ADB que NOVORA.
         */
        InitializeLinkEngineRuntimeLE();
        InitializeVisionEngineRuntimeVE();

        _deviceIdentity =
            new DeviceIdentityService(
                _adb,
                _settingsService);

        _metricsService =
            new DeviceMetricsService(
                _adb);

        DataContext =
            _viewModel;

        _viewModel.PropertyChanged +=
            ViewModel_PropertyChanged;

        ApplyResponsiveWindowBounds14();
    }


    /*
     * ============================================================
     * TAMAÑO RESPONSIVO 1.4
     * ============================================================
     */

    private void ApplyResponsiveWindowBounds14()
    {
        Rect workArea =
            SystemParameters.WorkArea;

        const double preferredWidth =
            920d;

        const double preferredHeight =
            560d;

        const double outerMargin =
            14d;

        double usableWidth =
            Math.Max(
                640d,
                workArea.Width -
                outerMargin * 2d);

        double usableHeight =
            Math.Max(
                480d,
                workArea.Height -
                outerMargin * 2d);

        MinWidth =
            Math.Min(
                820d,
                usableWidth);

        MinHeight =
            Math.Min(
                500d,
                usableHeight);

        Width =
            Math.Min(
                preferredWidth,
                usableWidth);

        Height =
            Math.Min(
                preferredHeight,
                usableHeight);

        MaxWidth =
            workArea.Width;

        MaxHeight =
            workArea.Height;

        Left =
            workArea.Left +
            Math.Max(
                0d,
                (workArea.Width - Width) / 2d);

        Top =
            workArea.Top +
            Math.Max(
                0d,
                (workArea.Height - Height) / 2d);
    }


    /*
     * ============================================================
     * INICIO
     * ============================================================
     */

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

            await InitializeVisionEngineOnLoadedVEAsync();

            UpdateOutputProfile();

            UpdateRuntimeButtons();

            await RefreshPerformanceOnceAsync();
        }
        catch (Exception ex)
        {
            _viewModel.ConnectionStatus =
                ex.Message;

            ResetPerformanceSurface14(
                "Rendimiento no disponible.");
        }
    }


    /*
     * ============================================================
     * CONFIGURACION
     * ============================================================
     */

    private void LoadSettings()
    {
        var settings =
            _settingsService.Load();

        _viewModel.AudioEnabled =
            settings.AudioEnabled;

        _viewModel.RefreshAudioOutputOptions(
            _paths);

        _viewModel.SelectedAudioOutput =
            settings.AudioEnabled
                ? settings.SelectedAudioOutput
                : NOVORA.VisionEngine.Audio.OutputAudioVE.DisabledValueVE;

        _viewModel.VideoPresentationMode =
            settings.VideoPresentationMode;

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


    /*
     * ============================================================
     * DISPOSITIVOS
     * ============================================================
     */

    private async Task RefreshDevicesAsync(
        bool force)
    {
        try
        {
            _refreshingDevices =
                true;

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

            UpdateOutputProfile();

            UpdateRuntimeButtons();
        }
        catch (Exception ex)
        {
            _viewModel.Devices =
                Array.Empty<DeviceInfo>();

            _viewModel.Device =
                new DeviceInfo();

            _viewModel.ConnectionStatus =
                ex.Message;

            UpdateOutputProfile();

            UpdateRuntimeButtons();

            ResetPerformanceSurface14(
                "Esperando dispositivo...");
        }
        finally
        {
            _refreshingDevices =
                false;

            RefreshDevicesButton.IsEnabled =
                true;
        }
    }


    private async void RefreshDevices_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RefreshDevicesAsync(
            force: true);

        await RefreshPerformanceOnceAsync();
    }


    /*
     * ============================================================
     * ADB POR WI-FI
     * ============================================================
     */

    private async void WifiAdb_Click(
        object sender,
        RoutedEventArgs e)
    {
        var device =
            _viewModel.Device;

        if (device is null ||
            !device.Connected)
        {
            MessageBox.Show(
                "Conecta primero el tel\u00E9fono por USB.",
                "NOVORA - ADB Wi-Fi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        if (device.IsWifiConnection)
        {
            MessageBox.Show(
                "El dispositivo ya utiliza ADB por Wi-Fi.",
                "NOVORA - ADB Wi-Fi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        if (IsLinkEngineActive())
        {
            MessageBox.Show(
                "Det\u00E9n LinkEngine antes de cambiar ADB de USB a Wi-Fi. " +
                "Despu\u00E9s puedes iniciar LinkEngine nuevamente.",
                "NOVORA - LinkEngine",
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
                "ADB por Wi-Fi conectado. Ya puedes retirar el cable USB.";

            SaveSelection();

            UpdateOutputProfile();

            UpdateRuntimeButtons();

            await RefreshPerformanceOnceAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "NOVORA - ADB Wi-Fi",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _viewModel.ConnectionStatus =
                "No se pudo conectar ADB por Wi-Fi.";
        }
        finally
        {
            WifiAdbButton.IsEnabled =
                true;
        }
    }


    private bool IsLinkEngineActive()
    {
        SessionRuntimeLE? session =
            _linkEngineRuntimeLE?
                .SessionLE;

        return
            session is not null &&
            session.State is not
                StateRuntimeLE.Stopped and not
                StateRuntimeLE.Failed;
    }


    /*
     * ============================================================
     * SELECCION DE DISPOSITIVO / MONITOR
     * ============================================================
     */

    private async void Device_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_closing ||
            _refreshingDevices)
        {
            return;
        }

        UpdateOutputProfile();

        SaveSelection();

        UpdateRuntimeButtons();

        await RefreshPerformanceOnceAsync();
    }


    private void Monitor_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateOutputProfile();

        SaveSelection();
    }


    /*
     * ============================================================
     * VISIONENGINE
     * ============================================================
     */

    private async void MainActionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var device = _viewModel.Device;

        if (device is null ||
            !device.Connected ||
            _viewModel.SelectedMonitor is null)
        {
            MessageBox.Show(
                "Selecciona un dispositivo Android y un monitor de salida.",
                "NOVORA",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        try
        {
            MainActionButton.IsEnabled = false;
            await ToggleVisionEngineVEAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "NOVORA - VisionEngine",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            MainActionButton.IsEnabled = true;
            UpdateRuntimeButtons();
        }
    }


    /*
     * ============================================================
     * ESTADO DE BOTONES
     * ============================================================
     */

    private void UpdateRuntimeButtons()
    {
        var device =
            _viewModel.Device;

        bool connected =
            device is
            {
                Connected: true
            } &&
            !string.IsNullOrWhiteSpace(
                device.Serial);

        bool mirroring =
            connected &&
            IsVisionEngineRunningVE();

        MainActionButton.Content =
            mirroring
                ? "\u25A0  DETENER"
                : "\u25B6  INICIAR";

        WifiAdbButton.IsEnabled =
            connected &&
            !device!.IsWifiConnection;

        UpdateLinkEngineButtonLE();
    }


    /*
     * ============================================================
     * PERFIL DE SALIDA
     * ============================================================
     */

    private void UpdateOutputProfile()
    {
        try
        {
            if (_viewModel.Device is not
                {
                    Connected: true
                } device ||
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


    /*
     * ============================================================
     * RENDIMIENTO
     * ============================================================
     *
     * No crea polling permanente.
     */

    private async Task RefreshPerformanceOnceAsync()
    {
        if (_closing ||
            _informationalPollingSuspended14)
        {
            return;
        }

        var device =
            _viewModel.Device;

        if (device is null ||
            !device.Connected ||
            string.IsNullOrWhiteSpace(
                device.Serial))
        {
            ResetPerformanceSurface14(
                "Esperando dispositivo...");

            return;
        }

        _performanceStatus.Text =
            "Leyendo rendimiento...";

        SetPerformanceReading14();

        try
        {
            DeviceMetrics metrics =
                await _metricsService.GetAsync(
                    device);

            if (_closing)
            {
                return;
            }

            ApplyPerformanceStatus(
                metrics);
        }
        catch (OperationCanceledException)
        {
            if (!_closing)
            {
                ResetPerformanceSurface14(
                    "Rendimiento no disponible.");
            }
        }
        catch
        {
            if (!_closing)
            {
                ResetPerformanceSurface14(
                    "Rendimiento no disponible.");
            }
        }
    }


    private void ApplyPerformanceStatus(
        DeviceMetrics metrics)
    {
        double usedMemoryGb =
            metrics.UsedMemoryKb /
            1024d /
            1024d;

        double totalMemoryGb =
            metrics.TotalMemoryKb /
            1024d /
            1024d;

        string cpu =
            metrics.CpuPercent > 0
                ? $"CPU {metrics.CpuPercent:0.#}%"
                : "CPU -";

        string memory =
            metrics.TotalMemoryKb > 0
                ? $"RAM {usedMemoryGb:0.00}/{totalMemoryGb:0.00} GB"
                : "RAM -";

        string battery =
            metrics.BatteryPercent > 0
                ? $"Bater\u00EDa {metrics.BatteryPercent}%"
                : "Bater\u00EDa -";

        string temperature =
            metrics.BatteryTemperatureC > 0
                ? $"{metrics.BatteryTemperatureC:0.#} \u00B0C"
                : "Temperatura -";

        _performanceStatus.Text =
            $"{cpu} | {memory}\n" +
            $"{battery} | {temperature}";

        ApplyPerformanceSurface14(metrics);
    }


    /*
     * ============================================================
     * GUARDAR SELECCION
     * ============================================================
     */

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

        settings.SelectedAudioOutput =
            _viewModel.SelectedAudioOutput;

        settings.VideoPresentationMode =
            _viewModel.VideoPresentationMode;

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


    /*
     * ============================================================
     * VENTANA DE CONFIGURACION
     * ============================================================
     */

    private void Configuration_Click(
        object sender,
        RoutedEventArgs e)
    {
        var window =
            new SettingsWindow(
                _viewModel)
            {
                Owner =
                    this
            };

        if (window.ShowDialog() == true)
        {
            UpdateOutputProfile();

            SaveSelection();

            UpdateRuntimeButtons();
        }
    }


    /*
     * ============================================================
     * ACTUALIZACIONES
     * ============================================================
     */

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
                    $"NOVORA {_updateService.CurrentVersion} ya est\u00E1 actualizado.",
                    "NOVORA - Actualizaci\u00F3n",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            if (MessageBox.Show(
                    $"Disponible NOVORA {update.LatestVersion}.\n\n" +
                    "\u00BFDescargar e instalar ahora?",
                    "NOVORA - Actualizaci\u00F3n",
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
                            $"Descargando actualizaci\u00F3n... {value}%");

            await _updateService.InstallAndRestartAsync(
                update,
                progress);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "NOVORA - Actualizaci\u00F3n",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _viewModel.ConnectionStatus =
                "No se pudo actualizar NOVORA.";
        }
    }


    /*
     * ============================================================
     * VIEWMODEL
     * ============================================================
     */

    private void ViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(MainViewModel.Bitrate)
            or nameof(MainViewModel.TargetFps)
            or nameof(MainViewModel.MaxSize)
            or nameof(MainViewModel.AudioEnabled))
        {
            UpdateOutputProfile();
        }
    }


    /*
     * ============================================================
     * VENTANA
     * ============================================================
     */

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


    /*
     * ============================================================
     * CIERRE
     * ============================================================
     */

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
         * LinkEngine se detiene primero porque necesita limpiar
         * el transporte y adb reverse.
         */
        try
        {
            await ShutdownLinkEngineRuntimeLEAsync();
        }
        catch
        {
        }

        try
        {
            await ShutdownVisionEngineRuntimeVEAsync();

            await _adb.StopServerIfNoOtherDevicesAsync(
                _viewModel.Device?.Serial);
        }
        catch
        {
        }

        _viewModel.PropertyChanged -=
            ViewModel_PropertyChanged;

        e.Cancel =
            false;

        Close();
    }
}