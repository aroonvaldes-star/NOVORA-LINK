using NOVORA.Models;
using NOVORA.Services;
using NOVORA.ViewModels;
using NOVORA.Widgets;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using FontFamily = System.Windows.Media.FontFamily;
using ListBox = System.Windows.Controls.ListBox;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace NOVORA;

public partial class MainWindow : Window
{
    private readonly NovoraPaths _paths = new();
    private readonly AdbService _adb;
    private readonly DeviceIdentityService _deviceIdentity;
    private readonly MonitorService _monitorService = new();
    private readonly WidgetRegistry _widgetRegistry;
    private readonly MainViewModel _viewModel;
    private readonly WidgetManager _widgetManager;

    private readonly GnirehtetService _gnirehtet;
    private readonly SettingsService _settingsService = new();
    private readonly OutputProfileService _outputProfileService = new();
    private readonly UpdateService _updateService = new();
    private readonly NetworkService _networkService;
    private readonly DeviceMetricsService _metricsService;
    private readonly DeviceStateService _deviceState;
    private readonly DeviceFileService _fileService;
    private readonly ApplicationService _applicationService;
    private readonly ScrcpyService _scrcpyService;
    private readonly DispatcherTimer _widgetTimer;

    private IReadOnlyList<MonitorInfo> _monitors =
        Array.Empty<MonitorInfo>();

    private Process? _scrcpyProcess;

    private bool _stopping;
    private bool _allowClose;
    private bool _refreshingDevices;

    // Variables para widgets dinámicos.
    private TextBlock? _redWidgetStatus;
    private TextBlock? _performanceWidgetStatus;
    private TextBlock? _videoWidgetStatus;
    private TextBlock? _statisticsWidgetStatus;

    public MainWindow()
    {
        InitializeComponent();

        // --------------------------------------------------------
        // Servicios
        // --------------------------------------------------------

        _adb =
            new AdbService(_paths);

        _deviceIdentity =
            new DeviceIdentityService(_adb);

        _gnirehtet =
            new GnirehtetService(_paths);

        _networkService =
            new NetworkService(_adb);

        _metricsService =
            new DeviceMetricsService(_adb);

        _deviceState =
            new DeviceStateService(
                _networkService,
                _metricsService);

        _fileService =
            new DeviceFileService(_adb);

        _applicationService =
            new ApplicationService(_adb);

        _scrcpyService =
            new ScrcpyService(_paths);

        // --------------------------------------------------------
        // Registry / ViewModel / Manager
        // --------------------------------------------------------

        _widgetRegistry =
            new WidgetRegistry();

        _viewModel =
            new MainViewModel(
                _widgetRegistry);

        DataContext =
            _viewModel;

        // --------------------------------------------------------
        // Widget manager
        // --------------------------------------------------------
        //
        // Si el XAML contiene un Canvas llamado WidgetCanvas,
        // se utiliza para los widgets persistentes.
        //
        // Si tu XAML usa otro nombre, cambia únicamente
        // "WidgetCanvas" por el nombre real del Canvas.
        //
        // --------------------------------------------------------

        _widgetManager =
            new WidgetManager(
                WidgetCanvas,
                _widgetRegistry,
                _settingsService);

        // --------------------------------------------------------
        // Timer de widgets
        // --------------------------------------------------------

        _widgetTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromSeconds(3)
            };

        _widgetTimer.Tick += async (_, _) =>
            await RefreshDynamicWidgetsAsync();

        // --------------------------------------------------------
        // Cambios de widgets
        // --------------------------------------------------------

