using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NOVORA.Services;

/// <summary>
/// Información de una actualización de NOVORA.
/// </summary>
public sealed record NovoraUpdateInfo(
    Version Version,
    string TagName,
    string ReleaseName,
    string AssetName,
    string ExpectedSha256,
    Uri DownloadUri,
    string ReleaseUrl,
    string ReleaseNotes = "")
{
    /// <summary>
    /// Indica si existe una versión más reciente.
    /// </summary>
    public bool Available =>
        Version > GetCurrentVersion();

    /// <summary>
    /// Nombre de la versión más reciente.
    /// </summary>
    public string LatestVersion =>
        Version.ToString();

    private static Version GetCurrentVersion()
    {
        var text =
            Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

        if (string.IsNullOrWhiteSpace(text))
        {
            return new Version(1, 2, 0);
        }

        var separator = text.IndexOfAny(
            new[] { '-', '+' });

        if (separator >= 0)
        {
            text = text[..separator];
        }

        return Version.TryParse(
                text,
                out var version)
            ? version
            : new Version(1, 2, 0);
    }
}

/// <summary>
/// Comprueba, descarga y prepara la instalación de actualizaciones
/// oficiales de NOVORA.
/// </summary>
public sealed class UpdateService
{
    private const string Repository =
        "aroonvaldes-star/NOVORA-PROYECT";

    private const string ReleasesApi =
        "https://api.github.com/repos/aroonvaldes-star/NOVORA-PROYECT/releases/latest";

    private static readonly HttpClient Http =
        CreateHttpClient();

    // ============================================================
    // VERSIÓN ACTUAL
    // ============================================================

    public Version CurrentVersion
    {
        get
        {
            var text =
                Assembly.GetEntryAssembly()?
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;

            if (string.IsNullOrWhiteSpace(text))
            {
                return new Version(1, 2, 0);
            }

            var separator = text.IndexOfAny(
                new[] { '-', '+' });

            if (separator >= 0)
            {
                text = text[..separator];
            }

            return Version.TryParse(
                    text,
                    out var version)
                ? version
                : new Version(1, 2, 0);
        }
    }

    // ============================================================
    // COMPATIBILIDAD CON MAINWINDOW
    // ============================================================

