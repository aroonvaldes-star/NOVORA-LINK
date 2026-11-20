using System.ComponentModel;
using System.Runtime.CompilerServices;
using NOVORA.Launcher.Models;
using System.Collections.Generic;

namespace NOVORA.Launcher.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private bool _audioEnabled = true;
    public bool AudioEnabled
    {
        get => _audioEnabled;
        set
        {
            if (_audioEnabled == value) return;
            _audioEnabled = value;
            OnPropertyChanged();
        }
    }

    private IReadOnlyList<DeviceInfo> _devices = Array.Empty<DeviceInfo>();
    private DeviceInfo _device = new();
    private string _connectionStatus = "Sin comprobar";
    private MonitorInfo? _selectedMonitor;
    private string _performanceSummary = "Esperando dispositivo...";
    private string _gnirehtetStatus = "No iniciado";
    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();
    private OutputProfile? _outputProfile;

    public IReadOnlyList<DeviceInfo> Devices { get => _devices; set { _devices = value; OnPropertyChanged(); } }
    public DeviceInfo Device { get => _device; set { _device = value; OnPropertyChanged(); } }
    public string ConnectionStatus { get => _connectionStatus; set { _connectionStatus = value; OnPropertyChanged(); } }
    public MonitorInfo? SelectedMonitor { get => _selectedMonitor; set { _selectedMonitor = value; OnPropertyChanged(); } }
    public string PerformanceSummary { get => _performanceSummary; set { _performanceSummary = value; OnPropertyChanged(); } }
    public string GnirehtetStatus { get => _gnirehtetStatus; set { _gnirehtetStatus = value; OnPropertyChanged(); } }
    public IReadOnlyList<MonitorInfo> Monitors { get => _monitors; set { _monitors = value; OnPropertyChanged(); } }
    public OutputProfile? OutputProfile { get => _outputProfile; set { _outputProfile = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