        _viewModel.PropertyChanged +=
            (_, e) =>
            {
                if (e.PropertyName?.StartsWith(
                        "Show",
                        StringComparison.Ordinal) == true)
                {
                    RebuildWidgets();
                }
            };
    }

    // ============================================================
    // LOG
    // ============================================================

    private void Log(
        string message)
    {
        if (_consoleOutput is null)
            return;

        _consoleOutput.AppendText(
            $"[{DateTime.Now:HH:mm:ss}] " +
            $"{message}" +
            Environment.NewLine);

        _consoleOutput.ScrollToEnd();
    }

    // ============================================================
    // WINDOW LOADED
    // ============================================================

    private async void Window_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            _paths.ValidateRequiredTools();

            LoadSettings();

            ThemeService.Apply(
                _viewModel.Theme);

            LoadMonitors();

            RebuildWidgets();

            Log(
                "[INFO] NOVORA 1.2 iniciado.");

            await RefreshDeviceAsync();

            await AutoConnectWifiAsync();

            _widgetTimer.Start();
        }
        catch (Exception ex)
        {
            _viewModel.ConnectionStatus =
                "Error de inicialización";

            Log(
                $"[ERROR] Inicialización: {ex.Message}");

            MessageBox.Show(
                ex.Message,
                "NOVORA",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // ============================================================
    // SETTINGS
    // ============================================================

    private void LoadSettings()
    {
        var settings =
            _settingsService.Load();

        _viewModel.AudioEnabled =
            settings.AudioEnabled;

        _viewModel.Theme =
            settings.Theme;

        _viewModel.Bitrate =
            settings.Bitrate;

        _viewModel.TargetFps =
            settings.TargetFps;

        _viewModel.MaxSize =
            settings.MaxSize;

        _viewModel.ShowConsoleWidget =
            settings.ShowConsoleWidget;

        _viewModel.ShowVideoWidget =
            settings.ShowVideoWidget;

        _viewModel.ShowStatisticsWidget =
            settings.ShowStatisticsWidget;

        _viewModel.ShowRefWidget =
            settings.ShowRefWidget;

        _viewModel.ShowRedWidget =
            settings.ShowRedWidget;

        _viewModel.ShowPerformanceWidget =
            settings.ShowPerformanceWidget;

        _viewModel.ShowCaptureWidget =
            settings.ShowCaptureWidget;

        _viewModel.ShowFilesWidget =
            settings.ShowFilesWidget;

        _viewModel.ShowApplicationsWidget =
            settings.ShowApplicationsWidget;
    }

    // ============================================================
    // MONITORES
    // ============================================================

    private void LoadMonitors()
    {
        _monitors =
            _monitorService.GetMonitors();

        _viewModel.Monitors =
            _monitors;

        var savedMonitor =
            _settingsService.Load()
                .SelectedMonitorLabel;

        _viewModel.SelectedMonitor =
            _monitors.FirstOrDefault(
                m =>
                    string.Equals(
                        m.DisplayLabel,
                        savedMonitor,
                        StringComparison.OrdinalIgnoreCase))
            ??
            _monitorService.GetBestMonitor(
                _monitors);

        RefreshOutputProfile();
    }

    // ============================================================
    // DISPOSITIVOS
    // ============================================================

    private async Task RefreshDeviceAsync(
        bool force = false)
    {
        var devices =
            await _deviceIdentity.GetDevicesAsync(
                force);

        _viewModel.Devices =
            devices;

        var settings =
            _settingsService.Load();

        var selected =
            devices.FirstOrDefault(
                d =>
                    !string.IsNullOrWhiteSpace(
                        settings.SelectedDeviceSerial)
                    &&
                    string.Equals(
                        d.Serial,
                        settings.SelectedDeviceSerial,
                        StringComparison.OrdinalIgnoreCase));

        _viewModel.Device =
            selected
            ??
            (
                devices.Count == 1
                    ? devices[0]
                    : new DeviceInfo()
            );

        if (_viewModel.Device.Connected)
        {
            _viewModel.ConnectionStatus =
                $"● CONECTADO • " +
                $"{_deviceIdentity.GetDisplayName(
                    _viewModel.Device)}";

            var mode =
                _viewModel.Device.BestDisplayMode;

            _viewModel.PerformanceSummary =
                mode is null
                    ? "Pantalla detectada."
                    : $"Hasta {mode.Width}x{mode.Height} " +
                      $"@ {mode.RefreshRateHz:0.#} Hz";

            RefreshOutputProfile();

            _deviceState.Invalidate(
                _viewModel.Device.Serial);
        }
        else
        {
            _viewModel.ConnectionStatus =
                devices.Count == 0
                    ? "Sin dispositivo"
                    : "Selecciona un dispositivo";

            _viewModel.PerformanceSummary =
                devices.Count == 0
                    ? "Conecta y autoriza el teléfono."
                    : $"{devices.Count} dispositivos detectados.";

            _viewModel.OutputProfile =
                null;
        }

        await RefreshDynamicWidgetsAsync();
    }

    // ============================================================
    // ADB WI-FI AUTOMÁTICO
    // ============================================================

    private async Task AutoConnectWifiAsync()
    {
        var device =
            _viewModel.Device;

        if (device is null ||
            !device.Connected ||
            string.IsNullOrWhiteSpace(
                device.Serial))
        {
            return;
        }

        if (device.Serial.Contains(
                ':',
                StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var wifiSerial =
                await _adb.ConnectOverWifiAsync(
                    device.Serial);

            var settings =
                _settingsService.Load();

            settings.SelectedDeviceSerial =
                wifiSerial;

            _settingsService.Save(
                settings);

            await RefreshDeviceAsync(
                true);

            Log(
                $"[OK] ADB Wi-Fi automático: " +
                $"{wifiSerial}");
        }
        catch (Exception ex)
        {
            Log(
                $"[INFO] ADB Wi-Fi automático no disponible: " +
                $"{ex.Message}");
        }
    }

    // ============================================================
    // ADB WI-FI MANUAL
    // ============================================================

    private async void WifiAdb_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_viewModel.Device.Connected)
        {
            MessageBox.Show(
                "Conecta y autoriza el teléfono por USB.",
                "NOVORA — ADB Wi-Fi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        if (sender is Button button)
            button.IsEnabled = false;

        try
        {
            var serial =
                await _adb.ConnectOverWifiAsync(
                    _viewModel.Device.Serial);

            var settings =
                _settingsService.Load();

            settings.SelectedDeviceSerial =
                serial;

            _settingsService.Save(
                settings);

            await RefreshDeviceAsync(
                true);

            Log(
                $"[OK] ADB Wi-Fi conectado: {serial}");
        }
        catch (Exception ex)
        {
            Log(
                $"[ERROR] ADB Wi-Fi: {ex.Message}");

            MessageBox.Show(
                ex.Message,
                "NOVORA — ADB Wi-Fi",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            if (sender is Button button2)
                button2.IsEnabled = true;
        }
    }

    // ============================================================
    // REFRESH DEVICES
    // ============================================================

    private async void RefreshDevices_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_refreshingDevices)
            return;

        _refreshingDevices = true;

        if (RefreshDevicesButton is not null)
            RefreshDevicesButton.IsEnabled = false;

        try
        {
            _viewModel.ConnectionStatus =
                "Buscando dispositivos...";

            await RefreshDeviceAsync(
                true);
        }
        catch (Exception ex)
        {
            Log(
                $"[ERROR] Dispositivos: {ex.Message}");

            MessageBox.Show(
                ex.Message,
                "NOVORA — Dispositivos",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _refreshingDevices = false;

            if (RefreshDevicesButton is not null)
                RefreshDevicesButton.IsEnabled = true;
        }
    }

    // ============================================================
    // DEVICE SELECTION
    // ============================================================

    private void Device_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        var device =
            _viewModel.Device;

        if (device is null ||
            !device.Connected)
        {
            return;
        }

        var settings =
            _settingsService.Load();

        settings.SelectedDeviceSerial =
            device.Serial;

        _settingsService.Save(
            settings);

        _deviceState.Invalidate(
            device.Serial);

        RefreshOutputProfile();

        _ = RefreshDynamicWidgetsAsync();
    }

    // ============================================================
    // MONITOR SELECTION
    // ============================================================

    private void Monitor_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        RefreshOutputProfile();
    }

    // ============================================================
    // OUTPUT PROFILE
    // ============================================================

    private void RefreshOutputProfile()
    {
        if (_viewModel.Device is null ||
            !_viewModel.Device.Connected ||
            _viewModel.SelectedMonitor is null)
        {
            return;
        }

        _viewModel.OutputProfile =
            _outputProfileService.Calculate(
                _viewModel.Device,
                _viewModel.SelectedMonitor,
                _viewModel.Bitrate,
                _viewModel.TargetFps,
                _viewModel.MaxSize);

        _viewModel.PerformanceSummary =
            _viewModel.OutputProfile.Summary;
    }

    // ============================================================
    // CONFIGURATION
    // ============================================================

    private void Configuration_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void OpenSettings()
    {
        var dialog =
            new SettingsWindow(
                _viewModel,
                _widgetManager,
                _widgetRegistry)
            {
                Owner = this
            };

        if (dialog.ShowDialog() == true)
        {
            ThemeService.Apply(
                _viewModel.Theme);

            RefreshOutputProfile();

            RebuildWidgets();

            Log(
                "[OK] Configuración guardada.");
        }
    }

    // ============================================================
    // WINDOW CONTROLS
    // ============================================================

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
        _allowClose = true;

        Close();
    }

    private void Maximize_Click(
        object sender,
        RoutedEventArgs e)
    {
        WindowState =
            WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
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

    // ============================================================
    // WIDGETS
    // ============================================================

    private void RebuildWidgets()
    {
        if (WidgetHost is null)
            return;

        WidgetHost.Children.Clear();

        _redWidgetStatus = null;
        _performanceWidgetStatus = null;
        _videoWidgetStatus = null;
        _statisticsWidgetStatus = null;

        var active =
            new List<string>();

        if (_viewModel.ShowRefWidget)
            active.Add("ref");

        if (_viewModel.ShowRedWidget)
            active.Add("red");

        if (_viewModel.ShowVideoWidget)
            active.Add("video");

        if (_viewModel.ShowStatisticsWidget)
            active.Add("statistics");

        if (_viewModel.ShowPerformanceWidget)
            active.Add("performance");

        if (_viewModel.ShowCaptureWidget)
            active.Add("capture");

        if (_viewModel.ShowFilesWidget)
            active.Add("files");

        if (_viewModel.ShowApplicationsWidget)
            active.Add("applications");

        if (_viewModel.ShowConsoleWidget)
            active.Add("console");

        WidgetHost.Columns =
            active.Count switch
            {
                0 => 1,
                1 => 1,
                2 => 2,
                _ => 3
            };

        foreach (var id in active)
        {
            WidgetHost.Children.Add(
                CreateWidget(id));
        }

        if (active.Count == 0)
        {
            WidgetHost.Children.Add(
                Card(
                    "WIDGETS",
                    "No hay widgets activos. " +
                    "Abre WIDGETS en la barra superior " +
                    "para agregar los que necesites.",
                    null));
        }
    }

    private Border Card(
        string title,
        string text,
        UIElement? body)
    {
        var stack =
            new StackPanel
            {
                Margin =
                    new Thickness(10)
            };

        stack.Children.Add(
            new TextBlock
            {
                Text =
                    title.ToUpperInvariant(),

                FontSize = 13,

                FontWeight =
                    FontWeights.Bold,

                Foreground =
                    (Brush)FindResource(
                        "BlueBrush"),

                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        7)
            });

        stack.Children.Add(
            new TextBlock
            {
                Text = text,

                Foreground =
                    (Brush)FindResource(
                        "MutedBrush"),

                TextWrapping =
                    TextWrapping.Wrap
            });

        if (body is not null)
            stack.Children.Add(body);

        return new Border
        {
            Margin =
                new Thickness(4),

            Padding =
                new Thickness(8),

            CornerRadius =
                new CornerRadius(8),

            BorderThickness =
                new Thickness(1),

            BorderBrush =
                (Brush)FindResource(
                    "BorderBrush"),

            Background =
                (Brush)FindResource(
                    "PanelBrush2"),

            Child = stack,

            MinHeight = 105
        };
    }

    private Border CreateWidget(
        string id)
    {
        return id switch
        {
            "ref" =>
                CreateRefWidget(),

            "red" =>
                CreateRedWidget(),

            "video" =>
                CreateVideoWidget(),

            "statistics" =>
                CreateStatisticsWidget(),

            "performance" =>
                CreatePerformanceWidget(),

            "capture" =>
                CreateCaptureWidget(),

            "files" =>
                CreateFilesWidget(),

            "applications" =>
                CreateApplicationsWidget(),

            "console" =>
                CreateConsoleWidget(),

            _ =>
                Card(
                    id,
                    "Widget no reconocido.",
                    null)
        };
    }

    private Border CreateRefWidget()
    {
        return Card(
            "REF",
            "Control de referencia de la salida Android.",
            MakeButton(
                "INICIAR / ENFOCAR SCRCPY",
                Start_Click));
    }

    private Border CreateRedWidget()
    {
        var panel =
            new StackPanel();

        _redWidgetStatus =
            new TextBlock
            {
                Text =
                    "Consultando...",

                Foreground =
                    (Brush)FindResource(
                        "MutedBrush"),

                TextWrapping =
                    TextWrapping.Wrap,

                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        6)
            };

        panel.Children.Add(
            _redWidgetStatus);

        var row =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal
            };

        row.Children.Add(
            MakeButton(
                "ACTUALIZAR",
                async (_, _) =>
                    await RefreshRedAsync()));

        row.Children.Add(
            MakeButton(
                _gnirehtet.IsActive
                    ? "DETENER VPN"
                    : "INICIAR VPN",
                Gnirehtet_Click));

        panel.Children.Add(row);

        return Card(
            "RED",
            "ADB, Internet y Gnirehtet " +
            "del dispositivo seleccionado.",
            panel);
    }

    private Border CreateVideoWidget()
    {
        _videoWidgetStatus =
            new TextBlock
            {
                Text =
                    _viewModel.OutputProfile?.Summary
                    ??
                    "Sin transmisión activa.",

                Foreground =
                    (Brush)FindResource(
                        "BlueBrush"),

                TextWrapping =
                    TextWrapping.Wrap,

                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        0)
            };

        return Card(
            "SALIDA DE VIDEO",
            "Bitrate, FPS y resolución de scrcpy.",
            _videoWidgetStatus);
    }

    private Border CreateStatisticsWidget()
    {
        _statisticsWidgetStatus =
            new TextBlock
            {
                Text =
                    $"Configurado: " +
                    $"{_viewModel.TargetFps} FPS · " +
                    $"{_viewModel.Bitrate} · " +
                    $"{_viewModel.MaxSize}",

                Foreground =
                    (Brush)FindResource(
                        "GreenBrush"),

                TextWrapping =
                    TextWrapping.Wrap,

                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        0)
            };

        return Card(
            "ESTADÍSTICAS",
            "Datos de transmisión sin sondeo " +
            "constante del teléfono.",
            _statisticsWidgetStatus);
    }

    private Border CreatePerformanceWidget()
    {
        var panel =
            new StackPanel();

        _performanceWidgetStatus =
            new TextBlock
            {
                Text =
                    "Consultando...",

                Foreground =
                    (Brush)FindResource(
                        "MutedBrush"),

                TextWrapping =
                    TextWrapping.Wrap,

                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        0)
            };

        panel.Children.Add(
            _performanceWidgetStatus);

        return Card(
            "RENDIMIENTO",
            "CPU, RAM, batería y temperatura.",
            panel);
    }

    private Border CreateCaptureWidget()
    {
        return Card(
            "CAPTURA",
            "Captura la pantalla actual del dispositivo.",
            MakeButton(
                "CAPTURAR PANTALLA",
                Capture_Click));
    }

    private Border CreateFilesWidget()
    {
        var panel =
            new StackPanel();

        var list =
            new ListBox
            {
                Height = 100,

                Background =
                    (Brush)FindResource(
                        "InputBackgroundBrush"),

                Foreground =
                    (Brush)FindResource(
                        "InputForegroundBrush")
            };

        panel.Children.Add(list);

        var row =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal
            };

        row.Children.Add(
            MakeButton(
                "ACTUALIZAR",
                async (_, _) =>
                {
                    try
                    {
                        list.ItemsSource =
                            await _fileService
                                .ListFilesAsync(
                                    _viewModel.Device);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            ex.Message,
                            "NOVORA — Archivos",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }));

        panel.Children.Add(row);

        return Card(
            "ARCHIVOS",
            "Acceso a archivos del dispositivo " +
            "mediante ADB.",
            panel);
    }

    private Border CreateApplicationsWidget()
    {
        var panel =
            new StackPanel();

        var list =
            new ListBox
            {
                Height = 85,

                Background =
                    (Brush)FindResource(
                        "InputBackgroundBrush"),

                Foreground =
                    (Brush)FindResource(
                        "InputForegroundBrush")
            };

        panel.Children.Add(list);

        var row =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal
            };

        row.Children.Add(
            MakeButton(
                "ACTUALIZAR",
                async (_, _) =>
                {
                    try
                    {
                        list.ItemsSource =
                            await _applicationService
                                .ListPackagesAsync(
                                    _viewModel.Device);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            ex.Message,
                            "NOVORA — Aplicaciones",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }));

        row.Children.Add(
            MakeButton(
                "ABRIR",
                async (_, _) =>
                {
                    if (list.SelectedItem
                        is string packageName)
                    {
                        await _applicationService
                            .LaunchAsync(
                                _viewModel.Device,
                                packageName);
                    }
                }));

        row.Children.Add(
            MakeButton(
                "DETENER",
                async (_, _) =>
                {
                    if (list.SelectedItem
                        is string packageName)
                    {
                        await _applicationService
                            .StopAsync(
                                _viewModel.Device,
                                packageName);
                    }
                }));

        panel.Children.Add(row);

        return Card(
            "APLICACIONES",
            "Lista y acciones básicas sobre " +
            "paquetes Android mediante ADB.",
            panel);
    }

    private Border CreateConsoleWidget()
    {
        return Card(
            "CONSOLA",
            "La consola principal ya está visible " +
            "en la ventana.",
            null);
    }

    private Button MakeButton(
        string text,
        RoutedEventHandler handler)
    {
        var button =
            new Button
            {
                Content = text,

                Style =
                    (Style)FindResource(
                        "ActionButton"),

                Padding =
                    new Thickness(
                        8,
                        4,
                        8,
                        4),

                Margin =
                    new Thickness(3),

                MinHeight = 30
            };

        button.Click += handler;

        return button;
    }

    // ============================================================
    // REFRESH WIDGETS
    // ============================================================

    private async Task RefreshDynamicWidgetsAsync()
    {
        var device =
            _viewModel.Device;

        if (device is null ||
            !device.Connected)
        {
            return;
        }

        try
        {
            if (_viewModel.ShowRedWidget)
                await RefreshRedAsync();

            if (_viewModel.ShowPerformanceWidget)
                await RefreshPerformanceAsync();

            if (_videoWidgetStatus is not null)
            {
                _videoWidgetStatus.Text =
                    _viewModel.OutputProfile?.Summary
                    ??
                    "Sin transmisión activa.";
            }

            if (_statisticsWidgetStatus is not null)
            {
                _statisticsWidgetStatus.Text =
                    $"Configurado: " +
                    $"{_viewModel.TargetFps} FPS · " +
                    $"{_viewModel.Bitrate} · " +
                    $"{_viewModel.MaxSize}";
            }
        }
        catch (Exception ex)
        {
            Log(
                $"[INFO] Actualización de widgets: " +
                $"{ex.Message}");
        }
    }

    // ============================================================
    // RED
    // ============================================================

    private async Task RefreshRedAsync()
    {
        var device =
            _viewModel.Device;

        if (device is null ||
            !device.Connected)
        {
            return;
        }

        var network =
            await _deviceState
                .GetNetworkAsync(
                    device);

        var text =
            $"ADB: conectado\n" +
            $"IP: {network.IpAddress} · " +
            $"Interfaz: {network.InterfaceName}\n" +
            $"Internet: " +
            $"{(
                network.InternetAvailable
                    ? "disponible"
                    : "no disponible"
            )} · " +
            $"Ping: " +
            $"{(
                network.LatencyMs >= 0
                    ? network.LatencyMs + " ms"
                    : "—"
            )}\n" +
            $"Gnirehtet: " +
            $"{(
                _gnirehtet.IsActive
                    ? "activo"
                    : "inactivo"
            )}";

        if (_redStatus is not null)
            _redStatus.Text = text;

        if (_redWidgetStatus is not null)
            _redWidgetStatus.Text = text;

        _viewModel.GnirehtetStatus =
            _gnirehtet.IsActive
                ? "Activo"
                : "No iniciado";
    }

    // ============================================================
    // PERFORMANCE
    // ============================================================

    private async Task RefreshPerformanceAsync()
    {
        var device =
            _viewModel.Device;

        if (device is null ||
            !device.Connected)
        {
            return;
        }

        var metrics =
            await _deviceState
                .GetMetricsAsync(
                    device);

        var ram =
            metrics.TotalMemoryKb > 0
                ? (double)metrics.UsedMemoryKb /
                  metrics.TotalMemoryKb *
                  100
                : 0;

        var text =
            $"CPU: {metrics.CpuPercent:0.#}%\n" +
            $"RAM: {ram:0.#}%\n" +
            $"Batería: {metrics.BatteryPercent}% · " +
            $"Temp: {metrics.BatteryTemperatureC:0.#} °C";

        if (_performanceStatus is not null)
            _performanceStatus.Text = text;

        if (_performanceWidgetStatus is not null)
            _performanceWidgetStatus.Text = text;
    }

    // ============================================================
    // CAPTURA
    // ============================================================

    private async void Capture_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var device =
                _viewModel.Device;

            if (device is null ||
                !device.Connected)
            {
                throw new InvalidOperationException(
                    "No hay teléfono conectado.");
            }

            var pictures =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyPictures);

            var folder =
                Path.Combine(
                    pictures,
                    "NOVORA");

            Directory.CreateDirectory(
                folder);

            var path =
                Path.Combine(
                    folder,
                    $"NOVORA_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            var bytes =
                await _adb.CaptureScreenAsync(
                    device.Serial);

            await File.WriteAllBytesAsync(
                path,
                bytes);

            Log(
                $"[OK] Captura guardada: {path}");
        }
        catch (Exception ex)
        {
            Log(
                $"[ERROR] Captura: {ex.Message}");
        }
    }

    // ============================================================
    // GNIREHTET
    // ============================================================

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
                "Conecta y autoriza un dispositivo Android.",
                "Gnirehtet",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        try
        {
            if (_gnirehtet.IsActive)
            {
                await _gnirehtet.StopAsync(
                    device.Serial);

                Log(
                    "[OK] Gnirehtet detenido.");
            }
            else
            {
                var devices =
                    await _adb.GetDevicesAsync();

                var result =
                    await _gnirehtet.StartAsync(
                        device,
                        devices.Count);

                Log(
                    result.Success
                        ? "[OK] Gnirehtet activo."
                        : $"[ERROR] Gnirehtet: " +
                          $"{result.Message}");
            }

            _deviceState.Invalidate(
                device.Serial);

            await RefreshDynamicWidgetsAsync();
        }
        catch (Exception ex)
        {
            Log(
                $"[ERROR] Gnirehtet: {ex.Message}");
        }
    }

    // ============================================================
    // SCRCPY START
    // ============================================================

    private async void Start_Click(
        object sender,
        RoutedEventArgs e)
    {
        var device =
            _viewModel.Device;

        if (device is null ||
            !device.Connected)
        {
            MessageBox.Show(
                "Conecta y autoriza un dispositivo Android.",
                "NOVORA",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        if (_scrcpyProcess is
            { HasExited: false })
        {
            return;
        }

        try
        {
            var monitor =
                _viewModel.SelectedMonitor
                ??
                _monitorService.GetBestMonitor(
                    _monitors);

            var profile =
                _outputProfileService.Calculate(
                    device,
                    monitor,
                    _viewModel.Bitrate,
                    _viewModel.TargetFps,
                    _viewModel.MaxSize);

            _viewModel.OutputProfile =
                profile;

            _scrcpyProcess =
                _scrcpyService.StartOptimized(
                    device,
                    monitor,
                    profile,
                    _viewModel.AudioEnabled);

            _scrcpyProcess.EnableRaisingEvents =
                true;

            _scrcpyProcess.Exited +=
                ScrcpyProcess_Exited;

            _viewModel.ConnectionStatus =
                "● NOVORA ACTIVO";

            Log(
                "[OK] Scrcpy iniciado.");
        }
        catch (Exception ex)
        {
            Log(
                $"[ERROR] Scrcpy: {ex.Message}");

            MessageBox.Show(
                ex.Message,
                "NOVORA",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        await Task.CompletedTask;
    }

    private void ScrcpyProcess_Exited(
        object? sender,
        EventArgs e)
    {
        Dispatcher.BeginInvoke(
            new Action(
                () =>
                {
                    _scrcpyProcess?.Dispose();

                    _scrcpyProcess =
                        null;

                    if (!_stopping)
                    {
                        _viewModel.ConnectionStatus =
                            "Transmisión finalizada";
                    }
                }));
    }

    // ============================================================
    // SCRCPY STOP
    // ============================================================

    private async void Stop_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_stopping)
            return;

        _stopping = true;

        try
        {
            if (_scrcpyProcess is
                { HasExited: false })
            {
                _scrcpyProcess.Kill(
                    true);
            }

            var device =
                _viewModel.Device;

            if (device is not null &&
                !string.IsNullOrWhiteSpace(
                    device.Serial))
            {
                await _gnirehtet.StopAsync(
                    device.Serial);

                await _adb
                    .StopServerIfNoOtherDevicesAsync(
                        device.Serial);
            }

            _viewModel.ConnectionStatus =
                "Detenido";

            Log(
                "[OK] NOVORA detenido.");
        }
        catch (Exception ex)
        {
            Log(
                $"[INFO] Limpieza: {ex.Message}");
        }
        finally
        {
            _stopping = false;
        }
    }

    // ============================================================
    // CONSOLA
    // ============================================================

    private async Task ExecuteConsoleCommandAsync()
    {
        if (_consoleInput is null ||
            _consoleOutput is null)
        {
            return;
        }

        var command =
            _consoleInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                command))
        {
            return;
        }

        _consoleInput.Clear();

        Log(
            $"> {command}");

        try
        {
            if (command.Equals(
                    "clear",
                    StringComparison.OrdinalIgnoreCase))
            {
                _consoleOutput.Clear();

                return;
            }

            if (command.Equals(
                    "devices",
                    StringComparison.OrdinalIgnoreCase))
            {
                var devices =
                    await _adb.GetDevicesAsync(
                        CancellationToken.None,
                        true);

                foreach (var device in devices)
                {
                    Log(
                        $"{device.Serial} | " +
                        $"{device.Model} | " +
                        $"{(
                            device.Connected
                                ? "conectado"
                                : "desconectado"
                        )}");
                }

                return;
            }

            if (command.Equals(
                    "refresh",
                    StringComparison.OrdinalIgnoreCase))
            {
                await RefreshDeviceAsync(
                    true);

                return;
            }

            Log(
                "[INFO] Comando no reconocido. " +
                "Usa: devices, refresh, clear");
        }
        catch (Exception ex)
        {
            Log(
                $"[ERROR] Consola: {ex.Message}");
        }
    }

    private void ConsoleRun_Click(
        object sender,
        RoutedEventArgs e)
    {
        _ = ExecuteConsoleCommandAsync();
    }

    private void ConsoleClear_Click(
        object sender,
        RoutedEventArgs e)
    {
        _consoleOutput?.Clear();
    }

    // ============================================================
    // ACTUALIZACIONES
    // ============================================================

    private async void Update_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button button)
            button.IsEnabled = false;

        try
        {
            Log(
                "[INFO] Buscando actualizaciones...");

            var updateInfo =
                await _updateService
                    .CheckForUpdatesAsync();

            // ----------------------------------------------------
            // No hay actualización
            // ----------------------------------------------------

            if (updateInfo is null)
            {
                Log(
                    "[INFO] No hay una actualización disponible.");

                MessageBox.Show(
                    "Ya tienes la última versión de NOVORA.",
                    "NOVORA — Actualizaciones",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            // ----------------------------------------------------
            // Actualización encontrada
            // ----------------------------------------------------

            var versionText =
                updateInfo.LatestVersion;

            var releaseName =
                string.IsNullOrWhiteSpace(
                    updateInfo.ReleaseName)
                    ? updateInfo.TagName
                    : updateInfo.ReleaseName;

            var notes =
                string.IsNullOrWhiteSpace(
                    updateInfo.ReleaseNotes)
                    ? "No hay notas de versión."
                    : updateInfo.ReleaseNotes;

            var message =
                $"Nueva versión disponible: " +
                $"{versionText}\n\n" +
                $"{releaseName}\n\n" +
                $"{notes}\n\n" +
                $"Versión actual: " +
                $"{_updateService.CurrentVersion}\n" +
                $"Nueva versión: " +
                $"{versionText}\n\n" +
                "¿Quieres descargar e instalar la actualización?";

            var result =
                MessageBox.Show(
                    message,
                    "NOVORA — Actualización disponible",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

            if (result !=
                MessageBoxResult.Yes)
            {
                return;
            }

            // ----------------------------------------------------
            // Descargar
            // ----------------------------------------------------

            Log(
                $"[INFO] Descargando " +
                $"{updateInfo.AssetName}...");

            var progress =
                new Progress<int>(
                    value =>
                    {
                        Log(
                            $"[UPDATE] Descarga: {value}%");
                    });

            await _updateService
                .InstallAndRestartAsync(
                    updateInfo,
                    progress);

            // ----------------------------------------------------
            // InstallAndRestartAsync termina NOVORA mediante
            // Environment.Exit(0), por lo que normalmente esta
            // línea no será alcanzada.
            // ----------------------------------------------------

        }
        catch (OperationCanceledException)
        {
            Log(
                "[INFO] Actualización cancelada.");
        }
        catch (Exception ex)
        {
            Log(
                $"[ERROR] Actualización: {ex.Message}");

            MessageBox.Show(
                ex.Message,
                "NOVORA — Actualizaciones",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            if (sender is Button button2)
                button2.IsEnabled = true;
        }
    }

    // ============================================================
    // CLOSING
    // ============================================================

    private void Window_Closing(
        object sender,
        System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;

            Hide();

            return;
        }

        _widgetTimer.Stop();

        try
        {
            if (_scrcpyProcess is
                { HasExited: false })
            {
                _scrcpyProcess.Kill(
                    true);
            }
        }
        catch
        {
            // No impedir el cierre.
        }

        try
        {
            _widgetManager.SaveAll();
        }
        catch
        {
            // No impedir el cierre.
        }
    }
}
