using System.Text.Json;

namespace NOVORA.Linux.ExIn;

/// <summary>
/// The three ExIn values Windows keeps in NLServiceNovoraSettings (ExInEnabled,
/// ExInPointerSensitivity, ExInGameStickSensitivity), stored next to the Linux trust store.
/// </summary>
<<<<<<< Updated upstream
internal sealed record LinuxExInSettings(bool Enabled = true, int PointerSensitivity = 20, int GameStickSensitivity = 100)
=======
internal sealed record LinuxExInSettings(bool Enabled = true, int PointerSensitivity = 20, int GameStickSensitivity = 85)
>>>>>>> Stashed changes
{
    public static LinuxExInSettings Default { get; } = new();

    public static string FilePath
    {
        get
        {
            string? configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            string root = !string.IsNullOrWhiteSpace(configHome) && Path.IsPathFullyQualified(configHome)
                ? configHome
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(root, "novora-link-linux", "exin.json");
        }
    }

    public static LinuxExInSettings Load(string? path = null)
    {
        path ??= FilePath;
        if (!File.Exists(path)) return Default;
        LinuxExInSettings settings = JsonSerializer.Deserialize<LinuxExInSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"La configuración ExIn está vacía: {path}");
        return settings with
        {
            PointerSensitivity = Math.Clamp(settings.PointerSensitivity, 5, 40),
            GameStickSensitivity = Math.Clamp(settings.GameStickSensitivity, 25, 150)
        };
    }

    public void Save(string? path = null)
    {
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}
