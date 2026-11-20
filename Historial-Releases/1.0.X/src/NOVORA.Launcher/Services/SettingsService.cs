using System.Text.Json;
using System.IO;

namespace NOVORA.Launcher.Services;

public sealed class NovoraSettings
{
    public bool AudioEnabled { get; set; } = true;
    public string? SelectedMonitorLabel { get; set; }
    public string? SelectedDeviceSerial { get; set; }
}

public sealed class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NOVORA");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
    }

    public NovoraSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new NovoraSettings();

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<NovoraSettings>(json) ?? new NovoraSettings();
        }
        catch
        {
            return new NovoraSettings();
        }
    }

    public void Save(NovoraSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_settingsPath, json);
    }
}
