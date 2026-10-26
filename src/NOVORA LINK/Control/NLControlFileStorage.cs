using System.IO;

namespace NOVORA.Control;

/// <summary>One public destination by content type, independent of transfer feature.</summary>
public static class NLControlFileStorage
{
    public static string PcRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NOVORA-Files");
    public static string Category(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".heic" or ".heif" or ".avif" or ".tiff" => "Imagenes",
        ".mp4" or ".m4v" or ".mov" or ".mkv" or ".webm" or ".avi" or ".3gp" => "Videos",
        ".mp3" or ".wav" or ".m4a" or ".aac" or ".ogg" or ".opus" or ".flac" or ".wma" => "Audio",
        ".pdf" or ".txt" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".csv" or ".json" or ".xml" or ".md" or ".odt" or ".ods" => "Documentos",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" => "Comprimidos",
        ".apk" or ".apks" or ".aab" or ".msi" or ".msix" or ".msixbundle" => "Instaladores",
        _ => "Otros"
    };

    public static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 180 || name is "." or ".." ||
            name.Any(c => char.IsControl(c) || "/\\:*?\"<>|".Contains(c)) || name.EndsWith('.') || name.EndsWith(' '))
            throw new InvalidDataException("Nombre de archivo inválido.");
        string stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Nombre reservado por Windows.");
        return name;
    }

    public static string Commit(string temporary, string root, string name)
    {
        SafeName(name);
        string directory = Path.Combine(root, Category(name));
        Directory.CreateDirectory(directory);
        // Never follow a substituted public category into another directory.
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("La carpeta NOVORA no puede ser un enlace.");
        for (int i = 0; i < 10000; i++)
        {
            string candidate = Path.Combine(directory, i == 0 ? name : $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
            try { File.Move(temporary, candidate, false); return candidate; }
            catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { }
        }
        throw new IOException("No se pudo reservar un nombre disponible.");
    }

    /// <summary>Import a completed ADB snapshot. Known category containers are flattened only one level;</summary>
    /// <remarks>user directories retain their internal structure under Otros.</remarks>
    public static int ImportDirectory(string source, string root)
    {
        string[] categories = ["Imagenes", "Videos", "Audio", "Documentos", "Comprimidos", "Instaladores", "Otros", "Img", "Doc", "Apps", "Files"];
        int count = 0;
        void ImportItem(string item)
        {
            if (Path.GetFileName(item).StartsWith(".novora-", StringComparison.OrdinalIgnoreCase)) return;
            if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) throw new IOException("No se importan enlaces de archivos.");
            if (File.Exists(item)) { Commit(item, root, SafeName(Path.GetFileName(item))); count++; return; }
            // Keep a transferred folder intact, never scatter its contents across categories.
            if (Directory.EnumerateFileSystemEntries(item, "*", SearchOption.AllDirectories).Any(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0))
                throw new IOException("La carpeta recibida contiene enlaces.");
            string parent = Path.Combine(root, "Otros"); Directory.CreateDirectory(parent);
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("La carpeta NOVORA no puede ser un enlace.");
            string name = SafeName(Path.GetFileName(item));
            string destination = Path.Combine(parent, name + "-" + Guid.NewGuid().ToString("N"));
            Directory.Move(item, destination); count++;
        }
        foreach (string item in Directory.EnumerateFileSystemEntries(source).ToArray())
        {
            if (Directory.Exists(item) && categories.Contains(Path.GetFileName(item), StringComparer.OrdinalIgnoreCase))
                foreach (string child in Directory.EnumerateFileSystemEntries(item).ToArray()) ImportItem(child);
            else ImportItem(item);
        }
        return count;
    }
}
