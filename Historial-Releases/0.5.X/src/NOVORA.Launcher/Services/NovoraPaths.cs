using System;
using System.IO;
using System.Linq;

namespace NOVORA.Launcher.Services;

public sealed class NovoraPaths
{
    public string BaseDirectory { get; }
    public string ToolsDirectory { get; }
    public string ToolBundleDirectory { get; }
    public string Adb => Path.Combine(ToolBundleDirectory, "adb.exe");
    public string Scrcpy => Path.Combine(ToolBundleDirectory, "scrcpy.exe");
    public string Gnirehtet => Path.Combine(ToolBundleDirectory, "gnirehtet.exe");
    public string GnirehtetApk => Path.Combine(ToolBundleDirectory, "gnirehtet.apk");
    public string ScrcpyServer => Path.Combine(ToolBundleDirectory, "scrcpy-server");

    public NovoraPaths(string? baseDirectory = null)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        ToolsDirectory = Path.Combine(BaseDirectory, "Tools");
        ToolBundleDirectory = Path.Combine(ToolsDirectory, "Runtime");
    }

    public void ValidateRequiredTools()
    {
        var required = new[] { Adb, Scrcpy, ScrcpyServer };
        var missing = required.Where(p => !File.Exists(p)).Select(Path.GetFileName).ToArray();
        if (missing.Length > 0)
            throw new FileNotFoundException("Faltan herramientas de NOVORA: " + string.Join(", ", missing));
    }

    public void ValidateGnirehtetTools()
    {
        ValidateRequiredTools();
        var required = new[] { Gnirehtet, GnirehtetApk };
        var missing = required.Where(p => !File.Exists(p)).Select(Path.GetFileName).ToArray();
        if (missing.Length > 0)
            throw new FileNotFoundException("Faltan herramientas de Gnirehtet: " + string.Join(", ", missing));
    }
}
