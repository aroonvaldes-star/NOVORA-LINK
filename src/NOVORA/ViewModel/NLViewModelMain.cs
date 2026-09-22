using NOVORA.Model;
using NOVORA.Service;
using NOVORA.VisionEngine.Audio;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NOVORA.ViewModel;

public sealed record NLViewModelSettingOption<T>(
    T Value,
    string Label)
{
    public override string ToString()
        => Label;
}

public sealed class NLViewModelMain : INotifyPropertyChanged
{
    public const string VideoPresentationModeWindow = "Window";
    public const string VideoPresentationModeFullscreen = "Fullscreen";

    private bool _audioEnabled = true;
    private IReadOnlyList<NLModelDeviceInfo> _devices = Array.Empty<NLModelDeviceInfo>();
    private NLModelDeviceInfo _device = new();
    private string _connectionStatus = "Sin comprobar";
    private NLModelMonitorInfo? _selectedMonitor;
    private string _performanceSummary = "Esperando dispositivo...";
    private IReadOnlyList<NLModelMonitorInfo> _monitors = Array.Empty<NLModelMonitorInfo>();
    private NLModelOutputProfile? _outputProfile;
    private string _bitrate = "8M";
    private int _targetFps = 45;
    private int _maxSize = 1280;
    private string _theme = NLServiceTheme.Dark;
    private string _videoPresentationMode = VideoPresentationModeWindow;
    private string _selectedAudioOutput = VEAudioOutput.DefaultValueVE;
    private IReadOnlyList<NLViewModelSettingOption<string>> _audioOutputOptions = Array.Empty<NLViewModelSettingOption<string>>();
    private IReadOnlyList<NLViewModelSettingOption<int>> _resolutionOptions = Array.Empty<NLViewModelSettingOption<int>>();
    private IReadOnlyList<NLViewModelSettingOption<int>> _fpsOptions = Array.Empty<NLViewModelSettingOption<int>>();
    private bool _privacyShieldEnabled;
    private bool _integrationClipboardEnabled = true;
    private bool _integrationFileTransferEnabled = true;
    private bool _integrationDragDropEnabled = true;
    private bool _integrationApplicationsEnabled = true;
    private bool _integrationNotificationsEnabled = true;
    private bool _integrationDynamicResizeEnabled = true;
    private bool _exInEnabled = true;
    private string _nvidiaProfile = "Competitive";

    public bool AudioEnabled
    {
        get => _audioEnabled;
        set => Set(ref _audioEnabled, value);
    }

    public IReadOnlyList<NLModelDeviceInfo> Devices
    {
        get => _devices;
        set => Set(ref _devices, value ?? Array.Empty<NLModelDeviceInfo>());
    }

    public NLModelDeviceInfo Device
    {
        get => _device;
        set
        {
            if (!Set(
                    ref _device,
                    value ?? new NLModelDeviceInfo()))
            {
                return;
            }

            OnPropertyChanged(nameof(DeviceDisplayName));
            OnPropertyChanged(nameof(DeviceConnected));
            NotifyCapabilityLabelsVE();
            RefreshOutputCapabilityOptions();
        }
    }

    public string DeviceDisplayName =>
        Device.FriendlyName;

    public bool DeviceConnected =>
        Device.Connected;

    /// <summary>
    /// Resolución física máxima/nativa detectada del teléfono.
    /// Se expone como propiedad plana para evitar bindings frágiles a
    /// propiedades calculadas anidadas dentro de Run.
    /// </summary>
    public string DeviceNativeResolutionLabel
    {
        get
        {
            NLModelDeviceCapabilities capabilities =
                ResolveCapabilities();

            return capabilities.IsDetected
                ? capabilities.NativeResolutionLabel
                : "No detectada";
        }
    }

    /// <summary>
    /// Máximo FPS seleccionable según la frecuencia máxima del panel Android
    /// detectada por ADB/DisplayManager.
    /// </summary>
    public string DeviceMaxFpsLabel
    {
        get
        {
            NLModelDeviceCapabilities capabilities =
                ResolveCapabilities();

            return capabilities.IsDetected
                ? $"{capabilities.MaxSelectableFps} FPS"
                : "No detectados";
        }
    }

    public string DeviceMaxRefreshRateLabel
    {
        get
        {
            NLModelDeviceCapabilities capabilities =
                ResolveCapabilities();

            return capabilities.IsDetected
                ? $"{capabilities.MaxRefreshRateHz:0.#} Hz"
                : "No detectados";
        }
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => Set(ref _connectionStatus, value);
    }

