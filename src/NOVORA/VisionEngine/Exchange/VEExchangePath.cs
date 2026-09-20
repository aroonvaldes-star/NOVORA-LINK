using System;
using System.IO;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Rutas oficiales utilizadas por VisionEngine
/// para intercambio de archivos PC ↔ Android.
///
/// Raíz oficial:
///
///     /sdcard/NOVORA
/// </summary>
internal static class VEExchangePath
{
    public const string RootAndroidVE =
        "/sdcard/NOVORA";

    public const string DocumentsFolderVE =
        "Documentos";

    public const string ImagesFolderVE =
        "Imagenes";

    public const string VideosFolderVE =
        "Videos";

    public const string AppsFolderVE =
        "Instaladores";

    public const string FilesFolderVE =
        "Otros";

    private static readonly HashSet<string> ImageExtensionsVE =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".gif",
            ".webp",
            ".bmp",
            ".heic",
            ".heif"
        };

    private static readonly HashSet<string> VideoExtensionsVE =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4",
            ".m4v",
            ".mov",
            ".mkv",
            ".webm",
            ".avi",
            ".3gp"
        };

    private static readonly HashSet<string> DocumentExtensionsVE =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf",
            ".txt",
            ".doc",
            ".docx",
            ".xls",
            ".xlsx",
            ".ppt",
            ".pptx",
            ".csv",
            ".json",
            ".xml",
            ".zip",
            ".rar",
            ".7z"
        };

    // ============================================================
    // ROOT
    // ============================================================

    public static string GetRootAndroidVE()
    {
        return RootAndroidVE;
    }

    // ============================================================
    // DESTINATION
    // ============================================================

    /// <summary>
    /// Construye el destino Android partiendo
    /// de una ruta local Windows.
    ///
    /// Ejemplo:
    ///
    /// C:\Videos\video.mp4
    ///
    /// ->
    ///
    /// /sdcard/NOVORA/video.mp4
    /// </summary>
    public static string BuildDestinationVE(
        string localPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            localPath);

        string safeName =
            GetSafeNameVE(
                localPath);

        return
            $"{RootAndroidVE}/{FilesFolderVE}/{safeName}";
    }

    /// <summary>
    /// Compatibilidad con VEExchangeFile existente.
    ///
    /// Aunque conserva el nombre antiguo "Download",
    /// ahora el destino oficial es:
    ///
    ///     /sdcard/NOVORA
    /// </summary>
    public static string BuildAndroidDownloadPathVE(
        string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            fileName);

        return
            BuildCategorizedDestinationFromNameVE(
                fileName);
    }

    public static string BuildDestinationFromNameVE(
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            name);

        string safeName =
            SanitizeNameVE(
                name);

        return
            $"{RootAndroidVE}/{FilesFolderVE}/{safeName}";
    }

    public static string BuildCategorizedDestinationFromNameVE(
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            name);

        string safeName =
            SanitizeNameVE(
                name);

        string folder =
            GetCategoryFolderVE(
                safeName);

        return
            BuildChildPathVE(
                folder,
                safeName);
    }

    public static string GetCategoryFolderVE(
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            name);

        return NOVORA.Control.NLControlFileStorage.Category(name);
    }

    public static IReadOnlyList<string> GetStandardDirectoriesVE()
        =>
        [
            RootAndroidVE,
            BuildChildPathVE(DocumentsFolderVE),
            BuildChildPathVE(ImagesFolderVE),
            BuildChildPathVE(VideosFolderVE),
            BuildChildPathVE(AppsFolderVE),
            BuildChildPathVE(FilesFolderVE),
            BuildChildPathVE("Audio"),
            BuildChildPathVE("Comprimidos")
        ];

    // ============================================================
    // CHILD PATH
    // ============================================================

    public static string BuildChildPathVE(
        params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(
            parts);

        string result =
            RootAndroidVE;

        foreach (string part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            string safePart =
                SanitizeNameVE(
                    part);

            result =
                result +
                "/" +
                safePart;
        }

        return result;
    }

    // ============================================================
    // SHELL COMMANDS
    // ============================================================

    public static string BuildEnsureRootCommandVE()
    {
        string directories =
            string.Join(
                " ",
                GetStandardDirectoriesVE()
                    .Select(
                        QuoteShellArgumentVE));

        return
            $"mkdir -p {directories}";
    }

    public static string BuildEnsureDirectoryCommandVE()
    {
        return
            BuildEnsureRootCommandVE();
    }

    public static string BuildEnsureDirectoryCommandVE(
        string androidDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            androidDirectory);

        string normalized =
            NormalizeAndroidPathVE(
                androidDirectory);

        if (!IsInsideNovoraRootVE(normalized))
        {
            throw new InvalidOperationException(
                "La carpeta debe pertenecer a /sdcard/NOVORA.");
        }

        string quoted =
            QuoteShellArgumentVE(
                normalized);

        return
            $"mkdir -p {quoted}";
    }

    public static string BuildDirectoryExistsCommandVE()
    {
        string root =
            QuoteShellArgumentVE(
                RootAndroidVE);

        return
            $"test -d {root} && echo NOVORA_DIRECTORY_OK";
    }

    public static string BuildFileExistsCommandVE(
        string androidFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            androidFilePath);

        string normalized =
            NormalizeAndroidPathVE(
                androidFilePath);

        string quoted =
            QuoteShellArgumentVE(
                normalized);

        return
            $"test -f {quoted} && echo NOVORA_FILE_OK";
    }

    // ============================================================
    // VALIDATION
    // ============================================================

    public static bool IsInsideNovoraRootVE(
        string androidPath)
    {
        if (string.IsNullOrWhiteSpace(androidPath))
        {
            return false;
        }

        string normalized =
            NormalizeAndroidPathVE(
                androidPath);

        if (
            normalized.Equals(
                RootAndroidVE,
                StringComparison.Ordinal))
        {
            return true;
        }

        return
            normalized.StartsWith(
                RootAndroidVE + "/",
                StringComparison.Ordinal);
    }

    // ============================================================
    // FILE NAME
    // ============================================================

    public static string GetFileNameVE(
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            path);

        string normalized =
            path.Replace(
                '\\',
                '/');

        int index =
            normalized.LastIndexOf('/');

        if (
            index < 0 ||
            index >= normalized.Length - 1)
        {
            return normalized;
        }

        return
            normalized[
                (index + 1)..];
    }

    public static string GetSafeNameVE(
        string localPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            localPath);

        string fullPath =
            Path.GetFullPath(
                localPath);

        string? name;

        if (Directory.Exists(fullPath))
        {
            DirectoryInfo directory =
                new(
                    fullPath);

            name =
                directory.Name;
        }
        else
        {
            name =
                Path.GetFileName(
                    fullPath);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                "No se pudo obtener el nombre del archivo o carpeta.");
        }

        return
            SanitizeNameVE(
                name);
    }

    // ============================================================
    // NORMALIZE
    // ============================================================

    public static string NormalizeAndroidPathVE(
        string androidPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            androidPath);

        string normalized =
            androidPath
                .Replace(
                    '\\',
                    '/')
                .Trim();

        while (
            normalized.Contains(
                "//",
                StringComparison.Ordinal))
        {
            normalized =
                normalized.Replace(
                    "//",
                    "/",
                    StringComparison.Ordinal);
        }

        /*
         * Aquí usamos EndsWith(char), que es exactamente
         * lo que recomienda el analizador.
         */
        while (
            normalized.Length > 1 &&
            normalized.EndsWith('/'))
        {
            normalized =
                normalized[..^1];
        }

        return normalized;
    }

    // ============================================================
    // SANITIZE
    // ============================================================

    public static string SanitizeNameVE(
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            name);

        char[] invalidCharacters =
        [
            '/',
            '\\',
            '\0',
            ':',
            '*',
            '?',
            '"',
            '<',
            '>',
            '|'
        ];

        string safeName =
            name.Trim();

        foreach (
            char invalidCharacter
            in invalidCharacters)
        {
            safeName =
                safeName.Replace(
                    invalidCharacter,
                    '_');
        }

        while (
            safeName.Contains(
                "__",
                StringComparison.Ordinal))
        {
            safeName =
                safeName.Replace(
                    "__",
                    "_",
                    StringComparison.Ordinal);
        }

        safeName =
            safeName.Trim(
                ' ',
                '.');

        if (string.IsNullOrWhiteSpace(safeName))
        {
            return
                "NOVORA_ITEM";
        }

        return safeName;
    }

    // ============================================================
    // SHELL QUOTE
    // ============================================================

    public static string QuoteShellArgumentVE(
        string value)
    {
        ArgumentNullException.ThrowIfNull(
            value);

        string escaped =
            value.Replace(
                "'",
                "'\\''",
                StringComparison.Ordinal);

        return
            $"'{escaped}'";
    }
}
