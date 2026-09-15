using NOVORA.Model;
using NOVORA.Service;
using NOVORA.ViewModel;
using NOVORA.STEngine.Core;
using NOVORA.VisionEngine.Gamepad;
using NOVORA.VisionEngine.Integration;
using NOVORA.NVIDIA;
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

public enum NLUIMessageKind14
{
    Info,
    Success,
    Warning,
    Error
}

public partial class NLUIWindowMain : Window
{
    private readonly NLServiceNovoraPaths _paths = new();
    private readonly NLServiceSettings _settingsService = new();
    private readonly NLServiceMonitor _monitorService = new();
    private readonly NLServiceOutputProfile _outputProfileService = new();
    private readonly NLServiceUpdate _updateService = new();
    private readonly NLViewModelMain _viewModel = new();
    private readonly NLServiceADB _adb;
    private readonly NLServiceDeviceIdentity _deviceIdentity;
    private readonly NLServiceDeviceMetrics _metricsService;
    private readonly STCoreEngine _stEngineST = new();

    private bool _closing;
    private bool _shellInitialized14;
    private bool _informationalPollingSuspended14;
    private string _selectedPage14 = "Home";
    private CancellationTokenSource? _refreshCts14;

    public NLUIWindowMain()
    {
        _adb = new NLServiceADB(_paths);
        _deviceIdentity = new NLServiceDeviceIdentity(_adb, _settingsService);
        _metricsService = new NLServiceDeviceMetrics(_adb);

        InitializeComponent();


        DataContext = _viewModel;

        NLServiceTheme.Apply(_viewModel.Theme);

        InitializeLinkEngineRuntimeLE();
        InitializeVisionEngineRuntimeVE();
        InitializeAndroidControl();
        InitializeAndroidInstallation();
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
        await RestoreAndroidTrustAsync();
        await RefreshAndroidInstallationAsync();
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
        CloseAndroidInstallation();
        _refreshCts14?.Cancel();

        _viewModel.PropertyChanged -=
            ShellViewModel_PropertyChanged14;

        try
        {
            await StopAndroidControlAsync(preserveTrustListening: true);
            _androidTrustStore?.Dispose();
            _androidTrustStore = null;
            _viewModel.PropertyChanged -= AndroidControlPropertyChanged;
            await StopDiscoveryLifecycleNVAsync();
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
            NLUIMessageKind14.Success);
    }

    private void VideoProfile_SelectionChanged14(object sender, SelectionChangedEventArgs e)
    {
        if (!_shellInitialized14 || VideoProfileCombo14.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string selected ||
            !Enum.TryParse<VisionEngine.Performance.VEPerformanceProfile>(selected, out var profile)) return;
        NLServiceVideoProfile.ApplyVE(_viewModel, profile);
        _visionEngineVE?.RuntimeVE.PerformanceVE.SetProfileVE(profile);
        RecalculateOutputProfile14();
        QueueSaveSelection14();
        _androidControlRevision++;
        QueueAndroidControlSnapshot();
        VideoProfileCombo14.SelectedIndex = -1;
        ShowTopMessage14("Perfil de video preparado. Se aplica al iniciar o reconectar la transmisión.", NLUIMessageKind14.Info);
    }

    private void InstallUpdateBanner_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowTopMessage14(
            "Instalador de actualizacion no disponible en esta compilacion.",
            NLUIMessageKind14.Info);
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
                nameof(NLViewModelMain.Theme),
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

            IReadOnlyList<NLModelDeviceInfo> devices =
                await _adb.GetDevicesAsync(
                    cancellationToken,
                    forceRefresh: force);

            _viewModel.Devices =
                devices;

            NLModelDeviceInfo? selected =
                devices.FirstOrDefault(
                    device =>
                        string.Equals(
                            device.Serial,
                            _viewModel.Device.Serial,
                            StringComparison.OrdinalIgnoreCase));

            _viewModel.Device =
                selected ??
                devices.FirstOrDefault() ??
                new NLModelDeviceInfo();

            _viewModel.RefreshOutputCapabilityOptions();
            RecalculateOutputProfile14();
            UpdateRuntimeButtons();

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

        NLModelDeviceInfo device =
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

