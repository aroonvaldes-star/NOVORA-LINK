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
    public string RecordingDirectory { get; init; } = Path.Combine(UserFolder(Environment.SpecialFolder.MyVideos, "Videos"), "NOVORA");
    public string ReceiveDirectory { get; init; } = Path.Combine(UserFolder(Environment.SpecialFolder.UserProfile, ""), "Downloads", "NOVORA");

    /// <summary>
    /// GetFolderPath returns "" on Linux when the folder does not exist yet (fresh profile, CI runner),
    /// which made every default setting invalid. Never verify existence; fall back under $HOME.
    /// </summary>
    private static string UserFolder(Environment.SpecialFolder folder, string homeFallback)
    {
        string path = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
        if (!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)) return path;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(home) || !Path.IsPathFullyQualified(home)) home = Path.GetTempPath();
        return homeFallback.Length == 0 ? home : Path.Combine(home, homeFallback);
    }
    public static LinuxVisionSettings Default => new();
    public int BitrateBits => ParseBitrate(Bitrate);
    /// <summary>Mobile-data profiles (values match NLControlMobileData: 120 kbit/s·5 fps·480 px and 250 kbit/s·30 fps·720 px).</summary>
    public bool IsMobile => IsMobileProfile(Profile);
    public static bool IsMobileProfile(string profile) => profile is "MobileExtreme" or "MobileEconomy";
    public static readonly string[] MobileBitrates = ["120K", "250K"];
    public static readonly string[] LegacyBitrates = Enumerable.Range(2, 14).Select(n => $"{n}M").ToArray();
    public static readonly string[] AllBitrates = [.. MobileBitrates, .. LegacyBitrates];
    public static readonly int[] MobileFrameRates = [5, 10, 15, 24, 30];
    public static readonly int[] LegacyFrameRates = [15, 24, 30, 45, 60];
    public static readonly int[] AllFrameRates = [5, 10, 15, 24, 30, 45, 60];
    /// <summary>"120K" → 120000 bit/s, "8M" → 8000000 bit/s.</summary>
    public static int ParseBitrate(string bitrate)
    {
        if (string.IsNullOrEmpty(bitrate) || bitrate.Length < 2)
            throw new InvalidDataException("Bitrate no válido.");
        int value = int.Parse(bitrate[..^1], System.Globalization.CultureInfo.InvariantCulture);
        return bitrate[^1] switch
        {
            'K' => checked(value * 1_000),
            'M' => checked(value * 1_000_000),
            _ => throw new InvalidDataException("Bitrate no válido.")
        };
    }
    public static string ProfileId(string value) => value switch
    {
        "Automático" => "Automatic", "Competitivo" => "Competitive", "Equilibrado" or "Balanceado" => "Balanced",
        "Calidad" => "Quality", "Fluidez" => "Smooth",
        "Móvil extremo" => "MobileExtreme", "Móvil ahorro" => "MobileEconomy", _ => value
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
            // Mobile data: audio off; Economy at 30 fps by user request (same bitrate, smoother motion).
            "MobileExtreme" => settings with { Bitrate = "120K", MaxSize = 480, Fps = 5, AudioEnabled = false, LowLatency = true },
            "MobileEconomy" => settings with { Bitrate = "250K", MaxSize = 720, Fps = 30, AudioEnabled = false, LowLatency = true },
            _ => settings
        };
        settings.Validate();
        return settings;
    }
    public void Validate()
    {
        bool mobile = IsMobile;
        if (Profile is not ("Automatic" or "Competitive" or "Balanced" or "Quality" or "Smooth" or "Software" or
                "MobileExtreme" or "MobileEconomy") ||
            !(mobile ? MobileBitrates : LegacyBitrates).Contains(Bitrate) ||
            (mobile ? MaxSize is not (480 or 720) :
                MaxSize is not (480 or 720 or 960 or 1080 or 1280 or 1440 or 1600 or 1920 or 2160)) ||
            !(mobile ? MobileFrameRates : LegacyFrameRates).Contains(Fps) ||
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
