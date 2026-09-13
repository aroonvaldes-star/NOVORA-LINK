using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NOVORA.Services;

public sealed class NovoraSettings
{
    public bool PrivacyShieldEnabled { get; set; }
    public bool IntegrationClipboardEnabled { get; set; } = true;
    public bool IntegrationFileTransferEnabled { get; set; } = true;
    public bool IntegrationDragDropEnabled { get; set; } = true;
    public bool IntegrationApplicationsEnabled { get; set; } = true;
    public bool IntegrationNotificationsEnabled { get; set; } = true;
    public bool IntegrationDynamicResizeEnabled { get; set; } = true;
    public bool GamepadEnabled { get; set; } = true;
    public bool RemoteAndroidEnabled { get; set; } = true;
    public string NvidiaProfile { get; set; } = "Automatic";

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

    public string Bitrate { get; set; } = "4M";

    public int TargetFps { get; set; } = 45;

    public int MaxSize { get; set; } = 1280;

    public string VideoPresentationMode { get; set; } = "Window";

    public string SelectedAudioOutput { get; set; } = "__default__";

    // ============================================================
    // APARIENCIA
    // ============================================================

    public string Theme { get; set; } = ThemeService.Dark;

    // ============================================================
    // INICIO / CICLO DE VIDA
    // ============================================================

    public bool StartWithWindows { get; set; } = true;

    public bool AutoStartSuppressed { get; set; }

    // ============================================================
    // NOMBRES PERSONALIZADOS
    // ============================================================

    public Dictionary<string, string> DeviceNames { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> MonitorNames { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SettingsService
{
    private readonly string _settingsPath;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    // ============================================================
    // CONSTRUCTOR
    // ============================================================

    public SettingsService()
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

    public NovoraSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new NovoraSettings();

            var json = File.ReadAllText(_settingsPath);

            var settings =
                JsonSerializer.Deserialize<NovoraSettings>(
                    json,
                    JsonOptions)
                ?? new NovoraSettings();

            settings.DeviceNames =
                new Dictionary<string, string>(
                    settings.DeviceNames ?? new(),
                    StringComparer.OrdinalIgnoreCase);

            settings.MonitorNames =
                new Dictionary<string, string>(
                    settings.MonitorNames ?? new(),
                    StringComparer.OrdinalIgnoreCase);

            ApplyLowJitterDefaults(settings);

            return settings;
        }
        catch
        {
            return new NovoraSettings();
        }
    }

    // ============================================================
    // GUARDAR
    // ============================================================

    public void Save(NovoraSettings settings)
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

        File.WriteAllText(
            _settingsPath,
            json);
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
        NovoraSettings settings)
    {
        settings.Bitrate =
            NormalizeLowJitterBitrate(
                settings.Bitrate);

        settings.TargetFps =
            Math.Clamp(
                settings.TargetFps,
                15,
                45);

        settings.MaxSize =
            Math.Clamp(
                settings.MaxSize,
                1,
                1280);
    }

    private static string NormalizeLowJitterBitrate(
        string? bitrate)
    {
        string normalized =
            BitrateService.Normalize(
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
            return "4M";
        }

        mbps =
            Math.Clamp(
                mbps,
                1d,
                4d);

        return
            mbps.ToString(
                "0.###",
                System.Globalization.CultureInfo.InvariantCulture) +
            "M";
    }
}