    public string PerformanceSummary
    {
        get => _performanceSummary;
        set => Set(ref _performanceSummary, value);
    }

    public IReadOnlyList<NLModelMonitorInfo> Monitors
    {
        get => _monitors;
        set => Set(ref _monitors, value ?? Array.Empty<NLModelMonitorInfo>());
    }

    public NLModelMonitorInfo? SelectedMonitor
    {
        get => _selectedMonitor;
        set => Set(ref _selectedMonitor, value);
    }

    public NLModelOutputProfile? NLModelOutputProfile
    {
        get => _outputProfile;
        set
        {
            if (Set(ref _outputProfile, value))
            {
                OnPropertyChanged(nameof(OutputSummary));
            }
        }
    }

    public string OutputSummary =>
        NLModelOutputProfile?.Summary ?? "Sin perfil de salida.";

    public string Bitrate
    {
        get => _bitrate;
        set => Set(
            ref _bitrate,
            NLServiceBitrate.Normalize(value));
    }

    public int TargetFps
    {
        get => _targetFps;
        set => Set(
            ref _targetFps,
            Math.Max(15, value));
    }

    public int MaxSize
    {
        get => _maxSize;
        set => Set(
            ref _maxSize,
            Math.Max(480, value));
    }

    public string Theme
    {
        get => _theme;
        set => Set(
            ref _theme,
            string.Equals(
                value,
                NLServiceTheme.Light,
                StringComparison.OrdinalIgnoreCase)
                ? NLServiceTheme.Light
                : NLServiceTheme.Dark);
    }

    public string VideoPresentationMode
    {
        get => _videoPresentationMode;
        set => Set(
            ref _videoPresentationMode,
            string.Equals(
                value,
                VideoPresentationModeFullscreen,
                StringComparison.OrdinalIgnoreCase)
                ? VideoPresentationModeFullscreen
                : VideoPresentationModeWindow);
    }