    public async Task<NovoraUpdateInfo?> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        return await CheckForUpdateAsync(
            cancellationToken);
    }

    // ============================================================
    // COMPROBAR ACTUALIZACIÓN
    // ============================================================

    public async Task<NovoraUpdateInfo?> CheckForUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        using var response =
            await Http.GetAsync(
                ReleasesApi,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        using var document =
            await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);

        var root =
            document.RootElement;

        // --------------------------------------------------------
        // No aceptar drafts
        // --------------------------------------------------------

        if (root.TryGetProperty(
                "draft",
                out var draft) &&
            draft.GetBoolean())
        {
            return null;
        }

        // --------------------------------------------------------
        // No aceptar prereleases
        // --------------------------------------------------------

        if (root.TryGetProperty(
                "prerelease",
                out var prerelease) &&
            prerelease.GetBoolean())
        {
            return null;
        }

        // --------------------------------------------------------
        // TAG
        // --------------------------------------------------------

        var tag =
            root.TryGetProperty(
                    "tag_name",
                    out var tagElement)
                ? tagElement.GetString() ??
                  string.Empty
                : string.Empty;

        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var version =
            ParseVersion(tag);

        // --------------------------------------------------------
        // Comparar versiones
        // --------------------------------------------------------

        if (version <= CurrentVersion)
        {
            return null;
        }

        // --------------------------------------------------------
        // NOMBRE DE RELEASE
        // --------------------------------------------------------

        var releaseName =
            root.TryGetProperty(
                    "name",
                    out var nameElement)
                ? nameElement.GetString() ??
                  tag
                : tag;

        // --------------------------------------------------------
        // RELEASE NOTES
        // --------------------------------------------------------

        var releaseNotes =
            root.TryGetProperty(
                    "body",
                    out var bodyElement)
                ? bodyElement.GetString() ??
                  string.Empty
                : string.Empty;

        // --------------------------------------------------------
        // URL DE RELEASE
        // --------------------------------------------------------

        var releaseUrl =
            root.TryGetProperty(
                    "html_url",
                    out var htmlElement)
                ? htmlElement.GetString() ??
                  $"https://github.com/{Repository}/releases"
                : $"https://github.com/{Repository}/releases";

        // --------------------------------------------------------
        // ASSETS
        // --------------------------------------------------------

        if (!root.TryGetProperty(
                "assets",
                out var assets) ||
            assets.ValueKind !=
                JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            // ----------------------------------------------------
            // Nombre
            // ----------------------------------------------------

            var assetName =
                asset.TryGetProperty(
                        "name",
                        out var assetNameElement)
                    ? assetNameElement.GetString()
                    : null;

            // ----------------------------------------------------
            // URL
            // ----------------------------------------------------

            var urlText =
                asset.TryGetProperty(
                        "browser_download_url",
                        out var urlElement)
                    ? urlElement.GetString()
                    : null;

            // ----------------------------------------------------
            // SHA-256
            // ----------------------------------------------------

            var digestText =
                asset.TryGetProperty(
                        "digest",
                        out var digestElement)
                    ? digestElement.GetString()
                    : null;

            if (string.IsNullOrWhiteSpace(assetName) ||
                string.IsNullOrWhiteSpace(urlText))
            {
                continue;
            }

            // ----------------------------------------------------
            // Solo instaladores NOVORA
            // ----------------------------------------------------

            var isSetup =
                (
                    assetName.StartsWith(
                        "NOVORA-Setup-",
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    assetName.EndsWith(
                        ".exe",
                        StringComparison.OrdinalIgnoreCase)
                )
                ||
                assetName.EndsWith(
                    "_Setup.exe",
                    StringComparison.OrdinalIgnoreCase);

            if (!isSetup)
            {
                continue;
            }

            // ----------------------------------------------------
            // HTTPS obligatorio
            // ----------------------------------------------------

            if (!Uri.TryCreate(
                    urlText,
                    UriKind.Absolute,
                    out var downloadUri) ||
                downloadUri.Scheme !=
                    Uri.UriSchemeHttps)
            {
                continue;
            }

            // ----------------------------------------------------
            // SHA-256 obligatorio
            // ----------------------------------------------------

            if (!TryParseSha256Digest(
                    digestText,
                    out var expectedSha256))
            {
                continue;
            }

            return new NovoraUpdateInfo(
                version,
                tag,
                releaseName,
                assetName,
                expectedSha256,
                downloadUri,
                releaseUrl,
                releaseNotes);
        }

        return null;
    }

    // ============================================================
    // DESCARGAR INSTALADOR
    // ============================================================

    public async Task<string> DownloadInstallerAsync(
        NovoraUpdateInfo update,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (update.DownloadUri.Scheme !=
            Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "La actualización no utiliza HTTPS.");
        }

        var tempFolder =
            Path.Combine(
                Path.GetTempPath(),
                "NOVORA",
                "Updates");

        Directory.CreateDirectory(
            tempFolder);

        // --------------------------------------------------------
        // Validar nombre de archivo
        // --------------------------------------------------------

        if (!string.Equals(
                Path.GetFileName(update.AssetName),
                update.AssetName,
                StringComparison.Ordinal) ||
            update.AssetName.IndexOfAny(
                Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException(
                "El nombre del instalador de actualización no es válido.");
        }

        var filePath =
            Path.Combine(
                tempFolder,
                update.AssetName);

        // --------------------------------------------------------
        // Descargar
        // --------------------------------------------------------

        using var response =
            await Http.GetAsync(
                update.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var total =
            response.Content.Headers.ContentLength;

        await using var input =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        await using var output =
            new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                useAsync: true);

        var buffer =
            new byte[128 * 1024];

        long readTotal = 0;
        int read;

        while (
            (read =
                await input.ReadAsync(
                    buffer.AsMemory(
                        0,
                        buffer.Length),
                    cancellationToken)) > 0)
        {
            await output.WriteAsync(
                buffer.AsMemory(
                    0,
                    read),
                cancellationToken);

            readTotal += read;

            if (total is > 0)
            {
                progress?.Report(
                    (int)Math.Clamp(
                        readTotal * 100L /
                        total.Value,
                        0,
                        100));
            }
        }

        await output.FlushAsync(
            cancellationToken);

        // --------------------------------------------------------
        // SHA-256
        // --------------------------------------------------------

        var actualSha256 =
            await ComputeSha256Async(
                filePath,
                cancellationToken);

        if (!actualSha256.Equals(
                update.ExpectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(filePath);

            throw new InvalidDataException(
                "La actualización no coincide con la huella SHA-256 publicada por NOVORA.");
        }

        progress?.Report(100);

        return filePath;
    }

    // ============================================================
    // INSTALAR Y REINICIAR
    // ============================================================

    public async Task InstallAndRestartAsync(
        NovoraUpdateInfo update,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        // --------------------------------------------------------
        // Descargar
        // --------------------------------------------------------

        var installerPath =
            await DownloadInstallerAsync(
                update,
                progress,
                cancellationToken);

        // --------------------------------------------------------
        // PID actual
        // --------------------------------------------------------

        var processId =
            Environment.ProcessId;

        // --------------------------------------------------------
        // EXE actual
        // --------------------------------------------------------

        var currentExePath =
            Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(
                currentExePath))
        {
            TryDelete(installerPath);

            throw new InvalidOperationException(
                "NOVORA no pudo determinar la ruta de su ejecutable.");
        }

        // --------------------------------------------------------
        // Crear helper
        // --------------------------------------------------------

        var helperPath =
            CreateUpdateHelper(
                installerPath,
                currentExePath,
                processId);

        try
        {
            var helperInfo =
                new ProcessStartInfo
                {
                    FileName =
                        "powershell.exe",

                    UseShellExecute =
                        false,

                    CreateNoWindow =
                        true,

                    WindowStyle =
                        ProcessWindowStyle.Hidden
                };

            helperInfo.ArgumentList.Add(
                "-NoProfile");

            helperInfo.ArgumentList.Add(
                "-NonInteractive");

            helperInfo.ArgumentList.Add(
                "-ExecutionPolicy");

            helperInfo.ArgumentList.Add(
                "Bypass");

            helperInfo.ArgumentList.Add(
                "-File");

            helperInfo.ArgumentList.Add(
                helperPath);

            var helper =
                Process.Start(
                    helperInfo);

            if (helper is null)
            {
                throw new InvalidOperationException(
                    "NOVORA no pudo iniciar el proceso de actualización.");
            }
        }
        catch
        {
            TryDelete(helperPath);
            TryDelete(installerPath);

            throw;
        }

        // --------------------------------------------------------
        // El helper espera a que NOVORA termine.
        // --------------------------------------------------------

        Environment.Exit(0);
    }

    // ============================================================
    // CREAR HELPER
    // ============================================================

    private static string CreateUpdateHelper(
        string installerPath,
        string currentExePath,
        int processId)
    {
        var folder =
            Path.Combine(
                Path.GetTempPath(),
                "NOVORA",
                "Updates");

        Directory.CreateDirectory(
            folder);

        var helperPath =
            Path.Combine(
                folder,
                $"install-{processId}-{Guid.NewGuid():N}.ps1");

        var installer =
            installerPath.Replace(
                "'",
                "''",
                StringComparison.Ordinal);

        var executable =
            currentExePath.Replace(
                "'",
                "''",
                StringComparison.Ordinal);

        var helper =
            helperPath.Replace(
                "'",
                "''",
                StringComparison.Ordinal);

        var script = @"
$ErrorActionPreference = 'Stop'

$pidToWait = __PROCESS_ID__

$installer = '__INSTALLER__'

$novoraExe = '__NOVORA_EXE__'

$helper = '__HELPER__'

$arguments = @(
    '/VERYSILENT'
    '/SUPPRESSMSGBOXES'
    '/NORESTART'
    '/CLOSEAPPLICATIONS'
)

$errorFolder =
    Join-Path `
        $env:TEMP `
        'NOVORA'

$errorLog =
    Join-Path `
        $errorFolder `
        'update-error.log'

try {

    if (-not (Test-Path -LiteralPath $errorFolder)) {

        New-Item `
            -ItemType Directory `
            -Path $errorFolder `
            -Force |
            Out-Null
    }

    # ============================================================
    # ESPERAR A NOVORA
    # ============================================================

    $elapsed = 0

    while ($elapsed -lt 60) {

        $process =
            Get-Process `
                -Id $pidToWait `
                -ErrorAction SilentlyContinue

        if (-not $process) {
            break
        }

        Start-Sleep `
            -Seconds 1

        $elapsed++
    }

    # ============================================================
    # CERRAR SI SIGUE ABIERTO
    # ============================================================

    $process =
        Get-Process `
            -Id $pidToWait `
            -ErrorAction SilentlyContinue

    if ($process) {

        try {

            Stop-Process `
                -Id $pidToWait `
                -Force `
                -ErrorAction SilentlyContinue

        }
        catch {
        }

        Start-Sleep `
            -Seconds 2
    }

    # ============================================================
    # COMPROBAR INSTALADOR
    # ============================================================

    if (-not (
        Test-Path `
            -LiteralPath $installer `
            -PathType Leaf
    )) {

        throw (
            'No se encontró el instalador descargado: ' +
            $installer
        )
    }

    # ============================================================
    # INSTALAR
    # ============================================================

    $installProcess =
        Start-Process `
            -FilePath $installer `
            -ArgumentList $arguments `
            -Wait `
            -PassThru

    if ($installProcess.ExitCode -ne 0) {

        throw (
            'El instalador terminó con código ' +
            $installProcess.ExitCode
        )
    }

    # ============================================================
    # ESPERAR INSTALACIÓN
    # ============================================================

    Start-Sleep `
        -Seconds 2

    # ============================================================
    # INICIAR NOVORA ORIGINAL
    # ============================================================

    if (
        Test-Path `
            -LiteralPath $novoraExe `
            -PathType Leaf
    ) {

        $workingDirectory =
            Split-Path `
                -Path $novoraExe `
                -Parent

        Start-Process `
            -FilePath $novoraExe `
            -WorkingDirectory $workingDirectory
    }
    else {

        # ========================================================
        # FALLBACK
        # ========================================================

        $possiblePaths = @(
            (
                Join-Path `
                    $env:ProgramFiles `
                    'NOVORA\NOVORA.exe'
            ),
            (
                Join-Path `
                    $env:LOCALAPPDATA `
                    'Programs\NOVORA\NOVORA.exe'
            )
        )

        $foundExe = $null

        foreach ($path in $possiblePaths) {

            if (
                Test-Path `
                    -LiteralPath $path `
                    -PathType Leaf
            ) {

                $foundExe = $path
                break
            }
        }

        if ($foundExe) {

            $workingDirectory =
                Split-Path `
                    -Path $foundExe `
                    -Parent

            Start-Process `
                -FilePath $foundExe `
                -WorkingDirectory $workingDirectory
        }
        else {

            Add-Content `
                -LiteralPath $errorLog `
                -Value 'NOVORA.exe no fue encontrado después de la instalación.'
        }
    }
}
catch {

    Add-Content `
        -LiteralPath $errorLog `
        -Value (
            (
                Get-Date `
                    -Format 'yyyy-MM-dd HH:mm:ss'
            ) +
            ' - ' +
            $_.Exception.Message
        )
}
finally {

    Start-Sleep `
        -Seconds 2

    Remove-Item `
        -LiteralPath $installer `
        -Force `
        -ErrorAction SilentlyContinue

    Remove-Item `
        -LiteralPath $helper `
        -Force `
        -ErrorAction SilentlyContinue
}
";

        script =
            script.Replace(
                "__PROCESS_ID__",
                processId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

        script =
            script.Replace(
                "__INSTALLER__",
                installer,
                StringComparison.Ordinal);

        script =
            script.Replace(
                "__NOVORA_EXE__",
                executable,
                StringComparison.Ordinal);

        script =
            script.Replace(
                "__HELPER__",
                helper,
                StringComparison.Ordinal);

        File.WriteAllText(
            helperPath,
            script,
            new UTF8Encoding(false));

        return helperPath;
    }

    // ============================================================
    // SHA-256
    // ============================================================

    private static bool TryParseSha256Digest(
        string? digest,
        out string sha256)
    {
        sha256 = string.Empty;

        if (string.IsNullOrWhiteSpace(digest))
        {
            return false;
        }

        const string prefix =
            "sha256:";

        if (!digest.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value =
            digest[prefix.Length..]
                .Trim();

        if (value.Length != 64)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        sha256 = value;

        return true;
    }

    private static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream =
            new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                useAsync: true);

        using var sha =
            SHA256.Create();

        var hash =
            await sha.ComputeHashAsync(
                stream,
                cancellationToken);

        return Convert.ToHexString(hash)
            .ToLowerInvariant();
    }

    // ============================================================
    // VERSIÓN
    // ============================================================

    private static Version ParseVersion(
        string tag)
    {
        var clean =
            tag.Trim()
                .TrimStart(
                    'v',
                    'V');

        var dash =
            clean.IndexOf('-');

        if (dash >= 0)
        {
            clean =
                clean[..dash];
        }

        var plus =
            clean.IndexOf('+');

        if (plus >= 0)
        {
            clean =
                clean[..plus];
        }

        return Version.TryParse(
                clean,
                out var version)
            ? version
            : new Version(
                0,
                0,
                0);
    }

    // ============================================================
    // HTTP
    // ============================================================

    private static HttpClient CreateHttpClient()
    {
        var client =
            new HttpClient
            {
                Timeout =
                    TimeSpan.FromSeconds(30)
            };

        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "NOVORA",
                "1.2"));

        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/vnd.github+json"));

        client.DefaultRequestHeaders.Add(
            "X-GitHub-Api-Version",
            "2022-11-28");

        return client;
    }

    // ============================================================
    // LIMPIEZA
    // ============================================================

    private static void TryDelete(
        string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // No hacer nada si no puede eliminarse.
        }
    }
}
