using System.IO;

namespace NOVORA.Services;

public sealed class NovoraPaths
{
    public string BaseDirectory { get; }
    public string ToolsDirectory { get; }

    public string Adb => Path.Combine(ToolsDirectory, "adb.exe");
    public string Scrcpy => Path.Combine(ToolsDirectory, "scrcpy.exe");
    public string ScrcpyServer => Path.Combine(ToolsDirectory, "scrcpy-server");
    public string Gnirehtet => Path.Combine(ToolsDirectory, "gnirehtet.exe");
    public string GnirehtetApk => Path.Combine(ToolsDirectory, "gnirehtet.apk");

    public NovoraPaths(string? baseDirectory = null)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        ToolsDirectory = Path.Combine(BaseDirectory, "Tools");
    }

    public void ValidateRequiredTools()
    {
        var required = new[] { Adb, Scrcpy, ScrcpyServer };
        var missing = required
            .Where(p => !File.Exists(p))
            .Select(Path.GetFileName)
            .ToArray();

        if (missing.Length > 0)
            throw new FileNotFoundException(
                "Faltan herramientas de NOVORA: " + string.Join(", ", missing));
    }

    public void ValidateGnirehtetTools()
    {
        ValidateRequiredTools();

        var required = new[] { Gnirehtet, GnirehtetApk };
        var missing = required
            .Where(p => !File.Exists(p))
            .Select(Path.GetFileName)
            .ToArray();

        if (missing.Length > 0)
            throw new FileNotFoundException(
                "Faltan herramientas de Gnirehtet: " + string.Join(", ", missing));
    }
}
