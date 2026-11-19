using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NOVORA.VisionEngine.Performance;

namespace NOVORA.Service;

public sealed class NLServiceNovoraSettings
{
    public bool IntegrationClipboardEnabled { get; set; } = true;
    public bool IntegrationFileTransferEnabled { get; set; } = true;
    public bool IntegrationDragDropEnabled { get; set; } = true;
    public bool IntegrationApplicationsEnabled { get; set; } = true;
    public bool IntegrationNotificationsEnabled { get; set; } = true;
    public bool IntegrationDynamicResizeEnabled { get; set; } = true;
    public bool ExInEnabled { get; set; } = true;
    public bool PerformanceSessionEnabled { get; set; }
    public int ExInPointerSensitivity { get; set; } = 20;
    public int ExInGameStickSensitivity { get; set; } = 100;
    [System.Obsolete("Compatibilidad de configuración; usa ExInEnabled.")]
    public bool GamepadEnabled { get => ExInEnabled; set => ExInEnabled = value; }
    public string VisionProfile { get; set; } = "Automatic";

    [JsonPropertyName("NvidiaProfile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyNvidiaProfile
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                VisionProfile = VEProfileOptions.FromLegacyNvidiaVE(value).ToString();
        }
    }

    [JsonIgnore]
    [Obsolete("Usa VisionProfile.")]
    public string NvidiaProfile
    {
        get => VisionProfile;
        set => VisionProfile = VEProfileOptions.FromLegacyNvidiaVE(value).ToString();
    }

    // ============================================================
    // AUDIO
    // ============================================================

    public bool AudioEnabled { get; set; } = true;

    // ============================================================
    // MONITOR SELECCIONADO
    // ============================================================

    public string? SelectedMonitorLabel { get; set; }

    public string? SelectedMonitorDeviceName { get; set; }

    // ============================================================
    // DISPOSITIVO SELECCIONADO
    // ============================================================

    public string? SelectedDeviceSerial { get; set; }

    // ============================================================
    // VIDEO
    // ============================================================

    public string Bitrate { get; set; } = "8M";

    public int TargetFps { get; set; } = 45;

    public int MaxSize { get; set; } = 1280;

    public string VideoPresentationMode { get; set; } = "Window";

    public string SelectedAudioOutput { get; set; } = "__default__";

    // ============================================================
    // APARIENCIA
    // ============================================================

    public string Theme { get; set; } = NLServiceTheme.Light;

    // ============================================================
    // INICIO / CICLO DE VIDA
    // ============================================================

    public bool StartWithWindows { get; set; }

    public bool AutoStartSuppressed { get; set; }

    // ============================================================
    // NOMBRES PERSONALIZADOS
    // ============================================================

    public Dictionary<string, string> DeviceNames { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> MonitorNames { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

}

public sealed class NLServiceSettings
{
    private const long MaxSettingsFileBytes = 1024 * 1024;
    private readonly string _settingsPath;
    private bool _lastLoadFailed;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    // ============================================================
    // CONSTRUCTOR
    // ============================================================

    public NLServiceSettings()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "NOVORA");

        Directory.CreateDirectory(folder);

        _settingsPath = Path.Combine(
            folder,
            "settings.json");
    }

    // ============================================================
    // CARGAR
    // ============================================================

    public NLServiceNovoraSettings Load()
    {
        string backupPath = _settingsPath + ".bak";
        if (!File.Exists(_settingsPath))
        {
            _lastLoadFailed = false;
            return TryReadSettingsFile(backupPath, out var recovered)
                ? recovered!
                : new NLServiceNovoraSettings();
        }

        if (TryReadSettingsFile(_settingsPath, out var settings))
        {
            _lastLoadFailed = false;
            return settings!;
        }

        _lastLoadFailed = true;
        System.Diagnostics.Debug.WriteLine($"NLServiceSettings: error leyendo '{_settingsPath}'; preservando archivo y buscando respaldo.");
        TryCopyBestEffort(_settingsPath, _settingsPath + ".corrupt");

        if (TryReadSettingsFile(backupPath, out var backup))
        {
            _lastLoadFailed = false;
            return backup!;
        }

        return new NLServiceNovoraSettings();
    }

