using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace NOVORA.Services;

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
    public bool Available => Version > UpdateService.ReadCurrentVersion();
    public string LatestVersion => Version.ToString();
}

public sealed class UpdateService
{
    private const string Repository = "aroonvaldes-star/NOVORA-LINK";

    // Se consulta la lista de releases estables y se selecciona la versión
    // semántica más alta disponible. Esto evita depender del indicador
    // "latest" de GitHub para futuras versiones.
    private const string ReleasesApi =
        "https://api.github.com/repos/aroonvaldes-star/NOVORA-LINK/releases?per_page=100";

    private static readonly HttpClient Http = CreateHttpClient();

    public Version CurrentVersion => ReadCurrentVersion();

    internal static Version ReadCurrentVersion()
    {
        var text = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(text))
        {
            return new Version(1, 3, 1);
        }

        var separator = text.IndexOfAny(new[] { '-', '+' });
        if (separator >= 0)
        {
            text = text[..separator];
        }

        return Version.TryParse(text, out var version)
            ? version
            : new Version(1, 3, 1);
    }

    public Task<NovoraUpdateInfo?> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
        => CheckForUpdateAsync(cancellationToken);

    public async Task<NovoraUpdateInfo?> CheckForUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(
            ReleasesApi,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        NovoraUpdateInfo? bestUpdate = null;

        foreach (var release in root.EnumerateArray())
        {
            var isDraft =
                release.TryGetProperty("draft", out var draftNode) &&
                draftNode.ValueKind == JsonValueKind.True;

            var isPrerelease =
                release.TryGetProperty("prerelease", out var prereleaseNode) &&
                prereleaseNode.ValueKind == JsonValueKind.True;

            if (isDraft || isPrerelease)
            {
                continue;
            }

            var tag = release.TryGetProperty("tag_name", out var tagNode)
                ? tagNode.GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var version = ParseVersion(tag);
            if (version <= CurrentVersion)
            {
                continue;
            }

            var releaseName = release.TryGetProperty("name", out var nameNode)
                ? nameNode.GetString() ?? tag
                : tag;

            var releaseNotes = release.TryGetProperty("body", out var bodyNode)
                ? bodyNode.GetString() ?? string.Empty
                : string.Empty;

            var releaseUrl = release.TryGetProperty("html_url", out var urlNode)
                ? urlNode.GetString() ?? $"https://github.com/{Repository}/releases"
                : $"https://github.com/{Repository}/releases";

            if (!release.TryGetProperty("assets", out var assets) ||
                assets.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                var assetName = asset.TryGetProperty("name", out var assetNameNode)
                    ? assetNameNode.GetString()
                    : null;

                var urlText = asset.TryGetProperty(
                    "browser_download_url",
                    out var downloadNode)
                    ? downloadNode.GetString()
                    : null;

                var digestText = asset.TryGetProperty("digest", out var digestNode)
                    ? digestNode.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(assetName) ||
                    string.IsNullOrWhiteSpace(urlText))
                {
                    continue;
                }

                if (!assetName.StartsWith(
                        "NOVORA-Setup-",
                        StringComparison.OrdinalIgnoreCase) ||
                    !assetName.EndsWith(
                        ".exe",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!Uri.TryCreate(
                        urlText,
                        UriKind.Absolute,
                        out var downloadUri) ||
                    downloadUri.Scheme != Uri.UriSchemeHttps)
                {
                    continue;
                }

                if (!TryParseSha256Digest(digestText, out var expectedSha256))
                {
                    continue;
                }

                var candidate = new NovoraUpdateInfo(
                    version,
                    tag,
                    releaseName,
                    assetName,
                    expectedSha256,
                    downloadUri,
                    releaseUrl,
                    releaseNotes);

                if (bestUpdate is null || candidate.Version > bestUpdate.Version)
                {
                    bestUpdate = candidate;
                }

                break;
            }
        }

        return bestUpdate;
    }

    public async Task<string> DownloadInstallerAsync(
        NovoraUpdateInfo update,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (update.DownloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "La actualización no utiliza HTTPS.");
        }

        if (!string.Equals(
                Path.GetFileName(update.AssetName),
                update.AssetName,
                StringComparison.Ordinal) ||
            update.AssetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException(
                "El nombre del instalador no es válido.");
        }

        var folder = Path.Combine(
            Path.GetTempPath(),
            "NOVORA",
            "Updates");

        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, update.AssetName);

        using var response = await Http.GetAsync(
            update.DownloadUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;

        await using var input =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        await using var output = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            useAsync: true);

        var buffer = new byte[128 * 1024];
        long copied = 0;
        int read;

        while ((read = await input.ReadAsync(
                   buffer.AsMemory(0, buffer.Length),
                   cancellationToken)) > 0)
        {
            await output.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);

            copied += read;

            if (total is > 0)
            {
                progress?.Report((int)Math.Clamp(
                    copied * 100L / total.Value,
                    0,
                    100));
            }
        }

        await output.FlushAsync(cancellationToken);

        var actualSha256 = await ComputeSha256Async(
            path,
            cancellationToken);

        if (!actualSha256.Equals(
                update.ExpectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(path);
            throw new InvalidDataException(
                "El instalador descargado no coincide con el SHA-256 publicado.");
        }

        progress?.Report(100);
        return path;
    }

    public async Task InstallAndRestartAsync(
        NovoraUpdateInfo update,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var installer = await DownloadInstallerAsync(
            update,
            progress,
            cancellationToken);

        var info = new ProcessStartInfo
        {
            FileName = installer,
            UseShellExecute = true,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS"
        };

        if (Process.Start(info) is null)
        {
            TryDelete(installer);
            throw new InvalidOperationException(
                "NOVORA no pudo iniciar el instalador de actualización.");
        }

        Environment.Exit(0);
    }

    private static bool TryParseSha256Digest(
        string? digest,
        out string sha256)
    {
        sha256 = string.Empty;
        const string prefix = "sha256:";

        if (string.IsNullOrWhiteSpace(digest) ||
            !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = digest[prefix.Length..].Trim();

        if (value.Length != 64 ||
            value.Any(character => !Uri.IsHexDigit(character)))
        {
            return false;
        }

        sha256 = value;
        return true;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            useAsync: true);

        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Version ParseVersion(string tag)
    {
        var clean = (tag ?? string.Empty)
            .Trim()
            .TrimStart('v', 'V');

        var split = clean.IndexOfAny(new[] { '-', '+' });
        if (split >= 0)
        {
            clean = clean[..split];
        }

        return Version.TryParse(clean, out var version)
            ? version
            : new Version(0, 0, 0);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("NOVORA", "1.3.1"));

        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        client.DefaultRequestHeaders.Add(
            "X-GitHub-Api-Version",
            "2022-11-28");

        return client;
    }

    private static void TryDelete(string path)
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
        }
    }
}
