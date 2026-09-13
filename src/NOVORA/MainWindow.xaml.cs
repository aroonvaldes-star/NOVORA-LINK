using NOVORA.Models;
using NOVORA.Services;
using NOVORA.Discovery;
using NOVORA.ViewModels;
using NOVORA.STEngine.Core;
using NOVORA.VisionEngine.Gamepad;
using NOVORA.VisionEngine.Integration;
using NOVORA.VisionEngine.Nvidia;
using NOVORA.VisionEngine.Privacy;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NOVORA;

public enum MessageKind14
{
    Info,
    Success,
    Warning,
    Error
}

public partial class MainWindow : Window
{
    private readonly NovoraPaths _paths = new();
    private readonly SettingsService _settingsService = new();
    private readonly MonitorService _monitorService = new();
    private readonly OutputProfileService _outputProfileService = new();
    private readonly UpdateService _updateService = new();
    private readonly MainViewModel _viewModel = new();
    private readonly AdbService _adb;
    private readonly DeviceIdentityService _deviceIdentity;
    private readonly DeviceMetricsService _metricsService;
    private readonly EngineCoreST _stEngineST = new();

    private bool _closing;
    private bool _shellInitialized14;
    private bool _informationalPollingSuspended14;
    private string _selectedPage14 = "Home";
    private CancellationTokenSource? _refreshCts14;
    private LanDiscoveryResponderNV? _lanDiscoveryResponderNV;

    public MainWindow()
    {
        _adb = new AdbService(_paths);
        _deviceIdentity = new DeviceIdentityService(_adb, _settingsService);
        _metricsService = new DeviceMetricsService(_adb);

        InitializeComponent();

        InitializeAndroidShareListNV();

        DataContext = _viewModel;

        ThemeService.Apply(_viewModel.Theme);

        InitializeLinkEngineRuntimeLE();
        InitializeVisionEngineRuntimeVE();
    }

    private async void Window_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        _shellInitialized14 = true;

        LoadSettingsToViewModel14();
        ApplyTheme14(_viewModel.Theme);
        UpdateRuntimeButtons();
        ApplySTEngineShell14();
        ShowPage14(_selectedPage14);

        _viewModel.PropertyChanged +=
            ShellViewModel_PropertyChanged14;

        Closed += ShellWindow_Closed14;

        await RefreshDevicesAsync(
            force: true);

        await RefreshPerformanceOnceAsync();