            NLServiceDeviceMetricsSnapshot metrics =
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
        NLServiceNovoraSettings settings =
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
        _viewModel.NvidiaProfile =
            settings.NvidiaProfile;
        ApplyAdvancedVisionSettingsVE();

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
            new NLServiceNovoraSettings
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
                NvidiaProfile = _viewModel.NvidiaProfile
            });
    }

    private void RecalculateOutputProfile14()
    {
        if (!_viewModel.Device.Connected ||
            string.IsNullOrWhiteSpace(
                _viewModel.Device.Serial))
        {
            _viewModel.NLModelOutputProfile =
                null;

            return;
        }

        _viewModel.NLModelOutputProfile =
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
        => NLServiceTheme.Apply(theme);

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

        UpdateLinkEngineButtonLE();

        WifiAdbButton.IsEnabled =
            hasDevice && !_closing;
    }

    private void ShowTopMessage14(
        string message,
        NLUIMessageKind14 kind = NLUIMessageKind14.Info)
    {
        TopMessageText14.Text =
            message;

        TopMessageDot14.Fill =
            kind switch
            {
                NLUIMessageKind14.Success =>
                    FindResource("GreenBrush") as System.Windows.Media.Brush,

                NLUIMessageKind14.Warning =>
                    FindResource("OrangeBrush") as System.Windows.Media.Brush,

                NLUIMessageKind14.Error =>
                    FindResource("DangerForegroundBrush") as System.Windows.Media.Brush,

                _ =>
                    FindResource("BlueBrush") as System.Windows.Media.Brush
            };
    }

    private void ApplyPrivacyShell14(
        VEPrivacyStatus status)
    {
        PrivacyStatusText14.Text =
            $"{status.State} - {status.Classification}\n{status.Message}";
    }

    private void ApplyGamepadShell14(
        VEGamepadStatus status)
    {
        GamepadStatusText14.Text =
            string.IsNullOrWhiteSpace(status.LastError)
                ? status.Message
                : $"{status.Message} - {status.LastError}";

        GamepadCountText14.Text =
            $"Mandos conectados: {status.ConnectedGamepads} - Reportes enviados: {status.ReportsSent:N0}";
    }

    private void ApplyIntegrationShell14(
        VEIntegrationStatus status)
    {
        VEIntegrationCapabilities c =
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
        NLNVIDIAStatus status)
    {
        NLNVIDIACapabilities c =
            status.Capabilities;

        NvidiaStatusTitle14.Text =
            status.Message;

        NvidiaStatusDot14.Fill =
            status.Pipeline.UseNvdec
                ? FindResource("GreenBrush") as System.Windows.Media.Brush
                : FindResource("MutedBrush") as System.Windows.Media.Brush;

        NvidiaCapabilitiesText14.Text =
            $"CUDA inicializada: {(c.CudaInitialized ? "Si" : "No")} - " +
            $"API NVDEC detectada: {(c.NvdecApiAvailable ? "Si" : "No")} - " +
            $"API NVENC detectada: {(c.NvencApiAvailable ? "Si" : "No")}";

        NvidiaBackendText14.Text =
            $"Backend: {c.Backend}";

        NvidiaPipelineText14.Text =
            (status.Pipeline.UseNvdec ? "NVDEC activo · salida en CPU. " : "NVDEC no activo. ") +
            "Zero-copy, NVENC, RTX Video y FRUC no implementados.";
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
        VEPrivacyStatus status)
        => Dispatcher.Invoke(
            () =>
                ApplyPrivacyShell14(status));

    private void ShellGamepad_StatusChangedVE(
        object? sender,
        VEGamepadStatus status)
        => Dispatcher.Invoke(
            () =>
                ApplyGamepadShell14(status));

    private void ShellIntegration_StatusChangedVE(
        object? sender,
        VEIntegrationStatus status)
        => Dispatcher.Invoke(
            () =>
                ApplyIntegrationShell14(status));

    private void ShellNvidia_StatusChangedVE(
        object? sender,
        NLNVIDIAStatus status)
        => Dispatcher.Invoke(
            () =>
                ApplyNvidiaShell14(status));

    private void NvidiaProfile_SelectionChanged14(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (NvidiaProfileCombo14.SelectedItem is ComboBoxItem item && item.Tag is string selected)
            _viewModel.NvidiaProfile = selected;

        ApplyAdvancedVisionSettingsVE();
        if (!_shellInitialized14)
        {
            return;
        }

        QueueSaveSelection14();
    }

    private void WifiAdb_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowTopMessage14(
            "ADB por Wi-Fi se configura desde LinkEngine/Remote Android.",
            NLUIMessageKind14.Info);
    }

    private void ApplySTEngineShell14()
    {
        STCoreSnapshotNovora snapshot =
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
                STCoreState.Healthy =>
                    "STEngine NOVORA estable",

                STCoreState.Watch =>
                    "STEngine observando NOVORA",

                STCoreState.Degraded =>
                    "STEngine NOVORA degradado",

                STCoreState.Critical =>
                    "STEngine NOVORA critico",

                _ =>
                    "STEngine sin estado"
            };

        STEngineStatusText14.Text =
            snapshot.Summary;

        STEngineStatusDot14.Fill =
            snapshot.State switch
            {
                STCoreState.Healthy =>
                    FindResource("GreenBrush") as System.Windows.Media.Brush,

                STCoreState.Watch =>
                    FindResource("BlueBrush") as System.Windows.Media.Brush,

                STCoreState.Degraded =>
                    FindResource("OrangeBrush") as System.Windows.Media.Brush,

                STCoreState.Critical =>
                    FindResource("DangerForegroundBrush") as System.Windows.Media.Brush,

                _ =>
                    FindResource("MutedBrush") as System.Windows.Media.Brush
            };

        STEngineStatusTitle14.Foreground =
            snapshot.State switch
            {
                STCoreState.Degraded =>
                    FindResource("OrangeBrush") as System.Windows.Media.Brush,

                STCoreState.Critical =>
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

    private STCoreSnapshotNovora RefreshSTEngineSnapshot14()
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
