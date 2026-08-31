using System.IO;

namespace NOVORA.Services;

public sealed class NovoraPaths
{
    public string BaseDirectory { get; }
    public string ToolsDirectory { get; }
    public string Adb => Path.Combine(ToolsDirectory, "adb.exe");
    public string AdbWinApi => Path.Combine(ToolsDirectory, "AdbWinApi.dll");
    public string AdbWinUsbApi => Path.Combine(ToolsDirectory, "AdbWinUsbApi.dll");
    public string Scrcpy => Path.Combine(ToolsDirectory, "scrcpy.exe");
    public string ScrcpyServer => Path.Combine(ToolsDirectory, "scrcpy-server");
    public string Gnirehtet => Path.Combine(ToolsDirectory, "gnirehtet.exe");
    public string GnirehtetApk => Path.Combine(ToolsDirectory, "gnirehtet.apk");

    public NovoraPaths(string? baseDirectory = null)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        ToolsDirectory = Path.Combine(BaseDirectory, "Tools");
    }

    public void ValidateRequiredTools() => ValidateAdbTools();

    public void ValidateAdbTools() => ValidateFiles(
        "ADB no está disponible porque faltan componentes de NOVORA",
        new[] { Adb, AdbWinApi, AdbWinUsbApi },
        defenderHint: true);

    public void ValidateScreenMirroringTools()
    {
        ValidateAdbTools();
        ValidateFiles("Screen Mirroring no está disponible porque faltan componentes de NOVORA", new[] { Scrcpy, ScrcpyServer }, defenderHint: true);
    }

    public void ValidateGnirehtetTools()
    {
        ValidateAdbTools();
        ValidateFiles("Internet USB no está disponible porque faltan herramientas de Gnirehtet", new[] { Gnirehtet, GnirehtetApk }, defenderHint: true);
    }

    private static void ValidateFiles(string prefix, IEnumerable<string> required, bool defenderHint)
    {
        var missing = required.Where(path => !File.Exists(path)).Select(Path.GetFileName).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
        if (missing.Length == 0) return;
        var message = prefix + ": " + string.Join(", ", missing) + ".";
        if (defenderHint)
            message += "\n\nWindows Security o tu antivirus puede haber bloqueado o puesto en cuarentena alguno de estos archivos." +
                       "\n\nAbre Seguridad de Windows > Protección contra virus y amenazas > Historial de protección y restaura/permite el componente sólo si pertenece a tu instalación oficial de NOVORA-LINK." +
                       "\n\nNo es necesario desactivar Microsoft Defender ni excluir toda la carpeta de NOVORA.";
        throw new FileNotFoundException(message);
    }
}