    public string SelectedAudioOutput
    {
        get => _selectedAudioOutput;
        set
        {
            string normalized =
                string.IsNullOrWhiteSpace(value)
                    ? VEAudioOutput.DefaultValueVE
                    : value.Trim();

            if (!Set(
                    ref _selectedAudioOutput,
                    normalized))
            {
                return;
            }

            AudioEnabled =
                !string.Equals(
                    normalized,
                    VEAudioOutput.DisabledValueVE,
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyList<NLViewModelSettingOption<string>> AudioOutputOptions
    {
        get => _audioOutputOptions;
        private set => Set(
            ref _audioOutputOptions,
            value ?? Array.Empty<NLViewModelSettingOption<string>>());
    }

    public IReadOnlyList<NLViewModelSettingOption<int>> ResolutionOptions
    {
        get => _resolutionOptions;
        private set => Set(
            ref _resolutionOptions,
            value ?? Array.Empty<NLViewModelSettingOption<int>>());
    }

    public IReadOnlyList<NLViewModelSettingOption<int>> FpsOptions
    {
        get => _fpsOptions;
        private set => Set(
            ref _fpsOptions,
            value ?? Array.Empty<NLViewModelSettingOption<int>>());
    }

    public bool PrivacyShieldEnabled
    {
        get => _privacyShieldEnabled;
        set => Set(ref _privacyShieldEnabled, value);
    }

    public bool IntegrationClipboardEnabled
    {
        get => _integrationClipboardEnabled;
        set => Set(ref _integrationClipboardEnabled, value);
    }

    public bool IntegrationFileTransferEnabled
    {
        get => _integrationFileTransferEnabled;
        set => Set(ref _integrationFileTransferEnabled, value);
    }

    public bool IntegrationDragDropEnabled
    {
        get => _integrationDragDropEnabled;
        set => Set(ref _integrationDragDropEnabled, value);
    }

    public bool IntegrationApplicationsEnabled
    {
        get => _integrationApplicationsEnabled;
        set => Set(ref _integrationApplicationsEnabled, value);
    }

    public bool IntegrationNotificationsEnabled
    {
        get => _integrationNotificationsEnabled;
        set => Set(ref _integrationNotificationsEnabled, value);
    }

    public bool IntegrationDynamicResizeEnabled
    {
        get => _integrationDynamicResizeEnabled;
        set => Set(ref _integrationDynamicResizeEnabled, value);
    }

    public bool ExInEnabled
    {
        get => _exInEnabled;
        set => Set(ref _exInEnabled, value);
    }

    public string NvidiaProfile
    {
        get => _nvidiaProfile;
        set => Set(
            ref _nvidiaProfile,
            string.IsNullOrWhiteSpace(value)
                ? "Automatic"
                : value.Trim());
    }


    public IReadOnlyList<NLViewModelSettingOption<string>> NvidiaProfileOptions { get; } =
        new[]
        {
            new NLViewModelSettingOption<string>("Automatic", "Automático"),
            new NLViewModelSettingOption<string>("Disabled", "Desactivado"),
            new NLViewModelSettingOption<string>("Competitive", "Competitivo"),
            new NLViewModelSettingOption<string>("Balanced", "Balanceado"),
            new NLViewModelSettingOption<string>("VisionPlus", "Vision+"),
            new NLViewModelSettingOption<string>("Smooth", "Suavidad"),
            new NLViewModelSettingOption<string>("Stream", "Streaming")
        };

    public IReadOnlyList<NLViewModelSettingOption<string>> BitrateOptions { get; } =
        new[]
        {
            new NLViewModelSettingOption<string>("2M", "2 Mb/s"),
            new NLViewModelSettingOption<string>("3M", "3 Mb/s"),
            new NLViewModelSettingOption<string>("4M", "4 Mb/s"),
            new NLViewModelSettingOption<string>("5M", "5 Mb/s"),
            new NLViewModelSettingOption<string>("6M", "6 Mb/s"),
            new NLViewModelSettingOption<string>("7M", "7 Mb/s"),
            new NLViewModelSettingOption<string>("8M", "8 Mb/s"),
            new NLViewModelSettingOption<string>("9M", "9 Mb/s"),
            new NLViewModelSettingOption<string>("10M", "10 Mb/s"),
            new NLViewModelSettingOption<string>("11M", "11 Mb/s"),
            new NLViewModelSettingOption<string>("12M", "12 Mb/s"),
            new NLViewModelSettingOption<string>("13M", "13 Mb/s"),
            new NLViewModelSettingOption<string>("14M", "14 Mb/s"),
            new NLViewModelSettingOption<string>("15M", "15 Mb/s")
        };

    public IReadOnlyList<NLViewModelSettingOption<string>> VideoPresentationModeOptions { get; } =
        new[]
        {
            new NLViewModelSettingOption<string>(
                VideoPresentationModeWindow,
                "Ventana"),

            new NLViewModelSettingOption<string>(
                VideoPresentationModeFullscreen,
                "Pantalla completa")
        };

    public IReadOnlyList<NLViewModelSettingOption<string>> ThemeOptions { get; } =
        new[]
        {
            new NLViewModelSettingOption<string>(NLServiceTheme.Dark, "Dark"),
            new NLViewModelSettingOption<string>(NLServiceTheme.Light, "Light")
        };

    public void RefreshAudioOutputOptions(
        NLServiceNovoraPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        IReadOnlyList<VEAudioOutput> outputs =
            VEAudioOutput.GetAvailableVE(paths);

        AudioOutputOptions =
            outputs
                .Select(
                    output =>
                        new NLViewModelSettingOption<string>(
                            output.Value,
                            output.Label))
                .ToArray();

        bool selectedExists =
            AudioOutputOptions.Any(
                option =>
                    string.Equals(
                        option.Value,
                        SelectedAudioOutput,
                        StringComparison.OrdinalIgnoreCase));

        if (!selectedExists)
        {
            SelectedAudioOutput =
                AudioEnabled
                    ? VEAudioOutput.DefaultValueVE
                    : VEAudioOutput.DisabledValueVE;
        }
    }

    public void RefreshOutputCapabilityOptions()
    {
        NotifyCapabilityLabelsVE();

        if (!Device.Connected)
        {
            FpsOptions =
                Array.Empty<NLViewModelSettingOption<int>>();

            ResolutionOptions =
                Array.Empty<NLViewModelSettingOption<int>>();

            return;
        }

        NLModelDeviceCapabilities capabilities =
            ResolveCapabilities();

        if (!capabilities.IsDetected)
        {
            FpsOptions =
                Array.Empty<NLViewModelSettingOption<int>>();

            ResolutionOptions =
                Array.Empty<NLViewModelSettingOption<int>>();

            return;
        }

        FpsOptions =
            BuildFpsOptions(
                capabilities);

        ResolutionOptions =
            BuildResolutionOptions(
                capabilities);

        ClampConfiguredOutputValues(
            capabilities);
    }

    private NLModelDeviceCapabilities ResolveCapabilities()
    {
        if (Device.Capabilities.IsDetected)
        {
            return Device.Capabilities;
        }

        IReadOnlyList<NLModelDisplayModeInfo> modes =
            Device.SupportedDisplayModes.Count > 0
                ? Device.SupportedDisplayModes
                : Device.BestDisplayMode is null
                    ? Array.Empty<NLModelDisplayModeInfo>()
                    : new[] { Device.BestDisplayMode };

        if (modes.Count == 0)
        {
            return NLModelDeviceCapabilities.Unknown;
        }

        NLModelDisplayModeInfo native =
            modes
                .OrderByDescending(
                    mode =>
                        mode.Pixels)
                .ThenByDescending(
                    mode =>
                        mode.RefreshRateHz)
                .First();

        double[] rates =
            modes
                .Where(
                    mode =>
                        mode.RefreshRateHz >= 20)
                .Select(
                    mode =>
                        Math.Round(
                            mode.RefreshRateHz,
                            1,
                            MidpointRounding.AwayFromZero))
                .Distinct()
                .OrderBy(
                    value =>
                        value)
                .ToArray();

        return new NLModelDeviceCapabilities
        {
            NativeWidth =
                native.Width,

            NativeHeight =
                native.Height,

            SupportedRefreshRatesHz =
                rates.Length > 0
                    ? rates
                    : new[] { 60d }
        };
    }

    private static IReadOnlyList<NLViewModelSettingOption<int>> BuildFpsOptions(
        NLModelDeviceCapabilities capabilities)
    {
        int maximum =
            capabilities.MaxSelectableFps;

        var values =
            new HashSet<int>();

        if (maximum >= 30)
        {
            values.Add(30);
        }

        if (maximum >= 60)
        {
            values.Add(60);
        }

        foreach (double rate in capabilities.SupportedRefreshRatesHz)
        {
            int fps =
                Math.Max(
                    1,
                    (int)Math.Round(
                        rate,
                        MidpointRounding.AwayFromZero));

            if (fps is >= 15 &&
                fps <= maximum)
            {
                values.Add(fps);
            }
        }

        values.Add(maximum);

        return values
            .Where(
                value =>
                    value is >= 15 &&
                    value <= maximum)
            .OrderBy(
                value =>
                    value)
            .Select(
                value =>
                    new NLViewModelSettingOption<int>(
                        value,
                        value == maximum
                            ? $"{value} FPS — máximo del celular"
                            : $"{value} FPS"))
            .ToArray();
    }

    private static IReadOnlyList<NLViewModelSettingOption<int>> BuildResolutionOptions(
        NLModelDeviceCapabilities capabilities)
    {
        int maximum =
            capabilities.MaxDimension;

        if (maximum <= 0)
        {
            return Array.Empty<NLViewModelSettingOption<int>>();
        }

        int[] common =
        {
            720,
            1024,
            1280,
            1440,
            1600,
            1920,
            2160,
            2400,
            2560,
            2880,
            3200,
            3840,
            4096,
            5120
        };

        List<int> values =
            common
                .Where(
                    value =>
                        value > 0 &&
                        value <= maximum)
                .ToList();

        if (!values.Contains(maximum))
        {
            values.Add(maximum);
        }

        return values
            .Distinct()
            .OrderBy(
                value =>
                    value)
            .Select(
                value =>
                {
                    double scale =
                        Math.Min(
                            1.0,
                            value /
                            (double)maximum);

                    int width =
                        Math.Max(
                            1,
                            (int)Math.Round(
                                capabilities.NativeWidth *
                                scale));

                    int height =
                        Math.Max(
                            1,
                            (int)Math.Round(
                                capabilities.NativeHeight *
                                scale));

                    string label =
                        value == maximum
                            ? $"{width}x{height} — nativa / máximo del celular"
                            : $"{width}x{height} — max-size {value}";

                    return new NLViewModelSettingOption<int>(
                        value,
                        label);
                })
            .ToArray();
    }

    private void ClampConfiguredOutputValues(
        NLModelDeviceCapabilities capabilities)
    {
        if (TargetFps >
            capabilities.MaxSelectableFps)
        {
            TargetFps =
                capabilities.MaxSelectableFps;
        }

        if (MaxSize >
            capabilities.MaxDimension)
        {
            MaxSize =
                capabilities.MaxDimension;
        }
    }

    private void NotifyCapabilityLabelsVE()
    {
        OnPropertyChanged(
            nameof(DeviceNativeResolutionLabel));

        OnPropertyChanged(
            nameof(DeviceMaxFpsLabel));

        OnPropertyChanged(
            nameof(DeviceMaxRefreshRateLabel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(
                field,
                value))
        {
            return false;
        }

        field =
            value;

        OnPropertyChanged(
            propertyName);

        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
}