    private static bool TryReadSettingsFile(string path, out NLServiceNovoraSettings? settings)
    {
        settings = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxSettingsFileBytes)
                return false;

            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<NLServiceNovoraSettings>(json, JsonOptions);
            if (parsed is null)
                return false;

            parsed.DeviceNames =
                new Dictionary<string, string>(
                    parsed.DeviceNames ?? new(),
                    StringComparer.OrdinalIgnoreCase);

            parsed.MonitorNames =
                new Dictionary<string, string>(
                    parsed.MonitorNames ?? new(),
                    StringComparer.OrdinalIgnoreCase);

            ApplyLowJitterDefaults(parsed);
            settings = parsed;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NLServiceSettings read error ({path}): {ex.Message}");
            return false;
        }
    }

    private static void TryCopyBestEffort(string sourcePath, string destinationPath)
    {
        try
        {
            if (File.Exists(sourcePath))
                File.Copy(sourcePath, destinationPath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NLServiceSettings backup error ({destinationPath}): {ex.Message}");
        }
    }

    // ============================================================
    // GUARDAR
    // ============================================================

    public void Save(NLServiceNovoraSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.DeviceNames =
            new Dictionary<string, string>(
                settings.DeviceNames ?? new(),
                StringComparer.OrdinalIgnoreCase);

        settings.MonitorNames =
            new Dictionary<string, string>(
                settings.MonitorNames ?? new(),
                StringComparer.OrdinalIgnoreCase);

        ApplyLowJitterDefaults(settings);

        var json =
            JsonSerializer.Serialize(
                settings,
                JsonOptions);

        string temporaryPath = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(_settingsPath))
            {
                TryCopyBestEffort(
                    _settingsPath,
                    _lastLoadFailed ? _settingsPath + ".corrupt" : _settingsPath + ".bak");
            }
            File.Move(temporaryPath, _settingsPath, overwrite: true);
            _lastLoadFailed = false;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    // ============================================================
    // NOMBRE PERSONALIZADO DEL DISPOSITIVO
    // ============================================================

    public string? GetDeviceName(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return null;

        var settings = Load();

        return settings.DeviceNames.TryGetValue(
            serial.Trim(),
            out var name)
            ? name
            : null;
    }

    public void SetDeviceName(
        string serial,
        string? name)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return;

        var settings = Load();

        serial = serial.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            settings.DeviceNames.Remove(serial);
        }
        else
        {
            settings.DeviceNames[serial] =
                name.Trim();
        }

        Save(settings);
    }

    // ============================================================
    // NOMBRE PERSONALIZADO DEL MONITOR
    // ============================================================

    public string? GetMonitorName(string? monitorLabel)
    {
        if (string.IsNullOrWhiteSpace(monitorLabel))
            return null;

        var settings = Load();

        return settings.MonitorNames.TryGetValue(
            monitorLabel.Trim(),
            out var name)
            ? name
            : null;
    }

    public void SetMonitorName(
        string monitorLabel,
        string? name)
    {
        if (string.IsNullOrWhiteSpace(monitorLabel))
            return;

        var settings = Load();

        monitorLabel = monitorLabel.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            settings.MonitorNames.Remove(
                monitorLabel);
        }
        else
        {
            settings.MonitorNames[monitorLabel] =
                name.Trim();
        }

        Save(settings);
    }

    private static void ApplyLowJitterDefaults(
        NLServiceNovoraSettings settings)
    {
        settings.Bitrate =
            NormalizeLowJitterBitrate(
                settings.Bitrate);

        settings.TargetFps =
            Math.Clamp(
                settings.TargetFps,
                15,
                240);

        settings.MaxSize =
            Math.Clamp(
                settings.MaxSize,
                480,
                4320);
    }

    private static string NormalizeLowJitterBitrate(
        string? bitrate)
    {
        string normalized =
            NLServiceBitrate.Normalize(
                bitrate);

        string number =
            normalized.EndsWith(
                "M",
                StringComparison.OrdinalIgnoreCase)
                ? normalized[..^1]
                : normalized;

        if (!double.TryParse(
                number,
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture,
                out double mbps))
        {
            return "8M";
        }

        mbps =
            Math.Clamp(
                mbps,
                2d,
                15d);

        return
            mbps.ToString(
                "0.###",
                System.Globalization.CultureInfo.InvariantCulture) +
            "M";
    }
}
