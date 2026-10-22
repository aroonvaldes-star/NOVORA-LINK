using NOVORA.Model;
using NOVORA.Service;
using NOVORA.ViewModel;
using NOVORA.ExInEngine;
using NOVORA.LinkEngine.Metrics;
using NOVORA.VisionEngine.Metrics;
using NOVORA.VisionEngine.Integration;
using NOVORA.NVIDIA;
using NOVORA.VisionEngine.Privacy;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ComboBox = System.Windows.Controls.ComboBox;

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

    private bool _closing;
    private bool _closeCleanupCompleted14;
    private bool _shellInitialized14;
    private bool _informationalPollingSuspended14;
    private string _selectedPage14 = "Home";
    private CancellationTokenSource? _refreshCts14;
    private bool _exInUiApplyingVE;
    private bool _exInPaddleMappingsApplyingVE;
    private ExInLiveSnapshot? _pendingExInLiveSnapshotVE;
    private int _exInLiveUpdateQueuedVE;

    private sealed record ExInPaddleTargetOption(ExInButtons Button, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly ExInPaddleTargetOption[] ExInPaddleTargetsVE =
    [
        new(ExInButtons.None, "Sin asignar"),
        new(ExInButtons.South, "A / Sur"),
        new(ExInButtons.East, "B / Este"),
        new(ExInButtons.West, "X / Oeste"),
        new(ExInButtons.North, "Y / Norte"),
        new(ExInButtons.LeftShoulder, "LB"),
        new(ExInButtons.RightShoulder, "RB"),
        new(ExInButtons.LeftStick, "Clic stick izquierdo"),
        new(ExInButtons.RightStick, "Clic stick derecho"),
        new(ExInButtons.DPadUp, "Cruceta arriba"),
        new(ExInButtons.DPadDown, "Cruceta abajo"),
        new(ExInButtons.DPadLeft, "Cruceta izquierda"),
        new(ExInButtons.DPadRight, "Cruceta derecha"),
        new(ExInButtons.Back, "Vista / Atrás"),
        new(ExInButtons.Start, "Menú / Inicio"),
        new(ExInButtons.Guide, "Guía"),
        new(ExInButtons.Touchpad, "Touchpad")
    ];

    public NLUIWindowMain()
    {
        _adb = new NLServiceADB(_paths);
        _deviceIdentity = new NLServiceDeviceIdentity(_adb, _settingsService);
        _metricsService = new NLServiceDeviceMetrics(_adb);

        InitializeComponent();
        InitializeExInPaddleMappingControlsVE();


        DataContext = _viewModel;

        NLServiceTheme.Apply(_viewModel.Theme);

        InitializeLinkEngineRuntimeLE();
        InitializeVisionEngineRuntimeVE();
        InitializeAndroidControl();
        InitializeAndroidInstallation();
    }

    private void InitializeExInPaddleMappingControlsVE()
    {
        foreach (ComboBox comboBox in ExInPaddleMappingControlsVE())
            comboBox.ItemsSource = ExInPaddleTargetsVE;
    }

    private async void Window_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            _ = CheckOfficialReleaseOnce14Async();

            LoadSettingsToViewModel14();
            // SelectionChanged fires while bindings are restored. Do not persist
            // partially loaded defaults over the user's saved configuration.
            _shellInitialized14 = true;
            ApplyTheme14(_viewModel.Theme);
            UpdateRuntimeButtons();
            ShowPage14(_selectedPage14);

            _viewModel.PropertyChanged +=
                ShellViewModel_PropertyChanged14;

            Closed += ShellWindow_Closed14;

            await RefreshDevicesAsync(
                force: true);
            await EnsureExInStandaloneAsync();

            await StartDiscoveryLifecycleNVAsync();
            await RestoreAndroidTrustAsync();
            await RefreshAndroidInstallationAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Window_Loaded error: {ex}");
            ShowTopMessage14(
                "No se pudo inicializar la UI. Revisa el diagnóstico del motor y el dispositivo.",
                NLUIMessageKind14.Error);
        }
    }

    private async void Window_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (_closeCleanupCompleted14)
        {
            return;
        }

        e.Cancel = true;

        if (_closing)
        {
            return;
        }

        _closing = true;
        StopAndroidLanNetworkWatch();
        _officialReleaseCts14.Cancel();
        CloseAndroidInstallation();
        _refreshCts14?.Cancel();

        _viewModel.PropertyChanged -=
            ShellViewModel_PropertyChanged14;

        try
        {
            await CleanupBeforeClose14Async();
        }
        finally
        {
            _closeCleanupCompleted14 = true;

            if (Dispatcher.CheckAccess())
            {
                Close();
            }
            else
            {
                _ = Dispatcher.InvokeAsync(
                    Close);
            }
        }
    }

    private async Task CleanupBeforeClose14Async()
    {
        await RunCloseStep14Async(
            StopDiscoveryLifecycleNVAsync);

        await RunCloseStep14Async(
            () => StopAndroidControlAsync(
                preserveTrustListening: true));

        await RunCloseStep14Async(
            () =>
            {
                _androidTrustStore?.Dispose();
                _androidUsbTrustStore?.Dispose();
                return Task.CompletedTask;
            });

        _androidTrustStore = null;
        _androidUsbTrustStore = null;
        _viewModel.PropertyChanged -= AndroidControlPropertyChanged;

        await RunCloseStep14Async(
            ShutdownLinkEngineRuntimeLEAsync);

        await RunCloseStep14Async(
            ShutdownVisionEngineRuntimeVEAsync);

        if (_exInControlSession is { } controlSession14)
        {
            await RunCloseStep14Async(
                async () =>
                    await controlSession14.DisposeAsync());

            _exInControlSession = null;
        }

        if (_exInEngine is { } exInEngine14)
        {
            exInEngine14.Manager.StatusChangedVE -= ShellGamepad_StatusChangedVE;
            exInEngine14.Manager.LiveSnapshotChangedVE -= ShellGamepad_LiveSnapshotChangedVE;
            exInEngine14.Manager.BatteryAlertVE -= ShellGamepad_BatteryAlertVE;

            await RunCloseStep14Async(
                async () =>
                {
                    await _exInInitialization;
                    await exInEngine14.DisposeAsync();
                });

            _exInEngine = null;
        }
    }

    private static async Task RunCloseStep14Async(
        Func<Task> closeStep14)
    {
        try
        {
            await closeStep14();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"NOVORA close cleanup error: {ex}");
        }
    }

    private void ShellWindow_Closed14(
        object? sender,
        EventArgs e)
    {
        Closed -= ShellWindow_Closed14;
        _officialReleaseCts14.Dispose();
        System.Windows.Application.Current.Shutdown();
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
    {
        ShowInTaskbar =
            false;

        Hide();
    }

    private void MaximizeRestore_Click(
        object sender,
        RoutedEventArgs e)
    {
        WindowState =
            WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
    }

    private void Window_StateChanged(
        object? sender,
        EventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        MaximizeRestoreButton14.Content =
            WindowState == WindowState.Maximized
                ? "❐"
                : "□";

        AutomationProperties.SetName(
            MaximizeRestoreButton14,
            WindowState == WindowState.Maximized
                ? "Restaurar NOVORA"
                : "Maximizar NOVORA");

        MaximizeRestoreButton14.ToolTip =
            WindowState == WindowState.Maximized
                ? "Restaurar"
                : "Maximizar";
    }

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
    }

    private async void MainActionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await ToggleVisionEngineVEAsync();
        }
        catch (Exception ex)
        {
            ShowTopMessage14(
                ex.Message,
                NLUIMessageKind14.Error);
        }
    }

    private void LinkEngineTestButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        bool running = _linkEngineRuntimeLE?.IsRunningLE == true ||
            _androidLanDataGateway?.IsConnected == true;
        bool usb = _androidControl?.IsAuthorized == true;
        bool lan = _androidLanControl?.IsAuthorized == true;
        var body = new StackPanel();

        void AddLine(string text, bool heading = false)
        {
            var line = new TextBlock
            {
                Text = text,
                FontSize = heading ? 16 : 14,
                FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, heading ? 0 : 8, 0, 0)
            };
            ApplyNovoraText(line);
            body.Children.Add(line);
        }

        AddLine(running ? "LinkEngine está en ejecución" : "Cómo activar Internet desde LINK CONTROL", heading: true);
        AddLine("Este botón muestra la guía y el estado. El inicio se solicita en el teléfono.");
        AddLine("1. Conecta LINK CONTROL con esta PC por USB o LAN. Para USB en esta compilación, autoriza la depuración en Android; para LAN, vincula ambos equipos en la misma red.");
        AddLine("2. En LINK CONTROL abre Principal, elige Internet y pulsa INICIAR. También puedes abrir Motores y pulsar Iniciar LinkEngine.");
        AddLine("3. Acepta el permiso VPN que muestra Android. Espera a que LinkEngine confirme la conexión antes de usar Internet por la PC.");
        AddLine($"Ruta autorizada en PC: {(usb && lan ? "USB y LAN" : usb ? "USB" : lan ? "LAN" : "ninguna")}.");
        AddLine("Estado de LinkEngine: " + LinkEngineFriendlyConnectionStatus.Text);
        AddLine("Enlace: " + LinkEngineFriendlyUsbStatus.Text);
        AddLine(LinkEngineResultStatus.Text);

        var dialog = CreateNovoraDialog("Internet por LinkEngine", 540,
            new ScrollViewer { Content = body, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var close = new System.Windows.Controls.Button { Content = "ENTENDIDO", Height = 38, Margin = new Thickness(0, 18, 0, 0) };
        ApplyNovoraActionButton(close);
        close.Click += (_, _) => dialog.Close();
        body.Children.Add(close);
        dialog.ShowDialog();
    }

    private async void OpenLinkMeasurementsButton_Click(object sender, RoutedEventArgs e)
    {
        string? serial = _viewModel.Device.Serial;
        if (string.IsNullOrWhiteSpace(serial))
        {
            LinkTunnelMeasurementsStatus.Text = "Abre Configuración → LinkEngine en el teléfono. La apertura desde PC requiere ADB conectado.";
            return;
        }

        OpenLinkMeasurementsButton.IsEnabled = false;
        try
        {
            string output = await _adb.ExecuteRawAsync(new[]
            {
                "-s", serial, "shell", "am", "start", "--user", "0", "-n",
                "com.novora.appcontrol/com.novora.appcontrol.MainActivity",
                "--es", "novora.page", "settings"
            });
            if (output.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Permission Denial", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(output.Trim());
            LinkTunnelMeasurementsStatus.Text = "Configuración de LE abierta en Android. Inicia la medición en el teléfono.";
        }
        catch (Exception)
        {
            LinkTunnelMeasurementsStatus.Text = "No se pudo abrir Android por ADB. Abre Configuración → LinkEngine directamente en el teléfono.";
        }
        finally
        {
            OpenLinkMeasurementsButton.IsEnabled = true;
        }
    }

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

        QueueVisionOutputSettingsSave14(applyAdvancedSettings: true);
    }

    private void ShellMonitor_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_shellInitialized14)
        {
            return;
        }

        QueueVisionOutputSettingsSave14(applyAdvancedSettings: false);
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

    private void OpenUserManualEs_Click(
        object sender,
        RoutedEventArgs e)
        => OpenUserManual14(
            Path.Combine(
                AppContext.BaseDirectory,
                "Manual",
                "NLManualUsuarioES.txt"));

    private void OpenUserManualEn_Click(
        object sender,
        RoutedEventArgs e)
        => OpenUserManual14(
            Path.Combine(
                AppContext.BaseDirectory,
                "Manual",
                "NLUserManualEN.txt"));

    private void OpenUserManual14(
        string path)
    {
        if (!File.Exists(path))
        {
            ShowTopMessage14(
                "No se encontró el manual instalado.",
                NLUIMessageKind14.Warning);

            return;
        }

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(path)
                {
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            ShowTopMessage14(
                ex.Message,
                NLUIMessageKind14.Error);
        }
    }

    private void VisionProfile_SelectionChanged14(object sender, SelectionChangedEventArgs e)
    {
        if (!_shellInitialized14) return;
        if (VisionProfileCombo14.SelectedValue is not string selected ||
            !Enum.TryParse<VisionEngine.Performance.VEProfile>(selected, out var profile)) return;
        var unified = VisionEngine.Performance.VEProfileOptions.CreateVE(profile);
        NLServiceVideoProfile.ApplyVE(_viewModel, profile);
        _visionEngineVE?.RuntimeVE.PerformanceVE.SetProfileVE(unified.PerformanceProfile);
        _visionEngineVE?.RuntimeVE.NvidiaVE.SetProfileVE(unified.ToLegacyNvidiaVE());
        RecalculateOutputProfile14();
        if (_shellInitialized14) QueueSaveSelection14();
        _androidControlRevision++;
        QueueAndroidControlSnapshot();
        if (_shellInitialized14)
            ShowTopMessage14("Perfil de VisionEngine preparado. Se aplica al iniciar o reconectar la transmisión.", NLUIMessageKind14.Info);
    }

    private async void InstallUpdateBanner_Click(
        object sender,
        RoutedEventArgs e)
    {
        await InstallOfficialRelease14Async();
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

            string? savedSerial = _settingsService.Load().SelectedDeviceSerial;
            NLModelDeviceInfo? selected =
                devices.FirstOrDefault(
                    device =>
                        string.Equals(
                            device.Serial,
                            _viewModel.Device.Serial,
                            StringComparison.OrdinalIgnoreCase));

            selected ??= devices.FirstOrDefault(device =>
                string.Equals(device.Serial, savedSerial, StringComparison.OrdinalIgnoreCase));

            // Sin dispositivo predeterminado: si no coincide el seleccionado ni el guardado, no se elige otro teléfono.
            _viewModel.Device =
                selected ??
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

    private void QueueVisionOutputSettingsSave14(bool applyAdvancedSettings)
    {
        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.DataBind,
            new Action(() =>
            {
                if (!_shellInitialized14 || _closing) return;
                RecalculateOutputProfile14();
                if (applyAdvancedSettings) ApplyAdvancedVisionSettingsVE();
                SaveSettingsFromViewModel14();
            }));
    }

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
        _viewModel.ExInEnabled = settings.ExInEnabled;
        _viewModel.VisionProfile =
            settings.VisionProfile;
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
        NLServiceNovoraSettings settings = _settingsService.Load();
        settings.Theme = _viewModel.Theme;
        settings.VideoPresentationMode = _viewModel.VideoPresentationMode;
        settings.Bitrate = _viewModel.Bitrate;
        settings.TargetFps = _viewModel.TargetFps;
        settings.MaxSize = _viewModel.MaxSize;
        settings.SelectedAudioOutput = _viewModel.SelectedAudioOutput;
        settings.AudioEnabled = _viewModel.AudioEnabled;
        settings.SelectedMonitorLabel = _viewModel.SelectedMonitor?.DisplayLabel;
        settings.SelectedMonitorDeviceName = _viewModel.SelectedMonitor?.DeviceName;
        if (!string.IsNullOrWhiteSpace(_viewModel.Device.Serial))
            settings.SelectedDeviceSerial = _viewModel.Device.Serial;
        settings.PrivacyShieldEnabled = _viewModel.PrivacyShieldEnabled;
        settings.IntegrationClipboardEnabled = _viewModel.IntegrationClipboardEnabled;
        settings.IntegrationFileTransferEnabled = _viewModel.IntegrationFileTransferEnabled;
        settings.IntegrationDragDropEnabled = _viewModel.IntegrationDragDropEnabled;
        settings.IntegrationApplicationsEnabled = _viewModel.IntegrationApplicationsEnabled;
        settings.IntegrationNotificationsEnabled = _viewModel.IntegrationNotificationsEnabled;
        settings.IntegrationDynamicResizeEnabled = _viewModel.IntegrationDynamicResizeEnabled;
        settings.ExInEnabled = _viewModel.ExInEnabled;
        settings.VisionProfile = _viewModel.VisionProfile;
        _settingsService.Save(settings);
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
        ExInStatus status)
    {
        GamepadStatusText14.Text =
            string.IsNullOrWhiteSpace(status.LastError)
                ? status.Message
                : $"{status.Message} - {status.LastError}";

        GamepadCountText14.Text =
            $"Mandos conectados: {status.ConnectedGamepads} - Reportes enviados: {status.ReportsSent:N0}";

        ExInLiveSnapshot? live = _exInEngine?.LiveSnapshot;
        ExInDevice? device = live?.Device;
        ExInState raw = live?.State ?? default;
        ExInState corrected = live?.CorrectedState ?? default;
        string? profileKey = device?.Identity?.ProfileKey;
        ExInDiagnosticStatus? diagnostic = profileKey is null
            ? null : status.Diagnostics.FirstOrDefault(value => value.ProfileKey == profileKey);
        ExInBatteryStatus? battery = profileKey is null
            ? null : status.Batteries.FirstOrDefault(value => value.ProfileKey == profileKey);
        ExInDynamicCalibrationView14.UpdateBatteryVE(battery);
        ExInInputMode mode = _exInEngine?.Manager.ModeVE ?? ExInInputMode.Game;
        bool available = device is not null && !_exInUiApplyingVE && !_androidControlApplying;
        bool calibrating = live?.Calibrating == true;
        bool canCalibrate = available && _exInEngine?.Manager.IsPrivacyProtectedVE != true;

        ExInIdentityText14.Text = device is null
            ? "Sin control físico"
            : $"{device.Identity?.Family ?? ExInControllerFamily.Generic} · {device.Name}\n" +
              $"{live?.Calibration?.ConnectionType ?? "Conexión no reportada"} · " +
              (device.Identity?.Family == ExInControllerFamily.DualShock4
                  ? "Juego · puntero · navegación · touchpad"
                  : "Juego · puntero · navegación");
        ApplyExInCalibrationProgressVE(live);
        static int AxisVE(short value) => (int)Math.Round(value / 32767d * 100);
        static int TriggerVE(short value) => (int)Math.Round(Math.Max(0, (int)value) / 32767d * 100);
        ExInAxesText14.Text =
            $"ENTRADA   L {AxisVE(raw.LeftX),4}/{AxisVE(raw.LeftY),4}  R {AxisVE(raw.RightX),4}/{AxisVE(raw.RightY),4}  T {TriggerVE(raw.LeftTrigger),3}/{TriggerVE(raw.RightTrigger),3}\n" +
            $"APLICADO  L {AxisVE(corrected.LeftX),4}/{AxisVE(corrected.LeftY),4}  R {AxisVE(corrected.RightX),4}/{AxisVE(corrected.RightY),4}  T {TriggerVE(corrected.LeftTrigger),3}/{TriggerVE(corrected.RightTrigger),3}";
        ExInHealthText14.Text = diagnostic is null
            ? "Diagnóstico no ejecutado"
            : $"{diagnostic.Classification} · {diagnostic.Explanation}";
        ExInBatteryText14.Text = battery is null
            ? "Batería no reportada"
            : battery.Percent is int percent ? $"{battery.State} · {percent}%" : battery.State.ToString();

        // The active mode stays enabled: a disabled button renders at 45% opacity, which made
        // the selected mode look faded instead of highlighted. Clicking it again is a no-op.
        ExInGameModeButton14.IsEnabled = available && !calibrating;
        ExInUiModeButton14.IsEnabled = available && !calibrating;
        int pointerSensitivity = (int)Math.Round((_exInEngine?.Manager.PointerSensitivityVE ?? .5d) * 100d);
        if (Math.Abs(ExInPointerSensitivitySlider14.Value - pointerSensitivity) >= .5)
            ExInPointerSensitivitySlider14.Value = pointerSensitivity;
        ExInPointerSensitivityText14.Text = $"{pointerSensitivity}% · UI";
        ExInPointerSensitivitySlider14.IsEnabled = available && !calibrating;
        ExInWindowsCalibrationButton14.IsEnabled = available && !calibrating;
        ExInCalibrationStartButton14.IsEnabled = canCalibrate && !calibrating;
        ExInCalibrationFinishButton14.IsEnabled = canCalibrate && calibrating && live?.CalibrationProgress?.Complete == true;
        ExInCalibrationResetButton14.IsEnabled = canCalibrate && (live?.Calibrated == true || calibrating);
        ExInCalibrationResetButton14.Content = calibrating ? "CANCELAR" : "RESTABLECER";
        ExInGameModeButton14.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty,
            mode == ExInInputMode.Game ? "InputSelectedBackgroundBrush" : "PanelBrush2");
        ExInUiModeButton14.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty,
            mode == ExInInputMode.Ui ? "InputSelectedBackgroundBrush" : "PanelBrush2");
        ExInGameModeButton14.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty,
            mode == ExInInputMode.Game ? "BlueBrush" : "BorderBrush");
        ExInUiModeButton14.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty,
            mode == ExInInputMode.Ui ? "BlueBrush" : "BorderBrush");
        ApplyExInPaddleMappingsUiVE(available && !calibrating);
        ApplyExInSelectionUiVE(!_exInUiApplyingVE && !_androidControlApplying, calibrating);
    }

    private IEnumerable<ComboBox> ExInPaddleMappingControlsVE()
    {
        yield return ExInP1MappingComboBox14;
        yield return ExInP2MappingComboBox14;
        yield return ExInP3MappingComboBox14;
        yield return ExInP4MappingComboBox14;
    }

    private void ApplyExInPaddleMappingsUiVE(bool enabled)
    {
        ExInPaddleMappings mappings = _exInEngine?.Manager.PaddleMappingsVE ?? ExInPaddleMappings.DefaultVE;
        _exInPaddleMappingsApplyingVE = true;
        try
        {
            foreach (ComboBox comboBox in ExInPaddleMappingControlsVE())
            {
                comboBox.IsEnabled = enabled;
                if (comboBox.Tag is not string tag || !Enum.TryParse(tag, out ExInPaddle paddle)) continue;
                comboBox.SelectedItem = ExInPaddleTargetsVE.FirstOrDefault(option => option.Button == mappings.GetVE(paddle))
                    ?? ExInPaddleTargetsVE[0];
            }
        }
        finally
        {
            _exInPaddleMappingsApplyingVE = false;
        }

        ExInPaddleSupportText14.Text = enabled
            ? "P1–P4 se repiten como el botón seleccionado. Puedes elegir el mismo botón para varios paddles."
            : "Conecta un mando compatible y termina la calibración para configurar P1–P4.";
    }

    private void ExInPaddleMapping_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_exInPaddleMappingsApplyingVE || sender is not ComboBox comboBox ||
            comboBox.Tag is not string tag || !Enum.TryParse(tag, out ExInPaddle paddle) ||
            comboBox.SelectedItem is not ExInPaddleTargetOption option)
            return;

        if (_exInEngine?.Manager.TrySetPaddleMappingVE(paddle, option.Button) == true)
        {
            _androidControlRevision++;
            ShowTopMessage14($"{paddle} ahora repite {option.Label}.", NLUIMessageKind14.Success);
            if (_exInEngine is not null) ApplyGamepadShell14(_exInEngine.Status);
            QueueAndroidControlSnapshot();
            return;
        }

        ShowTopMessage14("No se pudo guardar la asignación del paddle.", NLUIMessageKind14.Error);
        if (_exInEngine is not null) ApplyGamepadShell14(_exInEngine.Status);
    }

    private void ApplyExInCalibrationProgressVE(ExInLiveSnapshot? live)
    {
        ExInCalibrationProgress progress = live?.CalibrationProgress ??
            (live?.Calibrated == true ? new(100, 100, 100, 100) : new(0, 0, 0, 0));
        ExInDynamicCalibrationView14.UpdateVE(live);
        ExInCalibrationOverallProgress14.Value = progress.Overall;
        ExInCalibrationOverallText14.Text = $"{progress.Overall}%";
        ExInCalibrationLeftProgress14.Value = progress.LeftStick;
        ExInCalibrationRightProgress14.Value = progress.RightStick;
        ExInCalibrationLeftTriggerProgress14.Value = progress.LeftTrigger;
        ExInCalibrationRightTriggerProgress14.Value = progress.RightTrigger;
        ExInCalibrationLeftText14.Text = $"{progress.LeftStick}%";
        ExInCalibrationRightText14.Text = $"{progress.RightStick}%";
        ExInCalibrationLeftTriggerText14.Text = $"{progress.LeftTrigger}%";
        ExInCalibrationRightTriggerText14.Text = $"{progress.RightTrigger}%";
        ExInCalibrationGuideText14.Text = live?.Calibrating == true
            ? progress.Complete
                ? "Recorrido completo. Las aplicaciones siguen bloqueadas: suelta el mando y pulsa GUARDAR."
                : NextExInCalibrationStepVE(progress) + " Las aplicaciones están bloqueadas."
            : live?.Calibrated == true
                ? "Perfil activo para este mando. Puedes repetirlo o restablecerlo."
                : "Deja sticks y gatillos en reposo y pulsa INICIAR.";
        if (live?.Calibrating == true)
        {
            ExInCalibrationFinishButton14.IsEnabled = CanApplyExInCalibrationVE() && progress.Complete;
            ExInCalibrationResetButton14.IsEnabled = CanApplyExInCalibrationVE();
        }
    }

    private static string NextExInCalibrationStepVE(ExInCalibrationProgress progress)
        => progress.LeftStick < 90 ? "Paso 1 de 4: gira el stick izquierdo por todo el borde." :
           progress.RightStick < 90 ? "Paso 2 de 4: gira el stick derecho por todo el borde." :
           progress.LeftTrigger < 90 ? "Paso 3 de 4: presiona y suelta el gatillo izquierdo." :
           "Paso 4 de 4: presiona y suelta el gatillo derecho.";

    private async void ExInGameMode_Click(object sender, RoutedEventArgs e)
        => await SetExInModeFromUiVEAsync(ExInInputMode.Game);

    private async void ExInUiMode_Click(object sender, RoutedEventArgs e)
        => await SetExInModeFromUiVEAsync(ExInInputMode.Ui);

    private void ExInPointerSensitivity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_exInEngine is null) return;
        double value = e.NewValue / 100d;
        _exInEngine.Manager.SetPointerSensitivityVE(value);
        NLServiceSettings settings = new();
        NLServiceNovoraSettings snapshot = settings.Load();
        snapshot.ExInPointerSensitivity = (int)Math.Round(e.NewValue);
        settings.Save(snapshot);
        ExInPointerSensitivityText14.Text = $"{e.NewValue:0}% · UI";
    }

    private void ExInWindowsCalibration_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("control.exe", "joy.cpl") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowTopMessage14($"No fue posible abrir la calibración de Windows: {ex.Message}", NLUIMessageKind14.Error);
        }
    }

    private async Task SetExInModeFromUiVEAsync(ExInInputMode mode)
    {
        if (_exInEngine is null || _exInUiApplyingVE || _androidControlApplying) return;
        if (_exInEngine.Manager.ModeVE == mode) return;
        _exInUiApplyingVE = true;
        ApplyGamepadShell14(_exInEngine.Status);
        try
        {
            ExInModeResult result = await _exInEngine.Manager.SetModeAsync(mode);
            if (result.Success) _androidControlRevision++;
            else _viewModel.ConnectionStatus = result.Message;
        }
        catch (Exception ex) { _viewModel.ConnectionStatus = "ExInEngine: " + ex.Message; }
        finally
        {
            _exInUiApplyingVE = false;
            ApplyGamepadShell14(_exInEngine.Status);
            QueueAndroidControlSnapshot();
        }
    }

    private void ExInCalibrationStart_Click(object sender, RoutedEventArgs e)
        => ApplyExInCalibrationUiVE(CanApplyExInCalibrationVE() && _exInEngine?.Manager.BeginCalibrationVE() == true, "No se pudo iniciar la calibración.", incrementRevision: true);

    private void ExInCalibrationFinish_Click(object sender, RoutedEventArgs e)
        => ApplyExInCalibrationUiVE(CanApplyExInCalibrationVE() && _exInEngine?.Manager.FinishCalibrationVE() == true, "El recorrido capturado fue insuficiente.", incrementRevision: true);

    private void ExInCalibrationReset_Click(object sender, RoutedEventArgs e)
    {
        if (!CanApplyExInCalibrationVE()) return;
        if (MessageBox.Show(this,
                "Se eliminará la calibración actual del control. ¿Quieres restablecerla?",
                "Restablecer calibración",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _exInEngine?.Manager.ResetCalibrationVE();
        _androidControlRevision++;
        if (_exInEngine is not null) ApplyGamepadShell14(_exInEngine.Status);
        QueueAndroidControlSnapshot();
        ShowTopMessage14("Calibración restablecida.", NLUIMessageKind14.Success);
    }

    private bool CanApplyExInCalibrationVE()
        => !_exInUiApplyingVE && !_androidControlApplying &&
           _exInEngine?.Manager.IsPrivacyProtectedVE == false;

    private void ApplyExInCalibrationUiVE(bool success, string failureMessage, bool incrementRevision)
    {
        if (success && incrementRevision) _androidControlRevision++;
        if (!success) _viewModel.ConnectionStatus = failureMessage;
        if (_exInEngine is not null) ApplyGamepadShell14(_exInEngine.Status);
        QueueAndroidControlSnapshot();
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
        => Dispatcher.Invoke(() =>
        {
            ApplyPrivacyShell14(status);
            if (_exInEngine is not null) ApplyGamepadShell14(_exInEngine.Status);
            _androidControlRevision++;
            QueueAndroidControlSnapshot();
        });

    private void ShellGamepad_StatusChangedVE(
        object? sender,
        ExInStatus status)
        => Dispatcher.Invoke(() =>
        {
            ApplyGamepadShell14(status);
            AndroidEngineStateChanged();
            QueueAndroidControlSnapshot();
        });

    private void ShellGamepad_LiveSnapshotChangedVE(object? sender, ExInLiveSnapshot snapshot)
    {
        if (_closing) return;
        Interlocked.Exchange(ref _pendingExInLiveSnapshotVE, snapshot);
        QueueExInLiveFrameVE();
    }

    private void QueueExInLiveFrameVE()
    {
        if (Interlocked.CompareExchange(ref _exInLiveUpdateQueuedVE, 1, 0) != 0) return;
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            try
            {
                ExInLiveSnapshot? latest = Interlocked.Exchange(ref _pendingExInLiveSnapshotVE, null);
                if (!_closing && latest is not null) ApplyExInCalibrationProgressVE(latest);
            }
            finally
            {
                Interlocked.Exchange(ref _exInLiveUpdateQueuedVE, 0);
                if (!_closing && Volatile.Read(ref _pendingExInLiveSnapshotVE) is not null)
                    QueueExInLiveFrameVE();
            }
        }));
    }

    private void ShellGamepad_BatteryAlertVE(
        object? sender,
        ExInBatteryAlert alert)
        => Dispatcher.Invoke(() =>
        {
            _androidExInBatteryAlert = alert;
            _androidExInBatteryAlertSequence++;
            _androidControlRevision++;
            QueueAndroidControlSnapshot();
        });

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

    private async void WifiAdb_Click(
        object sender,
        RoutedEventArgs e)
    {
        await PrepareVisionLanFallbackVEAsync();
    }

}