        await StartDiscoveryLifecycleNVAsync();
        _lanDiscoveryResponderNV = new LanDiscoveryResponderNV();
        _lanDiscoveryResponderNV.StartNV();
    }

    private async void Window_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _refreshCts14?.Cancel();

        _viewModel.PropertyChanged -=
            ShellViewModel_PropertyChanged14;

        try
        {
            await StopDiscoveryLifecycleNVAsync();
            if (_lanDiscoveryResponderNV is not null)
            {
                await _lanDiscoveryResponderNV.DisposeAsync();
                _lanDiscoveryResponderNV = null;
            }
            await ShutdownRemoteAndroidNVAsync();
            await ShutdownLinkEngineRuntimeLEAsync();
            await ShutdownVisionEngineRuntimeVEAsync();
        }
        catch
        {
        }
    }

    private void ShellWindow_Closed14(
        object? sender,
        EventArgs e)
    {
        Closed -= ShellWindow_Closed14;
    }

    private void ShellRoot_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        ShowPage14(_selectedPage14);
    }

    private void TitleBar_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState =
                WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;

            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
        }
    }

    private void Minimize_Click(
        object sender,
        RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
        => Close();

    private async void RefreshDevices_Click(
        object sender,
        RoutedEventArgs e)
        => await RefreshDevicesAsync(
            force: true);

    private async void Device_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_shellInitialized14)
        {
            return;
        }

        QueueSaveSelection14();
        UpdateRuntimeButtons();
        await RefreshPerformanceOnceAsync();
    }

    private async void MainActionButton_Click(
        object sender,
        RoutedEventArgs e)
        => await ToggleVisionEngineVEAsync();

    private void Configuration_Click(
        object sender,
        RoutedEventArgs e)
    {
        _ = FadeToPage14("Settings");
    }

    private async void NavigationButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button &&
            button.Tag is string page &&
            !string.IsNullOrWhiteSpace(page))
        {
            await FadeToPage14(page);
        }
    }

    private void ShellTheme_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_shellInitialized14)
        {
            return;
        }

        ApplyTheme14(_viewModel.Theme);
        QueueSaveSelection14();
    }

    private void ShellVideoOption_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_shellInitialized14)
        {
            return;
        }

        RecalculateOutputProfile14();
        ApplyAdvancedVisionSettingsVE();
        QueueSaveSelection14();
    }

    private void ShellMonitor_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_shellInitialized14)
        {
            return;
        }

        RecalculateOutputProfile14();
        QueueSaveSelection14();
    }

    private void SaveShellSettings_Click(
        object sender,
        RoutedEventArgs e)
    {
        SaveSettingsFromViewModel14();
        ShowTopMessage14(
            "Configuracion guardada.",
            MessageKind14.Success);
    }

    private void InstallUpdateBanner_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowTopMessage14(
            "Instalador de actualizacion no disponible en esta compilacion.",
            MessageKind14.Info);
    }

    private void ShellViewModel_PropertyChanged14(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (!_shellInitialized14)
        {
            return;
        }

        if (string.Equals(
                e.PropertyName,
                nameof(MainViewModel.Theme),
                StringComparison.Ordinal))
        {
            ApplyTheme14(_viewModel.Theme);
        }
    }

    private async Task RefreshDevicesAsync(
        bool force = false)
    {
        _refreshCts14?.Cancel();
        _refreshCts14?.Dispose();
        _refreshCts14 = new CancellationTokenSource();

        CancellationToken cancellationToken =
            _refreshCts14.Token;

        try
        {
            _viewModel.ConnectionStatus =
                "Buscando dispositivos...";

            IReadOnlyList<DeviceInfo> devices =
                await _adb.GetDevicesAsync(
                    cancellationToken,
                    forceRefresh: force);

            _viewModel.Devices =
                devices;

            DeviceInfo? selected =
                devices.FirstOrDefault(
                    device =>
                        string.Equals(
                            device.Serial,
                            _viewModel.Device.Serial,
                            StringComparison.OrdinalIgnoreCase));

            _viewModel.Device =
                selected ??
                devices.FirstOrDefault() ??
                new DeviceInfo();

            _viewModel.RefreshOutputCapabilityOptions();
            RecalculateOutputProfile14();
            UpdateRuntimeButtons();
            await ConfigureRemoteAndroidForSelectedDeviceNVAsync();

            _viewModel.ConnectionStatus =
                _viewModel.Device.Connected
                    ? $"Conectado: {_viewModel.Device.FriendlyName}"
                    : "Sin dispositivo Android.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _viewModel.ConnectionStatus =
                $"ADB no disponible: {ex.Message}";
        }
    }

    private async Task RefreshPerformanceOnceAsync()
    {
        if (_informationalPollingSuspended14)
        {
            return;
        }

        DeviceInfo device =
            _viewModel.Device;

        if (!device.Connected ||
            string.IsNullOrWhiteSpace(device.Serial))
        {
            ResetPerformanceSurface14(
                "Sin dispositivo para medir.");

            return;
        }

        try
        {
            SetPerformanceReading14();

            DeviceMetrics metrics =
                await _metricsService.GetAsync(
                    device);

            ApplyPerformanceSurface14(
                metrics);
        }
        catch (Exception ex)
        {
            ResetPerformanceSurface14(
                $"Metricas no disponibles: {ex.Message}");
        }
    }

    private void QueueSaveSelection14()
        => SaveSettingsFromViewModel14();

    private void LoadSettingsToViewModel14()
    {
        NovoraSettings settings =
            _settingsService.Load();

        _viewModel.Theme =
            settings.Theme;
        _viewModel.VideoPresentationMode =
            settings.VideoPresentationMode;
        _viewModel.Bitrate =
            settings.Bitrate;
        _viewModel.TargetFps =
            settings.TargetFps;
        _viewModel.MaxSize =
            settings.MaxSize;
        _viewModel.SelectedAudioOutput =
            settings.SelectedAudioOutput;
        _viewModel.AudioEnabled =
            settings.AudioEnabled;
        _viewModel.PrivacyShieldEnabled =
            settings.PrivacyShieldEnabled;
        _viewModel.IntegrationClipboardEnabled =
            settings.IntegrationClipboardEnabled;
        _viewModel.IntegrationFileTransferEnabled =
            settings.IntegrationFileTransferEnabled;
        _viewModel.IntegrationDragDropEnabled =
            settings.IntegrationDragDropEnabled;
        _viewModel.IntegrationApplicationsEnabled =
            settings.IntegrationApplicationsEnabled;
        _viewModel.IntegrationNotificationsEnabled =
            settings.IntegrationNotificationsEnabled;
        _viewModel.IntegrationDynamicResizeEnabled =
            settings.IntegrationDynamicResizeEnabled;
        _viewModel.GamepadEnabled =
            settings.GamepadEnabled;
        _viewModel.RemoteAndroidEnabled =
            settings.RemoteAndroidEnabled;
        _viewModel.NvidiaProfile =
            settings.NvidiaProfile;

        _viewModel.Monitors =
            _monitorService.GetMonitors();

        _viewModel.SelectedMonitor =
            _viewModel.Monitors.FirstOrDefault(
                monitor =>
                    string.Equals(
                        monitor.DeviceName,
                        settings.SelectedMonitorDeviceName,
                        StringComparison.OrdinalIgnoreCase)) ??
            _viewModel.Monitors.FirstOrDefault();

        _viewModel.RefreshAudioOutputOptions(
            _paths);

        RecalculateOutputProfile14();
    }

    private void SaveSettingsFromViewModel14()
    {
        _settingsService.Save(
            new NovoraSettings
            {
                Theme = _viewModel.Theme,
                VideoPresentationMode = _viewModel.VideoPresentationMode,
                Bitrate = _viewModel.Bitrate,
                TargetFps = _viewModel.TargetFps,
                MaxSize = _viewModel.MaxSize,
                SelectedAudioOutput = _viewModel.SelectedAudioOutput,
                AudioEnabled = _viewModel.AudioEnabled,
                SelectedMonitorLabel = _viewModel.SelectedMonitor?.DisplayLabel,
                SelectedMonitorDeviceName = _viewModel.SelectedMonitor?.DeviceName,
                SelectedDeviceSerial = _viewModel.Device.Serial,
                PrivacyShieldEnabled = _viewModel.PrivacyShieldEnabled,
                IntegrationClipboardEnabled = _viewModel.IntegrationClipboardEnabled,
                IntegrationFileTransferEnabled = _viewModel.IntegrationFileTransferEnabled,
                IntegrationDragDropEnabled = _viewModel.IntegrationDragDropEnabled,
                IntegrationApplicationsEnabled = _viewModel.IntegrationApplicationsEnabled,
                IntegrationNotificationsEnabled = _viewModel.IntegrationNotificationsEnabled,
                IntegrationDynamicResizeEnabled = _viewModel.IntegrationDynamicResizeEnabled,
                GamepadEnabled = _viewModel.GamepadEnabled,
                RemoteAndroidEnabled = _viewModel.RemoteAndroidEnabled,
                NvidiaProfile = _viewModel.NvidiaProfile
            });
    }

    private void RecalculateOutputProfile14()
    {
        if (!_viewModel.Device.Connected ||
            string.IsNullOrWhiteSpace(
                _viewModel.Device.Serial))
        {
            _viewModel.OutputProfile =
                null;

            return;
        }

        _viewModel.OutputProfile =
            _outputProfileService.Calculate(
                _viewModel.Device,
                _viewModel.SelectedMonitor,
                _viewModel.Bitrate,
                _viewModel.TargetFps,
                _viewModel.MaxSize);
    }

    private void UpdateOutputProfile()
        => RecalculateOutputProfile14();

    private void SaveSelection()
        => SaveSettingsFromViewModel14();

    private void ApplyTheme14(
        string? theme)
        => ThemeService.Apply(theme);

    private async Task FadeToPage14(
        string page)
    {
        _selectedPage14 =
            string.IsNullOrWhiteSpace(page)
                ? "Home"
                : page;

        ShowPage14(_selectedPage14);

        if (string.Equals(
                _selectedPage14,
                "Performance",
                StringComparison.OrdinalIgnoreCase))
        {
            await RefreshPerformanceOnceAsync();
        }
    }

    private void ShowPage14(
        string page)
    {
        SetPageVisibility14(HomePage14, page, "Home");
        SetPageVisibility14(ScreenPage14, page, "Screen");
        SetPageVisibility14(NetworkPage14, page, "Network");
        SetPageVisibility14(PerformancePage14, page, "Performance");
        SetPageVisibility14(GameInputPage14, page, "GameInput");
        SetPageVisibility14(IntegrationPage14, page, "Integration");
        SetPageVisibility14(PrivacyPage14, page, "Privacy");
        SetPageVisibility14(SettingsPage14, page, "Settings");
    }

    private static void SetPageVisibility14(
        FrameworkElement pageElement,
        string current,
        string target)
    {
        pageElement.Visibility =
            string.Equals(
                current,
                target,
                StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void UpdateRuntimeButtons()
    {
        bool hasDevice =
            _viewModel.Device.Connected;

        MainActionButton.IsEnabled =
            hasDevice;

        MainActionButton.Content =
            IsVisionEngineRunningVE()
                ? "DETENER"
                : "INICIAR";

        RefreshDevicesButton.IsEnabled =
            !_closing;

        LinkEngineTestButton.IsEnabled =
            hasDevice && !_closing;

        WifiAdbButton.IsEnabled =
            hasDevice && !_closing;
    }

    private void ShowTopMessage14(
        string message,
        MessageKind14 kind = MessageKind14.Info)
    {
        TopMessageText14.Text =
            message;

        TopMessageDot14.Fill =
            kind switch
            {
                MessageKind14.Success =>
                    FindResource("GreenBrush") as System.Windows.Media.Brush,

                MessageKind14.Warning =>
                    FindResource("OrangeBrush") as System.Windows.Media.Brush,

                MessageKind14.Error =>
                    FindResource("DangerForegroundBrush") as System.Windows.Media.Brush,

                _ =>
                    FindResource("BlueBrush") as System.Windows.Media.Brush
            };
    }

    private void ApplyPrivacyShell14(
        StatusPrivacyVE status)
    {
        PrivacyStatusText14.Text =
            $"{status.State} - {status.Classification}\n{status.Message}";
    }

    private void ApplyGamepadShell14(
        StatusGamepadVE status)
    {
        GamepadStatusText14.Text =
            string.IsNullOrWhiteSpace(status.LastError)
                ? status.Message
                : $"{status.Message} - {status.LastError}";

        GamepadCountText14.Text =
            $"Mandos conectados: {status.ConnectedGamepads} - Reportes enviados: {status.ReportsSent:N0}";
    }

    private void ApplyIntegrationShell14(
        StatusIntegrationVE status)
    {
        CapabilitiesIntegrationVE c =
            status.Capabilities;

        string[] active =
        {
            c.Clipboard ? "Clipboard" : string.Empty,
            c.FileTransfer ? "Archivos" : string.Empty,
            c.DragDrop ? "Drag & Drop" : string.Empty,
            c.DynamicResize ? "Resize" : string.Empty,
            c.Camera ? "Camara" : string.Empty,
            c.AndroidMicrophone ? "Microfono Android" : string.Empty,
            c.PcMicrophoneToAndroid ? "Microfono PC -> Android" : string.Empty
        };

        IntegrationStatusText14.Text =
            string.Join(
                " - ",
                active.Where(
                    item =>
                        !string.IsNullOrWhiteSpace(item)));
    }

    private void ApplyNvidiaShell14(
        StatusNvidiaVE status)
    {
        CapabilitiesNvidiaVE c =
            status.Capabilities;

        NvidiaStatusTitle14.Text =
            status.Message;

        NvidiaStatusDot14.Fill =
            c.CudaInitialized
                ? FindResource("GreenBrush") as System.Windows.Media.Brush
                : FindResource("MutedBrush") as System.Windows.Media.Brush;

        NvidiaCapabilitiesText14.Text =
            $"CUDA: {(c.CudaInitialized ? "Si" : "No")} - " +
            $"NVDEC: {(c.NvdecApiAvailable ? "Si" : "No")} - " +
            $"NVENC: {(c.NvencApiAvailable ? "Si" : "No")}";

        NvidiaBackendText14.Text =
            $"Backend: {c.Backend}";

        NvidiaPipelineText14.Text =
            $"Pipeline: {status.Pipeline.Profile}";
    }

    private void ManualPrivacy_Click(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.PrivacyShieldEnabled =
            !_viewModel.PrivacyShieldEnabled;

        ApplyAdvancedVisionSettingsVE();
        QueueSaveSelection14();
    }

    private void ShellPrivacy_StatusChangedVE(
        object? sender,
        StatusPrivacyVE status)
        => Dispatcher.Invoke(
            () =>
                ApplyPrivacyShell14(status));

    private void ShellGamepad_StatusChangedVE(
        object? sender,
        StatusGamepadVE status)
        => Dispatcher.Invoke(
            () =>
                ApplyGamepadShell14(status));

    private void ShellIntegration_StatusChangedVE(
        object? sender,
        StatusIntegrationVE status)
        => Dispatcher.Invoke(
            () =>
                ApplyIntegrationShell14(status));

    private void ShellNvidia_StatusChangedVE(
        object? sender,
        StatusNvidiaVE status)
        => Dispatcher.Invoke(
            () =>
                ApplyNvidiaShell14(status));

    private void NvidiaProfile_SelectionChanged14(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_shellInitialized14)
        {
            return;
        }

        ApplyAdvancedVisionSettingsVE();
        QueueSaveSelection14();
    }

    private void WifiAdb_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowTopMessage14(
            "ADB por Wi-Fi se configura desde LinkEngine/Remote Android.",
            MessageKind14.Info);
    }

    private void ApplySTEngineShell14()
    {
        SnapshotNovoraST snapshot =
            RefreshSTEngineSnapshot14();

        bool waitingForStream =
            snapshot.Summary.StartsWith(
                "STEngine esperando",
                StringComparison.OrdinalIgnoreCase);

        STEngineStatusTitle14.Text =
            waitingForStream
                ? "STEngine independiente en espera"
                : snapshot.State switch
            {
                StateCoreST.Healthy =>
                    "STEngine NOVORA estable",

                StateCoreST.Watch =>
                    "STEngine observando NOVORA",

                StateCoreST.Degraded =>
                    "STEngine NOVORA degradado",

                StateCoreST.Critical =>
                    "STEngine NOVORA critico",

                _ =>
                    "STEngine sin estado"
            };

        STEngineStatusText14.Text =
            snapshot.Summary;

        STEngineStatusDot14.Fill =
            snapshot.State switch
            {
                StateCoreST.Healthy =>
                    FindResource("GreenBrush") as System.Windows.Media.Brush,

                StateCoreST.Watch =>
                    FindResource("BlueBrush") as System.Windows.Media.Brush,

                StateCoreST.Degraded =>
                    FindResource("OrangeBrush") as System.Windows.Media.Brush,

                StateCoreST.Critical =>
                    FindResource("DangerForegroundBrush") as System.Windows.Media.Brush,

                _ =>
                    FindResource("MutedBrush") as System.Windows.Media.Brush
            };

        STEngineStatusTitle14.Foreground =
            snapshot.State switch
            {
                StateCoreST.Degraded =>
                    FindResource("OrangeBrush") as System.Windows.Media.Brush,

                StateCoreST.Critical =>
                    FindResource("DangerForegroundBrush") as System.Windows.Media.Brush,

                _ =>
                    FindResource("TextBrush") as System.Windows.Media.Brush
            };

        if (snapshot.Observations.Count == 0)
        {
            STEngineObservationsText14.Text =
                snapshot.ShouldReduceNonCriticalWork
                    ? "STEngine recomienda reducir trabajo no critico."
                    : string.Empty;

            return;
        }

        STEngineObservationsText14.Text =
            string.Join(
                Environment.NewLine,
                snapshot.Observations);
    }

    private SnapshotNovoraST RefreshSTEngineSnapshot14()
    {
        try
        {
            return _stEngineST.CaptureNovoraST(
                _visionEngineVE?.RuntimeVE,
                _linkEngineRuntimeLE);
        }
        catch
        {
            return _stEngineST.LastNovoraSnapshotST;
        }
    }
}
