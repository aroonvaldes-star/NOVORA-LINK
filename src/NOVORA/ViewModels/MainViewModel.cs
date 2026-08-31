using NOVORA.Models;
using NOVORA.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NOVORA.ViewModels;

public sealed record SettingOption<T>(T Value, string Label);

public sealed class MainViewModel : INotifyPropertyChanged
{
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
    private string _theme = ThemeService.Dark;
    private IReadOnlyList<SettingOption<int>> _resolutionOptions = Array.Empty<SettingOption<int>>();
    private IReadOnlyList<SettingOption<int>> _fpsOptions = Array.Empty<SettingOption<int>>();

    public bool AudioEnabled { get => _audioEnabled; set => Set(ref _audioEnabled, value); }
    public IReadOnlyList<DeviceInfo> Devices { get => _devices; set => Set(ref _devices, value ?? Array.Empty<DeviceInfo>()); }
    public DeviceInfo Device
    {
        get => _device;
        set
        {
            if (!Set(ref _device, value ?? new())) return;
            OnPropertyChanged(nameof(DeviceDisplayName));
            OnPropertyChanged(nameof(DeviceConnected));
            RefreshOutputCapabilityOptions();
        }
    }
    public string DeviceDisplayName => Device.FriendlyName;
    public bool DeviceConnected => Device.Connected;
    public string ConnectionStatus { get => _connectionStatus; set => Set(ref _connectionStatus, value); }
    public string PerformanceSummary { get => _performanceSummary; set => Set(ref _performanceSummary, value); }
    public string GnirehtetStatus { get => _gnirehtetStatus; set => Set(ref _gnirehtetStatus, value); }
    public IReadOnlyList<MonitorInfo> Monitors { get => _monitors; set => Set(ref _monitors, value ?? Array.Empty<MonitorInfo>()); }
    public MonitorInfo? SelectedMonitor { get => _selectedMonitor; set => Set(ref _selectedMonitor, value); }
    public OutputProfile? OutputProfile { get => _outputProfile; set { if (Set(ref _outputProfile, value)) OnPropertyChanged(nameof(OutputSummary)); } }
    public string OutputSummary => OutputProfile?.Summary ?? "Sin perfil de salida.";
    public string Bitrate { get => _bitrate; set => Set(ref _bitrate, BitrateService.Normalize(value)); }
    public int TargetFps { get => _targetFps; set => Set(ref _targetFps, Math.Max(15, value)); }
    public int MaxSize { get => _maxSize; set => Set(ref _maxSize, Math.Max(480, value)); }
    public string Theme { get => _theme; set => Set(ref _theme, string.Equals(value, ThemeService.Light, StringComparison.OrdinalIgnoreCase) ? ThemeService.Light : ThemeService.Dark); }

    public IReadOnlyList<SettingOption<int>> ResolutionOptions { get => _resolutionOptions; private set => Set(ref _resolutionOptions, value); }
    public IReadOnlyList<SettingOption<int>> FpsOptions { get => _fpsOptions; private set => Set(ref _fpsOptions, value); }
    public IReadOnlyList<SettingOption<string>> BitrateOptions { get; } = new[]
    {
        new SettingOption<string>("1M","1 Mb/s"), new("2M","2 Mb/s"), new("3M","3 Mb/s"), new("4M","4 Mb/s"),
        new("6M","6 Mb/s"), new("8M","8 Mb/s"), new("10M","10 Mb/s"), new("12M","12 Mb/s"), new("16M","16 Mb/s"),
        new("20M","20 Mb/s"), new("25M","25 Mb/s"), new("30M","30 Mb/s"), new("40M","40 Mb/s"), new("50M","50 Mb/s")
    };
    public IReadOnlyList<SettingOption<string>> ThemeOptions { get; } = new[]
    {
        new SettingOption<string>(ThemeService.Dark,"Dark"), new(ThemeService.Light,"Light")
    };

