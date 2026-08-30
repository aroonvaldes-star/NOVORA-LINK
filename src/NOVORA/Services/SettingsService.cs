using System.IO;
using System.Text.Json;

namespace NOVORA.Services;

public sealed class NovoraSettings
{
    public bool AudioEnabled { get; set; } = true;
    public string? SelectedMonitorLabel { get; set; }
    public string? SelectedDeviceSerial { get; set; }
    public string Bitrate { get; set; } = "10M";
    public int TargetFps { get; set; } = 60;
    public int MaxSize { get; set; } = 1920;
    public string Theme { get; set; } = ThemeService.Dark;
    public Dictionary<string, string> DeviceNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> MonitorNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SettingsService
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public SettingsService()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NOVORA");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
    }

    public NovoraSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new NovoraSettings();
            var settings = JsonSerializer.Deserialize<NovoraSettings>(File.ReadAllText(_settingsPath), JsonOptions) ?? new NovoraSettings();
            settings.DeviceNames = new Dictionary<string, string>(settings.DeviceNames ?? new(), StringComparer.OrdinalIgnoreCase);
            settings.MonitorNames = new Dictionary<string, string>(settings.MonitorNames ?? new(), StringComparer.OrdinalIgnoreCase);
            return settings;
        }
        catch { return new NovoraSettings(); }
    }

    public void Save(NovoraSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.DeviceNames = new Dictionary<string, string>(settings.DeviceNames ?? new(), StringComparer.OrdinalIgnoreCase);
        settings.MonitorNames = new Dictionary<string, string>(settings.MonitorNames ?? new(), StringComparer.OrdinalIgnoreCase);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public string? GetDeviceName(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial)) return null;
        return Load().DeviceNames.TryGetValue(serial.Trim(), out var name) ? name : null;
    }

    public void SetDeviceName(string serial, string? name)
    {
        if (string.IsNullOrWhiteSpace(serial)) return;
        var settings = Load();
        serial = serial.Trim();
        if (string.IsNullOrWhiteSpace(name)) settings.DeviceNames.Remove(serial); else settings.DeviceNames[serial] = name.Trim();
        Save(settings);
    }

    public string? GetMonitorName(string? monitorLabel)
    {
        if (string.IsNullOrWhiteSpace(monitorLabel)) return null;
        return Load().MonitorNames.TryGetValue(monitorLabel.Trim(), out var name) ? name : null;
    }

    public void SetMonitorName(string monitorLabel, string? name)
    {
        if (string.IsNullOrWhiteSpace(monitorLabel)) return;
        var settings = Load();
        monitorLabel = monitorLabel.Trim();
        if (string.IsNullOrWhiteSpace(name)) settings.MonitorNames.Remove(monitorLabel); else settings.MonitorNames[monitorLabel] = name.Trim();
        Save(settings);
    }
}
