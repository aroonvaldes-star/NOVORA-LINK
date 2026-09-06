namespace NOVORA.VisionEngine.Exchange;

public static class PathExchangeVE
{
    public const string AndroidDownloadRootVE = "/sdcard/Download";

    public static string BuildAndroidDownloadPathVE(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("El nombre del archivo no puede estar vacío.", nameof(fileName));
        string trimmed = fileName.Trim();
        if (trimmed is "." or ".." || trimmed.Contains("..", StringComparison.Ordinal) ||
            trimmed.Contains('/') || trimmed.Contains('\\') || Path.GetFileName(trimmed) != trimmed)
            throw new ArgumentException("VisionEngine requiere únicamente un nombre de archivo seguro, no una ruta.", nameof(fileName));
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("El nombre de archivo contiene caracteres inválidos.", nameof(fileName));
        return $"{AndroidDownloadRootVE}/{trimmed}";
    }
}
