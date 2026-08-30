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

    public bool AudioEnabled { get => _audioEnabled; set => Set(ref _audioEnabled, value); }
    public IReadOnlyList<DeviceInfo> Devices { get => _devices; set => Set(ref _devices, value ?? Array.Empty<DeviceInfo>()); }
    public DeviceInfo Device { get => _device; set { if (Set(ref _device, value ?? new())) { OnPropertyChanged(nameof(DeviceDisplayName)); OnPropertyChanged(nameof(DeviceConnected)); } } }
    public string DeviceDisplayName => string.IsNullOrWhiteSpace(Device.CustomName) ? Device.Model : Device.CustomName;
    public bool DeviceConnected => Device.Connected;
    public string ConnectionStatus { get => _connectionStatus; set => Set(ref _connectionStatus, value); }
    public string PerformanceSummary { get => _performanceSummary; set => Set(ref _performanceSummary, value); }
    public string GnirehtetStatus { get => _gnirehtetStatus; set => Set(ref _gnirehtetStatus, value); }
    public IReadOnlyList<MonitorInfo> Monitors { get => _monitors; set => Set(ref _monitors, value ?? Array.Empty<MonitorInfo>()); }
    public MonitorInfo? SelectedMonitor { get => _selectedMonitor; set => Set(ref _selectedMonitor, value); }
    public OutputProfile? OutputProfile { get => _outputProfile; set { if (Set(ref _outputProfile, value)) OnPropertyChanged(nameof(OutputSummary)); } }
    public string OutputSummary => OutputProfile?.Summary ?? "Sin perfil de salida.";
    public string Bitrate { get => _bitrate; set => Set(ref _bitrate, BitrateService.Normalize(value)); }
    public int TargetFps { get => _targetFps; set => Set(ref _targetFps, Math.Clamp(value, 15, 240)); }
    public int MaxSize { get => _maxSize; set => Set(ref _maxSize, Math.Max(480, value)); }
    public string Theme { get => _theme; set => Set(ref _theme, string.Equals(value, ThemeService.Light, StringComparison.OrdinalIgnoreCase) ? ThemeService.Light : ThemeService.Dark); }

    public IReadOnlyList<SettingOption<int>> ResolutionOptions { get; } = new[] { new SettingOption<int>(1024,"1024"), new(1280,"1280"), new(1600,"1600"), new(1920,"1920"), new(2560,"2560"), new(3200,"3200"), new(3840,"3840"), new(5120,"5120") };
    public IReadOnlyList<SettingOption<int>> FpsOptions { get; } = new[] { new SettingOption<int>(30,"30 FPS"), new(60,"60 FPS"), new(90,"90 FPS"), new(120,"120 FPS"), new(144,"144 FPS"), new(165,"165 FPS"), new(240,"240 FPS") };
    public IReadOnlyList<SettingOption<string>> BitrateOptions { get; } = new[] { new SettingOption<string>("1M","1 Mb/s"), new("2M","2 Mb/s"), new("4M","4 Mb/s"), new("6M","6 Mb/s"), new("8M","8 Mb/s"), new("10M","10 Mb/s"), new("12M","12 Mb/s"), new("16M","16 Mb/s"), new("20M","20 Mb/s"), new("30M","30 Mb/s"), new("40M","40 Mb/s"), new("50M","50 Mb/s") };
    public IReadOnlyList<SettingOption<string>> ThemeOptions { get; } = new[] { new SettingOption<string>(ThemeService.Dark,"Dark"), new(ThemeService.Light,"Light") };

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
