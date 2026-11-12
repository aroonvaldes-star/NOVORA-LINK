using System.IO;

namespace NOVORA.Linux.Files;

/// <summary>One public destination by content type, independent of transfer feature.</summary>
public static class NLControlFileStorage
{
    public static string PcRoot => NOVORA.Linux.VisionEngine.LinuxVisionSettings.DefaultReceiveDirectory;
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

}
