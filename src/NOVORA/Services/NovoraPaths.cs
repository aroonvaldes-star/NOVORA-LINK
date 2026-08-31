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
        ValidateScreenMirroringTools();
    }

    public void ValidateScreenMirroringTools()
    {
        var required = new[]
        {
            Adb,
            Scrcpy,
            ScrcpyServer
        };

        var missing = required
            .Where(path => !File.Exists(path))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        if (missing.Length == 0)
            return;

        var missingList = string.Join(", ", missing);

        throw new FileNotFoundException(
            "Screen Mirroring no está disponible porque faltan componentes de NOVORA: " +
            missingList + ".\n\n" +
            "Windows Security o tu antivirus puede haber bloqueado o puesto en cuarentena alguno de estos archivos.\n\n" +
            "Abre Seguridad de Windows > Protección contra virus y amenazas > Historial de protección, " +
            "comprueba si el componente pertenece a NOVORA-LINK y restáuralo/permítelo únicamente si coincide con tu instalación oficial.\n\n" +
            "No es necesario desactivar Microsoft Defender ni excluir toda la carpeta de NOVORA.\n\n" +
            "Después vuelve a NOVORA y presiona PLAY otra vez.");
    }

    public void ValidateGnirehtetTools()
    {
        ValidateRequiredTools();

        var required = new[]
        {
            Gnirehtet,
            GnirehtetApk
        };

        var missing = required
            .Where(path => !File.Exists(path))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new FileNotFoundException(
                "Internet USB no está disponible porque faltan herramientas de Gnirehtet: " +
                string.Join(", ", missing) + ".\n\n" +
                "Windows Security o tu antivirus puede haber bloqueado o puesto en cuarentena alguno de estos archivos. " +
                "Revisa el Historial de protección antes de volver a intentarlo.");
        }
    }
}
