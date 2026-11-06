using System.Text.Json;

namespace NOVORA.Linux.VisionEngine;

internal sealed record LinuxVisionSettings
{
    public string Profile { get; init; } = "Balanced";
    public string Bitrate { get; init; } = "8M";
    public int MaxSize { get; init; } = 1920;
    public int Fps { get; init; } = 60;
    public string Acceleration { get; init; } = "Automatic";
    public string HardwareBackend { get; init; } = "vaapi";
    public string MonitorName { get; init; } = "";
    public string AudioDeviceName { get; init; } = "";
    public uint MonitorId { get; init; }
    public bool Fullscreen { get; init; }
    public uint AudioDeviceId { get; init; }
    public bool AudioEnabled { get; init; } = true;
    public bool RemoteInput { get; init; } = true;
    public bool LowLatency { get; init; } = true;
    public bool VSync { get; init; }
    public string RecordingDirectory { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "NOVORA");
    public string ReceiveDirectory { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "NOVORA");
    public static LinuxVisionSettings Default => new();
    public int BitrateBits => int.Parse(Bitrate[..^1], System.Globalization.CultureInfo.InvariantCulture) * 1_000_000;
    public static string ProfileId(string value) => value switch
    {
        "Automático" => "Automatic", "Competitivo" => "Competitive", "Equilibrado" or "Balanceado" => "Balanced",
        "Calidad" => "Quality", "Fluidez" => "Smooth", _ => value
    };
    public static LinuxVisionSettings ForProfile(string profile)
    {
        string id = ProfileId(profile);
        var settings = Default with { Profile = id };
        settings = id switch
        {
            "Competitive" => settings with { Bitrate = "8M", MaxSize = 1280, Fps = 60, LowLatency = true, VSync = false },
            "Quality" => settings with { Bitrate = "15M", MaxSize = 2160, Fps = 60, LowLatency = false, VSync = true },
            "Smooth" => settings with { Bitrate = "10M", MaxSize = 1920, Fps = 60, VSync = true },
            "Software" => settings with { Acceleration = "Software", MaxSize = 1280, Fps = 30 },
            _ => settings
        };
        settings.Validate();
        return settings;
    }
    public void Validate()
    {
        if (Profile is not ("Automatic" or "Competitive" or "Balanced" or "Quality" or "Smooth" or "Software") ||
            !Enumerable.Range(2, 14).Select(n => $"{n}M").Contains(Bitrate) ||
            MaxSize is not (480 or 720 or 960 or 1080 or 1280 or 1440 or 1600 or 1920 or 2160) ||
            Fps is not (15 or 24 or 30 or 45 or 60) ||
            Acceleration is not ("Automatic" or "Software" or "Forced") ||
            HardwareBackend is not ("vaapi" or "cuda" or "qsv" or "vulkan") ||
            string.IsNullOrWhiteSpace(RecordingDirectory) || !Path.IsPathFullyQualified(RecordingDirectory) ||
            string.IsNullOrWhiteSpace(ReceiveDirectory) || !Path.IsPathFullyQualified(ReceiveDirectory))
            throw new InvalidDataException("Los ajustes de VisionEngine no son válidos.");
    }
}

internal sealed class LinuxVisionSettingsStore
{
    private readonly string _path;
    public LinuxVisionSettingsStore(string? path = null)
    {
        string root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
            ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        _path = path ?? Path.Combine(root, "novora-link-linux", "vision.json");
    }
    public LinuxVisionSettings Load()
    {
        if (!File.Exists(_path)) return LinuxVisionSettings.Default;
        var settings = JsonSerializer.Deserialize<LinuxVisionSettings>(File.ReadAllText(_path))
            ?? throw new InvalidDataException("La configuración de VE está vacía.");
        settings.Validate();
        return settings;
    }
    public void Save(LinuxVisionSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, _path, overwrite: true);
        }
        finally { File.Delete(temp); }
    }
}
