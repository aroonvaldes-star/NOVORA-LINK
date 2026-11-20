using System.ComponentModel;
using System.Runtime.CompilerServices;
using NOVORA.Models;
using NOVORA.Services;
using NOVORA.Widgets;

namespace NOVORA.ViewModels;

public sealed record SettingOption<T>(
    T Value,
    string Label);

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly WidgetRegistry _widgetRegistry;

    private bool _audioEnabled = true;
    private IReadOnlyList<DeviceInfo> _devices = Array.Empty<DeviceInfo>();
    private DeviceInfo _device = new();
    private string _connectionStatus = "Sin comprobar";
    private MonitorInfo? _selectedMonitor;
    private string _performanceSummary = "Esperando dispositivo...";
    private string _gnirehtetStatus = "No iniciado";
    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();
    private OutputProfile? _outputProfile;
    private string _bitrate = "10M";
    private int _targetFps = 60;
    private int _maxSize = 1920;

    private bool _showConsoleWidget;
    private bool _showVideoWidget;
    private bool _showStatisticsWidget;
    private bool _showRefWidget;
    private bool _showRedWidget;
    private bool _showPerformanceWidget;
    private bool _showCaptureWidget;
    private bool _showFilesWidget;
    private bool _showApplicationsWidget;

    private string _theme = ThemeService.Dark;

    private IReadOnlyList<SettingOption<int>> _resolutionOptions = new[]
    {
        new SettingOption<int>(1024, "1024"),
        new SettingOption<int>(1280, "1280"),
        new SettingOption<int>(1600, "1600"),
        new SettingOption<int>(1920, "1920"),
        new SettingOption<int>(2560, "2560"),
        new SettingOption<int>(3200, "3200"),
        new SettingOption<int>(3840, "3840"),
        new SettingOption<int>(5120, "5120")
    };

    private IReadOnlyList<SettingOption<int>> _fpsOptions = new[]
    {
        new SettingOption<int>(30, "30 FPS"),
        new SettingOption<int>(60, "60 FPS"),
        new SettingOption<int>(90, "90 FPS"),
        new SettingOption<int>(120, "120 FPS"),
        new SettingOption<int>(144, "144 FPS"),
        new SettingOption<int>(165, "165 FPS"),
        new SettingOption<int>(240, "240 FPS")
    };

    private IReadOnlyList<SettingOption<string>> _bitrateOptions = new[]
    {
        new SettingOption<string>("1M", "1 Mb/s"),
        new SettingOption<string>("2M", "2 Mb/s"),
        new SettingOption<string>("3M", "3 Mb/s"),
        new SettingOption<string>("4M", "4 Mb/s"),
        new SettingOption<string>("6M", "6 Mb/s"),
        new SettingOption<string>("8M", "8 Mb/s"),
        new SettingOption<string>("10M", "10 Mb/s"),
        new SettingOption<string>("12M", "12 Mb/s"),
        new SettingOption<string>("16M", "16 Mb/s"),
        new SettingOption<string>("20M", "20 Mb/s"),
        new SettingOption<string>("25M", "25 Mb/s"),
        new SettingOption<string>("30M", "30 Mb/s"),
        new SettingOption<string>("40M", "40 Mb/s"),
        new SettingOption<string>("50M", "50 Mb/s")
    };

    public MainViewModel(WidgetRegistry widgetRegistry)
    {
        _widgetRegistry = widgetRegistry
            ?? throw new ArgumentNullException(nameof(widgetRegistry));

        foreach (var widget in _widgetRegistry.All)
        {
            widget.IsActive = GetShowProperty(widget.Id);
            widget.PropertyChanged += Widget_PropertyChanged;
        }
    }

    public bool AudioEnabled
    {
        get => _audioEnabled;
        set
        {
            if (_audioEnabled == value)
                return;

            _audioEnabled = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<DeviceInfo> Devices
    {
        get => _devices;
        set
        {
            _devices = value ?? Array.Empty<DeviceInfo>();
            OnPropertyChanged();
        }
    }

    public DeviceInfo Device
    {
        get => _device;
        set
        {
            _device = value ?? new DeviceInfo();

            OnPropertyChanged();
            OnPropertyChanged(nameof(DeviceDisplayName));
            OnPropertyChanged(nameof(DeviceSerial));
            OnPropertyChanged(nameof(DeviceConnected));
        }
    }

    public string DeviceDisplayName =>
        string.IsNullOrWhiteSpace(Device.CustomName)
            ? Device.Model
            : Device.CustomName;

    public string DeviceSerial =>
        Device.Serial;

    public bool DeviceConnected =>
        Device.Connected;

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set
        {
            if (_connectionStatus == value)
                return;

            _connectionStatus = value;
            OnPropertyChanged();
        }
    }

    public string PerformanceSummary
    {
        get => _performanceSummary;
        set
        {
            if (_performanceSummary == value)
                return;

            _performanceSummary = value;
            OnPropertyChanged();
        }
    }

    public string GnirehtetStatus
    {
        get => _gnirehtetStatus;
        set
        {
            if (_gnirehtetStatus == value)
                return;

            _gnirehtetStatus = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<MonitorInfo> Monitors
    {
        get => _monitors;
        set
        {
            _monitors = value ?? Array.Empty<MonitorInfo>();
            OnPropertyChanged();
        }
    }

    public MonitorInfo? SelectedMonitor
    {
        get => _selectedMonitor;
        set
        {
            if (ReferenceEquals(_selectedMonitor, value))
                return;

            _selectedMonitor = value;
            OnPropertyChanged();
        }
    }

    public OutputProfile? OutputProfile
    {
        get => _outputProfile;
        set
        {
            _outputProfile = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputSummary));
        }
    }

    public string OutputSummary =>
        OutputProfile?.Summary ?? "Sin perfil de salida.";

    public string Bitrate
    {
        get => _bitrate;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                value = "10M";

            value = value.Trim().ToUpperInvariant();

            if (_bitrate == value)
                return;

            _bitrate = value;
            OnPropertyChanged();
        }
    }

    public int TargetFps
    {
        get => _targetFps;
        set
        {
            if (_targetFps == value)
                return;

            _targetFps = value;
            OnPropertyChanged();
        }
    }

    public int MaxSize
    {
        get => _maxSize;
        set
        {
            if (_maxSize == value)
                return;

            _maxSize = value;
            OnPropertyChanged();
        }
    }

    // ============================================================
    // WIDGETS
    // ============================================================

    public bool ShowConsoleWidget
    {
        get => _showConsoleWidget;
        set
        {
            if (_showConsoleWidget == value)
                return;

            _showConsoleWidget = value;
            OnPropertyChanged();
            SyncWidgetState("console", value);
        }
    }

    public bool ShowVideoWidget
    {
        get => _showVideoWidget;
        set
        {
            if (_showVideoWidget == value)
                return;

            _showVideoWidget = value;
            OnPropertyChanged();
            SyncWidgetState("video", value);
        }
    }

    public bool ShowStatisticsWidget
    {
        get => _showStatisticsWidget;
        set
        {
            if (_showStatisticsWidget == value)
                return;

            _showStatisticsWidget = value;
            OnPropertyChanged();
            SyncWidgetState("statistics", value);
        }
    }

    public bool ShowRefWidget
    {
        get => _showRefWidget;
        set
        {
            if (_showRefWidget == value)
                return;

            _showRefWidget = value;
            OnPropertyChanged();
            SyncWidgetState("ref", value);
        }
    }

    public bool ShowRedWidget
    {
        get => _showRedWidget;
        set
        {
            if (_showRedWidget == value)
                return;

            _showRedWidget = value;
            OnPropertyChanged();
            SyncWidgetState("red", value);
        }
    }

    public bool ShowPerformanceWidget
    {
        get => _showPerformanceWidget;
        set
        {
            if (_showPerformanceWidget == value)
                return;

            _showPerformanceWidget = value;
            OnPropertyChanged();
            SyncWidgetState("performance", value);
        }
    }

    public bool ShowCaptureWidget
    {
        get => _showCaptureWidget;
        set
        {
            if (_showCaptureWidget == value)
                return;

            _showCaptureWidget = value;
            OnPropertyChanged();
            SyncWidgetState("capture", value);
        }
    }

    public bool ShowFilesWidget
    {
        get => _showFilesWidget;
        set
        {
            if (_showFilesWidget == value)
                return;

            _showFilesWidget = value;
            OnPropertyChanged();
            SyncWidgetState("files", value);
        }
    }

    public bool ShowApplicationsWidget
    {
        get => _showApplicationsWidget;
        set
        {
            if (_showApplicationsWidget == value)
                return;

            _showApplicationsWidget = value;
            OnPropertyChanged();
            SyncWidgetState("applications", value);
        }
    }

    // ============================================================
    // TEMA
    // ============================================================

    public string Theme
    {
        get => _theme;
        set
        {
            if (_theme == value)
                return;

            _theme = value;
            OnPropertyChanged();
        }
    }

    // ============================================================
    // OPCIONES
    // ============================================================

    public IReadOnlyList<SettingOption<int>> ResolutionOptions
    {
        get => _resolutionOptions;
        set
        {
            _resolutionOptions =
                value ?? Array.Empty<SettingOption<int>>();

            OnPropertyChanged();
        }
    }

    public IReadOnlyList<SettingOption<int>> FpsOptions
    {
        get => _fpsOptions;
        set
        {
            _fpsOptions =
                value ?? Array.Empty<SettingOption<int>>();

            OnPropertyChanged();
        }
    }

    public IReadOnlyList<SettingOption<string>> BitrateOptions
    {
        get => _bitrateOptions;
        set
        {
            _bitrateOptions =
                value ?? Array.Empty<SettingOption<string>>();

            OnPropertyChanged();
        }
    }

    public IReadOnlyList<SettingOption<string>> ThemeOptions { get; } =
        new[]
        {
            new SettingOption<string>(
                ThemeService.Dark,
                "Dark"),

            new SettingOption<string>(
                ThemeService.Light,
                "Light")
        };

    public IReadOnlyList<WidgetDefinition> AvailableWidgets =>
        _widgetRegistry.All;

    // ============================================================
    // SINCRONIZACIÓN DE WIDGETS
    // ============================================================

    private void SyncWidgetState(
        string id,
        bool isActive)
    {
        var widget =
            _widgetRegistry.All.FirstOrDefault(
                w => w.Id == id);

        if (widget != null &&
            widget.IsActive != isActive)
        {
            widget.IsActive = isActive;
        }
    }

    private void Widget_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (sender is not WidgetDefinition widget)
            return;

        if (e.PropertyName !=
            nameof(WidgetDefinition.IsActive))
        {
            return;
        }

        var propertyName =
            GetShowPropertyName(widget.Id);

        if (propertyName == null)
            return;

        var property =
            GetType().GetProperty(propertyName);

        if (property == null ||
            !property.CanWrite)
        {
            return;
        }

        var current =
            (bool)property.GetValue(this)!;

        if (current != widget.IsActive)
        {
            property.SetValue(
                this,
                widget.IsActive);
        }
    }

    private bool GetShowProperty(
        string id)
    {
        return id switch
        {
            "ref" =>
                ShowRefWidget,

            "red" =>
                ShowRedWidget,

            "video" =>
                ShowVideoWidget,

            "statistics" =>
                ShowStatisticsWidget,

            "performance" =>
                ShowPerformanceWidget,

            "capture" =>
                ShowCaptureWidget,

            "files" =>
                ShowFilesWidget,

            "applications" =>
                ShowApplicationsWidget,

            "console" =>
                ShowConsoleWidget,

            _ =>
                false
        };
    }

    private string? GetShowPropertyName(
        string id)
    {
        return id switch
        {
            "ref" =>
                nameof(ShowRefWidget),

            "red" =>
                nameof(ShowRedWidget),

            "video" =>
                nameof(ShowVideoWidget),

            "statistics" =>
                nameof(ShowStatisticsWidget),

            "performance" =>
                nameof(ShowPerformanceWidget),

            "capture" =>
                nameof(ShowCaptureWidget),

            "files" =>
                nameof(ShowFilesWidget),

            "applications" =>
                nameof(ShowApplicationsWidget),

            "console" =>
                nameof(ShowConsoleWidget),

            _ =>
                null
        };
    }

    // ============================================================
    // OPCIONES DINÁMICAS
    // ============================================================

    public void SetResolutionOptions(
        IEnumerable<SettingOption<int>> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ResolutionOptions =
            options
                .GroupBy(x => x.Value)
                .Select(x => x.First())
                .ToArray();
    }

    public void SetFpsOptions(
        IEnumerable<SettingOption<int>> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        FpsOptions =
            options
                .Where(x => x.Value >= 15)
                .GroupBy(x => x.Value)
                .Select(x => x.First())
                .OrderBy(x => x.Value)
                .ToArray();
    }

    public void SetBitrateOptions(
        IEnumerable<SettingOption<string>> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        BitrateOptions =
            options
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Value))
                .GroupBy(
                    x => x.Value,
                    StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToArray();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