    public void RefreshOutputCapabilityOptions()
    {
        if (!Device.Connected)
        {
            FpsOptions = Array.Empty<SettingOption<int>>();
            ResolutionOptions = Array.Empty<SettingOption<int>>();
            return;
        }

        var capabilities = ResolveCapabilities();
        if (!capabilities.IsDetected)
        {
            FpsOptions = Array.Empty<SettingOption<int>>();
            ResolutionOptions = Array.Empty<SettingOption<int>>();
            return;
        }

        FpsOptions = BuildFpsOptions(capabilities);
        ResolutionOptions = BuildResolutionOptions(capabilities);
        ClampConfiguredOutputValues(capabilities);
    }

    private DeviceCapabilities ResolveCapabilities()
    {
        if (Device.Capabilities.IsDetected) return Device.Capabilities;
        var modes = Device.SupportedDisplayModes.Count > 0
            ? Device.SupportedDisplayModes
            : Device.BestDisplayMode is null ? Array.Empty<DisplayModeInfo>() : new[] { Device.BestDisplayMode };
        if (modes.Count == 0) return DeviceCapabilities.Unknown;
        var native = modes.OrderByDescending(mode => mode.Pixels).ThenByDescending(mode => mode.RefreshRateHz).First();
        var rates = modes.Where(mode => mode.RefreshRateHz >= 20).Select(mode => Math.Round(mode.RefreshRateHz, 1, MidpointRounding.AwayFromZero)).Distinct().OrderBy(value => value).ToArray();
        return new DeviceCapabilities { NativeWidth = native.Width, NativeHeight = native.Height, SupportedRefreshRatesHz = rates.Length > 0 ? rates : new[] { 60d } };
    }

    private static IReadOnlyList<SettingOption<int>> BuildFpsOptions(DeviceCapabilities capabilities)
    {
        var maximum = capabilities.MaxSelectableFps;
        var values = new HashSet<int>();
        if (maximum >= 30) values.Add(30);
        if (maximum >= 60) values.Add(60);
        foreach (var rate in capabilities.SupportedRefreshRatesHz)
        {
            var fps = Math.Max(1, (int)Math.Round(rate, MidpointRounding.AwayFromZero));
            if (fps is >= 15 && fps <= maximum) values.Add(fps);
        }
        values.Add(maximum);
        return values.Where(value => value is >= 15 && value <= maximum).OrderBy(value => value)
            .Select(value => new SettingOption<int>(value, value == maximum ? $"{value} FPS — máximo del celular" : $"{value} FPS")).ToArray();
    }

    private static IReadOnlyList<SettingOption<int>> BuildResolutionOptions(DeviceCapabilities capabilities)
    {
        var maximum = capabilities.MaxDimension;
        if (maximum <= 0) return Array.Empty<SettingOption<int>>();
        int[] common = { 720, 1024, 1280, 1440, 1600, 1920, 2160, 2400, 2560, 2880, 3200, 3840, 4096, 5120 };
        var values = common.Where(value => value > 0 && value <= maximum).ToList();
        if (!values.Contains(maximum)) values.Add(maximum);
        return values.Distinct().OrderBy(value => value).Select(value =>
        {
            var scale = Math.Min(1.0, value / (double)maximum);
            var width = Math.Max(1, (int)Math.Round(capabilities.NativeWidth * scale));
            var height = Math.Max(1, (int)Math.Round(capabilities.NativeHeight * scale));
            var label = value == maximum ? $"{width}x{height} — nativa / máximo del celular" : $"{width}x{height} — max-size {value}";
            return new SettingOption<int>(value, label);
        }).ToArray();
    }

    private void ClampConfiguredOutputValues(DeviceCapabilities capabilities)
    {
        if (TargetFps > capabilities.MaxSelectableFps) TargetFps = capabilities.MaxSelectableFps;
        if (MaxSize > capabilities.MaxDimension) MaxSize = capabilities.MaxDimension;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
