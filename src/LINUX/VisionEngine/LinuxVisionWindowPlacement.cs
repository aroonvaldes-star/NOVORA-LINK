using System.Text.Json;

namespace NOVORA.Linux.VisionEngine;

/// <summary>
/// Last screen position of the VisionEngine window, so the next session opens where the user left it.
/// Stored apart from vision.json because it changes on every drag and must never invalidate settings.
/// </summary>
internal sealed record LinuxVisionWindowPlacement(int X, int Y)
{
    private const int MaxCoordinate = 100_000;

    public static string FilePath
    {
        get
        {
            string root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(root, "novora-link-linux", "vision-window.json");
        }
    }

    /// <summary>Returns the saved position, or null when none exists or the file is unreadable.</summary>
    public static LinuxVisionWindowPlacement? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var placement = JsonSerializer.Deserialize<LinuxVisionWindowPlacement>(File.ReadAllText(FilePath));
            if (placement is null || Math.Abs(placement.X) > MaxCoordinate || Math.Abs(placement.Y) > MaxCoordinate)
                return null;
            return placement;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public static void Save(int x, int y)
    {
        if (Math.Abs(x) > MaxCoordinate || Math.Abs(y) > MaxCoordinate) return;
        string path = FilePath;
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(new LinuxVisionWindowPlacement(x, y)));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A missing saved position only means the window opens at the compositor's default place.
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }
}
